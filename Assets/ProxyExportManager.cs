using Newtonsoft.Json;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;

public class ProxyExportManager : MonoBehaviour
{
    [Header("層級結構引用")]
    [Tooltip("將 Hierarchy 中的 Proxies_Root 拖入此處")]
    public Transform proxiesRoot;

    [Header("Layer 與標籤設置")]
    public LayerMask roomLayer;       // 設為 RoomLayer
    public LayerMask viewLayer;       // 設為家具模型所在的層 (通常是 Default)
    public string cameraTag = "MockCamera"; // 監控點的 Tag 名稱

    [Header("拍照設置")]
    public Camera snapshotCam;        // 指派專用的 Snapshot Camera
    public int resolution = 512;      // 圖片解析度

    [ContextMenu("Execute Full Global Scan")]
    public void ExecuteScan()
    {
        if (proxiesRoot == null || snapshotCam == null)
        {
            Debug.LogError("錯誤：請先在 Inspector 指派 ProxiesRoot 與 SnapshotCamera！");
            return;
        }

        // 1. 取得場景中所有標記為 MockCamera 的監控點
        GameObject[] pivots = GameObject.FindGameObjectsWithTag(cameraTag);
        if (pivots.Length == 0)
        {
            Debug.LogError("錯誤：場景中找不到任何標有 'MockCamera' Tag 的物件！");
            return;
        }

        List<Dictionary<string, object>> allData = new List<Dictionary<string, object>>();

        // 2. 遍歷 Proxies_Root 下的所有子物件 (家具代理)
        foreach (Transform proxy in proxiesRoot)
        {
            BoxCollider col = proxy.GetComponent<BoxCollider>();
            if (col == null) continue;

            Debug.Log($"正在處理物件: {proxy.name}");

            // 3. 尋找距離該家具最近的監控點
            Transform bestPivot = GetBestPivot(proxy.position, pivots);

            // 4. 判定家具所在的房間
            string room = IdentifyRoom(col.bounds.center);

            // 5. 將相機移至監控點，對準家具拍照
            string base64Img = CaptureFromPivot(bestPivot, col.bounds);

            // 6. 封裝成 MongoDB 格式
            var entry = new Dictionary<string, object> {
                { "instance_id", proxy.gameObject.GetInstanceID().ToString() },
                { "label", proxy.name.Split('_')[0] }, // 例如 bed_01 取 bed
                { "pos", new { x = col.bounds.center.x, y = col.bounds.center.y, z = col.bounds.center.z } },
                { "room_name", room },
                { "image_data", base64Img },
                { "timestamp", DateTimeOffset.Now.ToUnixTimeMilliseconds() }
            };
            allData.Add(entry);
        }

        // 7. 儲存 JSON 檔案至專案 Assets 資料夾
        string savePath = Application.dataPath + "/scene_snapshots.json";
        File.WriteAllText(savePath, Newtonsoft.Json.JsonConvert.SerializeObject(allData, Newtonsoft.Json.Formatting.Indented));

        Debug.Log($"<color=green>掃描成功！資料已存至: {savePath}</color>");
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
    }

    // 尋找最近監控點的邏輯
    Transform GetBestPivot(Vector3 targetPos, GameObject[] pivots)
    {
        Transform best = pivots[0].transform;
        float minDist = Vector3.Distance(targetPos, best.position);

        foreach (var p in pivots)
        {
            float dist = Vector3.Distance(targetPos, p.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                best = p.transform;
            }
        }
        return best;
    }

    // 房間判定邏輯
    string IdentifyRoom(Vector3 pos)
    {
        // 發射一個微型球體偵測 RoomLayer 的 Trigger
        Collider[] hits = Physics.OverlapSphere(pos, 0.1f, roomLayer, QueryTriggerInteraction.Collide);
        foreach (var hit in hits)
        {
            RoomArea ra = hit.GetComponent<RoomArea>();
            if (ra != null) return ra.roomName;
            return hit.gameObject.name; // 若無腳本則回傳物件名
        }
        return "Unknown Room";
    }

    // 核心拍照邏輯
    string CaptureFromPivot(Transform pivot, Bounds bounds)
    {
        // A. 配置相機位置與朝向
        snapshotCam.transform.position = pivot.position;
        snapshotCam.transform.LookAt(bounds.center);

        // B. 自動對焦：根據物件大小調整 Orthographic Size
        snapshotCam.orthographic = true;
        snapshotCam.orthographicSize = Mathf.Max(bounds.size.x, bounds.size.y) * 0.8f;

        // 只拍模型層，不拍 Proxy 層
        snapshotCam.cullingMask = viewLayer;

        // C. 渲染流程
        RenderTexture rt = new RenderTexture(resolution, resolution, 24);
        snapshotCam.targetTexture = rt;
        snapshotCam.Render();

        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
        tex.Apply();

        // D. 清理與轉換
        byte[] bytes = tex.EncodeToPNG();
        snapshotCam.targetTexture = null;
        RenderTexture.active = null;
        DestroyImmediate(tex);
        DestroyImmediate(rt);

        return Convert.ToBase64String(bytes);
    }
}