using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 虛擬相機執行者（最終版）
/// 職責：接收 CameraNode 清單 → 離屏渲染 → 封裝 MultiImagePayload → 傳送後端
/// 使用 SharedPayload.cs 的 MultiImagePayload，不在這裡重複定義
/// </summary>
public class VirtualCameraBrain : MonoBehaviour
{
    public static VirtualCameraBrain Instance;

    [Header("虛擬相機（必填）")]
    [Tooltip("場景中專用隱形相機，不要用 Main Camera")]
    public Camera virtualCam;

    [Header("渲染設定")]
    [Tooltip("512 對 VLM 辨識已足夠，需要更高品質可調至 768")]
    public int resolution = 512;

    [Range(50, 100)]
    [Tooltip("JPEG 壓縮品質，75 是品質與封包大小的平衡點")]
    public int jpegQuality = 75;

    void Awake()
    {
        Instance = this;

        if (virtualCam == null)
        {
            Debug.LogError("[VirtualCameraBrain] 請在 Inspector 拖入專用 VirtualCamera！");
            return;
        }

        // 確保不自動渲染到主畫面
        virtualCam.enabled       = false;
        virtualCam.targetDisplay = 7; // Display 7 不存在，確保不輸出到螢幕
    }

    // ─────────────────────────────────────────────
    // 主入口：由 StaticCameraManager 呼叫
    // ─────────────────────────────────────────────
    public IEnumerator ExecuteCaptureSequence(List<CameraNode> nodes, UserEntity target, Vector3 aimPos)
    {
        if (virtualCam == null) { Debug.LogError("[VirtualCameraBrain] virtualCam 為 null"); yield break; }

        var base64List  = new List<string>();
        var nameList    = new List<string>();
        var scoreList   = new List<float>();

        Debug.Log($"<color=cyan>[VirtualCameraBrain]</color> 開始拍攝，共 {nodes.Count} 個節點");

        foreach (CameraNode node in nodes)
        {
            if (node == null) continue;

            // Step 1：瞬移到節點位置並朝向目標
            virtualCam.transform.position = node.transform.position;
            virtualCam.transform.LookAt(aimPos);

            // Step 2：等幀結束確保場景已更新
            yield return new WaitForEndOfFrame();

            // Step 3：離屏渲染 → Base64
            string b64 = RenderToBase64();
            if (b64 == null)
            {
                Debug.LogWarning($"[VirtualCameraBrain] {node.nodeName} 渲染失敗，略過");
                continue;
            }

            base64List.Add(b64);
            nameList.Add(node.nodeName);
            scoreList.Add(node.lastScore);

            Debug.Log($"<color=cyan>[Captured]</color> {node.nodeName} | Room: {node.roomName} | Score: {node.lastScore:F2}");
        }

        if (base64List.Count == 0)
        {
            Debug.LogWarning("[VirtualCameraBrain] 無有效影像，放棄傳送");
            yield break;
        }

        // Step 4：封裝 Payload（使用 SharedPayload.cs 的 MultiImagePayload）
        MultiImagePayload payload = new MultiImagePayload
        {
            image_list       = base64List.ToArray(),
            source_nodes     = nameList.ToArray(),
            node_scores      = scoreList.ToArray(),
            image_count      = base64List.Count,
            userID           = target.userID,
            activity         = target.currentActivity,
            user_pos         = new Vector3_Data(target.transform.position),
            timestamp        = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            robot_rotation_y = 0f,
            camera_fov       = 0f
            // robot_pos 留 null（定點相機模式不填）
        };

        // Step 5：傳送
        yield return SendToBackend(payload);
    }

    // ─────────────────────────────────────────────
    // 離屏渲染：GPU 緩衝 → CPU 讀回 → JPEG → Base64
    // ─────────────────────────────────────────────
    private string RenderToBase64()
    {
        RenderTexture rt  = null;
        Texture2D     tex = null;
        try
        {
            rt = new RenderTexture(resolution, resolution, 24);
            virtualCam.targetTexture = rt;
            virtualCam.Render();                    // 手動觸發（enabled=false 不會自動渲染）

            RenderTexture.active = rt;
            tex = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            tex.Apply();

            return Convert.ToBase64String(tex.EncodeToJPG(jpegQuality));
        }
        catch (Exception e)
        {
            Debug.LogError($"[VirtualCameraBrain] 渲染錯誤：{e.Message}");
            return null;
        }
        finally
        {
            // 必須清理，避免 GPU 記憶體洩漏
            virtualCam.targetTexture = null;
            RenderTexture.active     = null;
            if (rt  != null) Destroy(rt);
            if (tex != null) Destroy(tex);
        }
    }

    // ─────────────────────────────────────────────
    // 傳送至 Flask 後端
    // ─────────────────────────────────────────────
    private IEnumerator SendToBackend(MultiImagePayload payload)
    {
        Debug.Log($"<color=green>[VLM Pipeline]</color> 傳送 {payload.image_count} 張 | user={payload.userID} | activity={payload.activity}");

        if (NetworkClient.Instance != null)
        {
            yield return NetworkClient.Instance.PostToPredict(payload, response =>
            {
                Debug.Log($"<color=lime>[AI Result]</color> user={payload.userID} → action={response}");
            });
        }
        else
        {
            // NetworkClient 不存在時模擬（開發階段用）
            yield return new WaitForSeconds(0.1f);
            Debug.LogWarning("[VirtualCameraBrain] NetworkClient 未找到，模擬傳送完成");
        }
    }
}