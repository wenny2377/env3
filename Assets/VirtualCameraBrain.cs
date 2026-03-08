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

        virtualCam.enabled       = false;
        virtualCam.targetDisplay = 7;
    }

    // ─────────────────────────────────────────────
    // 主入口：由 StaticCameraManager 呼叫
    // ← FIX: 加入 roomName 參數
    // ─────────────────────────────────────────────
    public IEnumerator ExecuteCaptureSequence(List<CameraNode> nodes, UserEntity target,
                                              Vector3 aimPos, string roomName = "")
    {
        if (virtualCam == null) { Debug.LogError("[VirtualCameraBrain] virtualCam 為 null"); yield break; }

        var base64List = new List<string>();
        var nameList   = new List<string>();
        var scoreList  = new List<float>();

        Debug.Log($"<color=cyan>[VirtualCameraBrain]</color> 開始拍攝，共 {nodes.Count} 個節點 | room={roomName}");

        foreach (CameraNode node in nodes)
        {
            if (node == null) continue;

            virtualCam.transform.position = node.transform.position;
            virtualCam.transform.LookAt(aimPos);

            yield return new WaitForEndOfFrame();

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

        // ← FIX: 加入 room_name（優先用傳入的 roomName，fallback 用第一個節點的 roomName）
        string finalRoomName = !string.IsNullOrEmpty(roomName)
            ? roomName
            : (nodes.Count > 0 && nodes[0] != null ? nodes[0].roomName : "");

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
            room_name        = finalRoomName,   // ← FIX: 填入房間名稱
            robot_rotation_y = 0f,
            camera_fov       = 0f
        };

        Debug.Log($"<color=cyan>[Payload]</color> room_name='{finalRoomName}' | images={payload.image_count}");

        yield return SendToBackend(payload);
    }

    // ─────────────────────────────────────────────
    // 離屏渲染
    // ─────────────────────────────────────────────
    private string RenderToBase64()
    {
        RenderTexture rt  = null;
        Texture2D     tex = null;
        try
        {
            rt = new RenderTexture(resolution, resolution, 24);
            virtualCam.targetTexture = rt;
            virtualCam.Render();

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
        Debug.Log($"<color=green>[VLM Pipeline]</color> 傳送 {payload.image_count} 張 | " +
                  $"user={payload.userID} | activity={payload.activity} | room={payload.room_name}");

        if (NetworkClient.Instance != null)
        {
            yield return NetworkClient.Instance.PostToPredict(payload, response =>
            {
                Debug.Log($"<color=lime>[AI Result]</color> user={payload.userID} → action={response}");
            });
        }
        else
        {
            yield return new WaitForSeconds(0.1f);
            Debug.LogWarning("[VirtualCameraBrain] NetworkClient 未找到，模擬傳送完成");
        }
    }
}