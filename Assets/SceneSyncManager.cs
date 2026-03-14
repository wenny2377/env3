using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// SceneSyncManager — 場景靜態家具同步到 MongoDB
///
/// 職責：
///   掃描 Proxies_Root 下所有帶 BoxCollider 的代理物件，
///   對每個物件：找最近的 MockCamera 拍快照 → 辨識房間 → 組 JSON → POST /scene
///
/// 觸發方式：
///   A. scanOnStart=true → Play 後延 3 秒自動執行一次
///   B. updateInterval   → 每 N 秒定期重掃（預設 600s = 10 分鐘）
///   C. Inspector 右鍵 → "Run Full Scene Scan Manually"
///
/// 依賴：
///   - Newtonsoft.Json（Unity 套件管理器安裝）
///   - RoomArea.cs（OverlapSphere 查房間名稱）
///
/// GameObject 掛載位置：[_System] / SceneSyncManager
///
/// Proxy 命名規範：
///   Proxies_Root 下的子物件名稱即為 label（自動 ToLower()）
///   建議命名：Sofa, DeskChair, WaterDispenser, Desk
///   傳到後端後自動變成 "sofa", "deskchair", "waterdispenser", "desk"
///
/// MockCamera Tag 設定：
///   在 Nodes_LivingRoom / Nodes_Study 下的節點加上 Tag = "MockCamera"
///   讓 GetBestPivot() 能找到最近的拍照視角
///   注意：這些節點不需要掛 Camera 元件，只需要 Transform + Tag
/// </summary>
public class SceneSyncManager : MonoBehaviour
{
    // ══════════════════════════════════════════════════════
    // Inspector 欄位
    // ══════════════════════════════════════════════════════

    [Header("API 設定")]
    [Tooltip("Flask /scene 端點 URL")]
    public string apiEndpoint = "http://localhost:5000/scene";

    [Header("執行控制")]
    [Tooltip("Play 後自動執行第一次掃描")]
    public bool scanOnStart = true;

    [Tooltip("定期重掃間隔（秒）。0 = 不重掃\n600 = 每 10 分鐘同步一次")]
    public float updateInterval = 600f;

    [Header("場景結構")]
    [Tooltip("把 Hierarchy 中的 Proxies_Root 拖入這裡")]
    public Transform proxiesRoot;

    [Tooltip("MockCamera 節點的 Tag 名稱（拍攝視角用）")]
    public string mockCameraTag = "MockCamera";

    [Header("物理層設定")]
    [Tooltip("設為 RoomLayer（RoomArea BoxCollider 所在層）")]
    public LayerMask roomLayer;

    [Tooltip("設為家具所在層（快照 cullingMask）")]
    public LayerMask furnitureLayer;

    [Header("快照相機")]
    [Tooltip("專用快照相機，不是場景主相機\n建議掛在 [_System] / SceneSyncManager 下")]
    public Camera snapshotCam;

    [Tooltip("快照解析度（正方形，建議 256~512）")]
    public int resolution = 512;

    // ══════════════════════════════════════════════════════
    // Unity 生命週期
    // ══════════════════════════════════════════════════════

    void Start()
    {
        if (scanOnStart)
            Invoke(nameof(StartGlobalScan), 3f);   // 延 3 秒等場景初始化

        if (updateInterval > 0f)
            InvokeRepeating(nameof(StartGlobalScan), updateInterval, updateInterval);
    }

    // ══════════════════════════════════════════════════════
    // 對外 API
    // ══════════════════════════════════════════════════════

    /// <summary>手動觸發（Inspector 右鍵選單 / 其他腳本呼叫）</summary>
    [ContextMenu("Run Full Scene Scan")]
    public void StartGlobalScan()
    {
        Debug.Log($"[SceneSync] 開始掃描 | {DateTime.Now:HH:mm:ss}");
        StartCoroutine(ScanRoutine());
    }

    // ══════════════════════════════════════════════════════
    // 核心掃描流程
    // ══════════════════════════════════════════════════════

    IEnumerator ScanRoutine()
    {
        if (proxiesRoot == null)
        {
            Debug.LogError("[SceneSync] proxiesRoot 未設定！請把 Proxies_Root 拖入 Inspector");
            yield break;
        }
        if (snapshotCam == null)
        {
            Debug.LogError("[SceneSync] snapshotCam 未設定！請把快照相機拖入 Inspector");
            yield break;
        }

        // 找所有 MockCamera 視角節點
        GameObject[] pivots = GameObject.FindGameObjectsWithTag(mockCameraTag);
        if (pivots.Length == 0)
        {
            Debug.LogError($"[SceneSync] 找不到 Tag='{mockCameraTag}' 的節點！" +
                           "\n請在 CameraNode 上設定 Tag，或確認 mockCameraTag 欄位正確");
            yield break;
        }

        var objectList = new List<Dictionary<string, object>>();

        // 逐一掃描 Proxies_Root 的子物件
        foreach (Transform proxy in proxiesRoot)
        {
            BoxCollider col = proxy.GetComponent<BoxCollider>();
            if (col == null) continue;

            // A. 最近的拍攝視角
            Transform bestPivot = GetBestPivot(proxy.position, pivots);

            // B. 辨識所在房間
            string room = IdentifyRoom(col.bounds.center);

            // C. 拍快照 → Base64 JPEG
            string base64Img = CaptureFromPivot(bestPivot, col.bounds);

            // D. 組資料（對齊 Python pipeline 欄位）
            objectList.Add(new Dictionary<string, object>
            {
                { "id",    proxy.gameObject.GetInstanceID() },
                { "label", proxy.name.ToLower() },        // "Sofa" → "sofa"
                { "x",     col.bounds.center.x },
                { "y",     col.bounds.center.y },
                { "z",     col.bounds.center.z },
                { "room",  room },
                { "image", base64Img }
            });

            // 每處理一個物件讓出一幀，避免卡頓
            yield return null;
        }

        string json = JsonConvert.SerializeObject(new
        {
            objects = objectList,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });

        yield return StartCoroutine(PostToBackend(json));
    }

    // ══════════════════════════════════════════════════════
    // POST 到後端
    // ══════════════════════════════════════════════════════

    IEnumerator PostToBackend(string json)
    {
        using var req = new UnityWebRequest(apiEndpoint, "POST");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
            Debug.Log($"[SceneSync] ✓ 同步成功 | {DateTime.Now:HH:mm:ss}");
        else
            Debug.LogWarning($"[SceneSync] ✗ 同步失敗: {req.error}\n" +
                             $"URL={apiEndpoint}  Flask 是否正在執行？");
    }

    // ══════════════════════════════════════════════════════
    // 輔助方法
    // ══════════════════════════════════════════════════════

    /// <summary>找距離目標最近的 MockCamera 節點</summary>
    Transform GetBestPivot(Vector3 targetPos, GameObject[] pivots)
    {
        Transform best = pivots[0].transform;
        float minDist = Vector3.Distance(targetPos, best.position);

        foreach (var p in pivots)
        {
            float d = Vector3.Distance(targetPos, p.transform.position);
            if (d < minDist) { minDist = d; best = p.transform; }
        }
        return best;
    }

    /// <summary>
    /// OverlapSphere 找 RoomArea，回傳 roomName
    /// 需要 RoomArea 所在 GameObject 設為 roomLayer
    /// </summary>
    string IdentifyRoom(Vector3 pos)
    {
        Collider[] hits = Physics.OverlapSphere(pos, 0.2f, roomLayer,
                                                QueryTriggerInteraction.Collide);
        foreach (var hit in hits)
        {
            RoomArea ra = hit.GetComponent<RoomArea>();
            if (ra != null) return ra.roomName;
        }
        return "Unknown";
    }

    /// <summary>
    /// 把快照相機移到 pivot 位置，對準家具包圍盒中心
    /// 用正交相機確保家具完整入鏡（不受透視畸變影響）
    /// 回傳 Base64 JPEG 字串
    /// </summary>
    string CaptureFromPivot(Transform pivot, Bounds bounds)
    {
        // 移相機
        snapshotCam.transform.position = pivot.position;
        snapshotCam.transform.LookAt(bounds.center);

        // 正交模式：大小剛好包住家具
        snapshotCam.orthographic = true;
        snapshotCam.orthographicSize =
            Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * 0.6f;
        snapshotCam.cullingMask = furnitureLayer;

        // 渲染
        RenderTexture rt = new RenderTexture(resolution, resolution, 24);
        snapshotCam.targetTexture = rt;
        snapshotCam.Render();

        // 讀取
        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
        tex.Apply();

        byte[] bytes = tex.EncodeToJPG(85);   // JPEG 85% 品質，比 PNG 小很多

        // 清理
        snapshotCam.targetTexture = null;
        RenderTexture.active = null;
        DestroyImmediate(tex);
        DestroyImmediate(rt);

        return Convert.ToBase64String(bytes);
    }
}