using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// VirtualCameraBrain — 虛擬節點多視角連拍
///
/// ┌─────────────────────────────────────────────────────┐
/// │  硬體限制：只有 1 台真實 Camera                       │
/// │  模擬目標：N 台固定相機同時拍攝的效果                  │
/// │                                                     │
/// │  做法：依序瞬移單台相機到每個虛擬節點位置拍攝           │
/// │       → 收集 topN 張 → 一次 POST                    │
/// │                                                     │
/// │  時間成本：每張需要 captureWaitFrames 幀              │
/// │  topN=2, captureWaitFrames=2, 60fps                 │
/// │  → 拍攝總耗時 ≈ 2×2/60 ≈ 67ms，可接受               │
/// └─────────────────────────────────────────────────────┘
///
/// 流程：
///   StaticCameraManager 呼叫 ExecuteMultiCapture(user, sortedNodes, activity)
///   sortedNodes 已通過 FOV 硬截斷 + 門檻篩選，由高分到低分排序
///   → 依序瞬移相機到前 topN 個節點
///   → 每個位置等 captureWaitFrames 幀讓渲染刷新
///   → 截圖 → 收集到 image_list[]
///   → 全部截完後一次 POST 到 /predict
///   → 還原相機位置
///
/// POST /predict JSON 格式：
///   {
///     "user_id":      "User_Mom",
///     "activity":     "Drink",
///     "virtual_hour": 7.0,
///     "image_count":  2,
///     "image_list":   ["<base64 PNG>", "<base64 PNG>"],
///     "source_nodes": ["LR_NW", "LR_NE"],
///     "node_scores":  [0.82, 0.71]
///   }
///
/// Flask 端處理建議：
///   A. 最高信心度優先：用 node_scores 加權，取信心最高的識別結果
///   B. 多數決：各圖各跑一次 VLM，投票決定最終 action
///   C. 相容舊版：只用 image_list[0]，忽略其餘視角
///
/// Inspector topN 設定建議：
///   1 → 單張，與舊版相容，可作為 Baseline
///   2 → 最佳 + 次佳，推薦（平衡速度與準確率）
///   3 → 前三台，適合房間有 4 個節點時
///   4 → 全部節點，最接近真實但 POST 資料量大
/// </summary>
public class VirtualCameraBrain : MonoBehaviour
{
    // ══════════════════════════════════════════════════════
    // Inspector 欄位
    // ══════════════════════════════════════════════════════

    [Header("唯一真實相機（必填）")]
    [Tooltip("場景中掛 Camera 元件的 GameObject\n" +
             "CameraNode 是虛擬評分節點，不需要 Camera 元件")]
    public Camera mainCamera;

    [Header("Flask 端點")]
    public string predictUrl = "http://127.0.0.1:5000/predict";

    [Header("多視角連拍數量")]
    [Tooltip("每次行為拍幾個虛擬視角\n" +
             "1 = 單張（舊版相容 / Baseline）\n" +
             "2 = 推薦（最佳＋次佳，67ms 額外延遲）\n" +
             "3~4 = 更完整，但需要更多幀時間")]
    [Range(1, 4)]
    public int topN = 2;

    [Header("每個視角截圖前等幾幀")]
    [Tooltip("瞬移相機後需要等渲染管線刷新\n" +
             "建議 1~2 幀（60fps 下每幀 ~16ms）\n" +
             "太少：相機還沒渲染新位置就截圖（畫面錯誤）\n" +
             "太多：截圖流程變慢")]
    [Range(1, 4)]
    public int captureWaitFrames = 2;

    [Header("截圖解析度（正方形）")]
    [Tooltip("建議 512×512\nVLM 夠用，Base64 大小約 200~400KB 每張")]
    public int renderWidth  = 512;
    public int renderHeight = 512;

    [Header("截圖後復原相機")]
    [Tooltip("勾選：全部截完後把相機移回 Play 開始時的位置\n" +
             "不勾：相機停在最後一個節點位置")]
    public bool restoreCamera = true;

    // ══════════════════════════════════════════════════════
    // 私有成員
    // ══════════════════════════════════════════════════════

    float      virtualHour   = -1f;
    Vector3    originalPos;
    Quaternion originalRot;
    bool       originalSaved = false;

    // ══════════════════════════════════════════════════════
    // Unity 生命週期
    // ══════════════════════════════════════════════════════

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera == null)
        {
            Debug.LogError("[VirtualCameraBrain] 找不到 mainCamera！\n" +
                           "請把場景中唯一的 Camera GameObject 拖入 Inspector");
            return;
        }

        // 記錄原始位置，截圖後還原用
        originalPos   = mainCamera.transform.position;
        originalRot   = mainCamera.transform.rotation;
        originalSaved = true;

        Debug.Log($"[VirtualCameraBrain] 初始化完成 | topN={topN} | " +
                  $"captureWaitFrames={captureWaitFrames} | 解析度={renderWidth}×{renderHeight}");
    }

    // ══════════════════════════════════════════════════════
    // 對外 API
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// ExperimentRunner 在每個 time slot 開始前呼叫
    /// POST 時帶入這個時間，後端記錄到 virtual_hour 欄位
    /// </summary>
    public void SetVirtualHour(float hour) => virtualHour = hour;

    /// <summary>
    /// 多視角連拍主流程（StaticCameraManager.CaptureWithFallback 呼叫）
    ///
    /// sortedNodes：已通過 FOV 硬截斷 + 門檻篩選、由高分到低分排序的節點清單
    ///              StaticCameraManager.ScoreCamerasRanked() 的輸出
    /// </summary>
    public IEnumerator ExecuteMultiCapture(
        UserEntity       user,
        List<CameraNode> sortedNodes,
        string           activity)
    {
        if (mainCamera == null)
        {
            Debug.LogError("[VirtualCameraBrain] mainCamera 為 null，無法截圖");
            yield break;
        }

        if (sortedNodes == null || sortedNodes.Count == 0)
        {
            Debug.LogWarning("[VirtualCameraBrain] sortedNodes 為空，跳過");
            yield break;
        }

        int captureCount = Mathf.Min(topN, sortedNodes.Count);

        var imageList  = new List<string>();   // Base64 PNG
        var nodeNames  = new List<string>();   // 節點名稱（對應 image_list 順序）
        var nodeScores = new List<float>();    // 節點分數

        // ── 逐一瞬移相機到每個節點並截圖 ─────────────────────
        for (int i = 0; i < captureCount; i++)
        {
            CameraNode node = sortedNodes[i];

            // 1. 瞬移真實相機到虛擬節點位置
            mainCamera.transform.position = node.transform.position;
            mainCamera.transform.rotation = node.transform.rotation;

            // 2. 等幾幀讓渲染管線從新位置輸出
            //    WaitForEndOfFrame 確保在當幀渲染完成後才截圖
            for (int f = 0; f < captureWaitFrames; f++)
                yield return new WaitForEndOfFrame();

            // 3. 建立 RenderTexture，強制渲染一次
            RenderTexture rt = new RenderTexture(renderWidth, renderHeight, 24);
            mainCamera.targetTexture = rt;
            mainCamera.Render();   // 強制渲染，不等下一幀

            // 4. 讀取像素 → PNG → Base64
            RenderTexture.active = rt;
            var tex = new Texture2D(renderWidth, renderHeight, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, renderWidth, renderHeight), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            imageList.Add(System.Convert.ToBase64String(tex.EncodeToPNG()));
            nodeNames.Add(node.nodeName);
            nodeScores.Add(node.lastScore);

            // 5. 釋放資源
            mainCamera.targetTexture = null;
            Destroy(rt);
            Destroy(tex);

            Debug.Log($"[VirtualCameraBrain] [{i + 1}/{captureCount}] " +
                      $"節點={node.nodeName} 分數={node.lastScore:F2}");
        }

        // ── 還原相機位置 ───────────────────────────────────────
        if (restoreCamera && originalSaved)
        {
            mainCamera.transform.position = originalPos;
            mainCamera.transform.rotation = originalRot;
        }

        // ── 一次 POST 全部截圖（fire-and-forget）────────────────
        // 不 yield return → 截圖完成後立即返回給呼叫方
        // → ExperimentRunner 不等 Flask 回應就繼續下一個 episode
        // → Flask 沒開的情況下動畫照常執行，只有 log 警告
        StartCoroutine(PostMultiImage(user, activity, imageList, nodeNames, nodeScores));
    }

    /// <summary>
    /// 單張相容介面（StaticCameraManager 舊介面 / topN=1 時使用）
    /// 直接包裝 ExecuteMultiCapture，傳入只有一個節點的清單
    /// </summary>
    public IEnumerator ExecuteCaptureSequence(
        UserEntity user, CameraNode camNode, string activity)
    {
        yield return StartCoroutine(
            ExecuteMultiCapture(user, new List<CameraNode> { camNode }, activity));
    }

    // ══════════════════════════════════════════════════════
    // POST
    // ══════════════════════════════════════════════════════

    IEnumerator PostMultiImage(
        UserEntity   user,
        string       activity,
        List<string> imageList,
        List<string> nodeNames,
        List<float>  nodeScores)
    {
        float hour = virtualHour >= 0f
            ? virtualHour
            : (float)System.DateTime.Now.Hour;

        // room_name：從第一個節點名稱推算
        // 例如 "Kitchen_Cam1" → "Kitchen"，"LivingRoom_Cam2" → "LivingRoom"
        string roomName = "";
        if (nodeNames.Count > 0)
        {
            int camIdx = nodeNames[0].LastIndexOf("_Cam");
            roomName = camIdx > 0 ? nodeNames[0].Substring(0, camIdx) : nodeNames[0];
        }

        // Flask /predict 期望的欄位名稱：
        //   userID（Flask: data.get('userID')）
        //   activity, room_name, virtual_hour
        //   image_list, image_count, source_nodes, node_scores
        string json = "{"
            + $"\"userID\":\"{Esc(user.userID)}\","
            + $"\"activity\":\"{Esc(activity)}\","
            + $"\"room_name\":\"{Esc(roomName)}\","
            + $"\"virtual_hour\":{hour.ToString("F1", InvCulture)},"
            + $"\"image_count\":{imageList.Count},"
            + $"\"image_list\":{StrArrayJson(imageList)},"
            + $"\"source_nodes\":{StrArrayJson(nodeNames)},"
            + $"\"node_scores\":{FloatArrayJson(nodeScores)}"
            + "}";

        using var req = new UnityWebRequest(predictUrl, "POST");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 30;   // VLM 推理可能需要較長時間

        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
            Debug.Log($"[VirtualCameraBrain] ✓ POST OK | {user.userID} | " +
                      $"{activity} | {imageList.Count} 張 | hour={hour}");
        else
            Debug.LogWarning($"[VirtualCameraBrain] ✗ POST 失敗: {req.error}\n" +
                             $"URL={predictUrl}  確認 Flask 是否在執行");
    }

    // ══════════════════════════════════════════════════════
    // JSON 輔助（不依賴 Newtonsoft）
    // ══════════════════════════════════════════════════════

    static readonly System.Globalization.CultureInfo InvCulture =
        System.Globalization.CultureInfo.InvariantCulture;

    static string Esc(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    static string StrArrayJson(List<string> list)
    {
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(Esc(list[i])).Append('"');
        }
        return sb.Append(']').ToString();
    }

    static string FloatArrayJson(List<float> list)
    {
        var parts = new List<string>();
        foreach (var f in list)
            parts.Add(f.ToString("F4", InvCulture));
        return "[" + string.Join(",", parts) + "]";
    }
}