using Newtonsoft.Json;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;

public class ProxyExportManager : MonoBehaviour
{
    [Header("Hierarchy Structure Settings")]
    [Tooltip("Drag the Proxies_Root object from the Hierarchy here")]
    public Transform proxiesRoot;

    [Header("Layer and Tag Settings")]
    public LayerMask roomLayer;       // Set as RoomLayer
    public LayerMask viewLayer;       // Layer where scene objects are visible (usually Default)
    public string cameraTag = "MockCamera"; // Tag name of pivot camera objects

    [Header("Snapshot Settings")]
    public Camera snapshotCam;        // Dedicated snapshot camera
    public int resolution = 512;      // Image resolution

    [ContextMenu("Execute Full Global Scan")]
    public void ExecuteScan()
    {
        if (proxiesRoot == null || snapshotCam == null)
        {
            Debug.LogError("Error: Please assign ProxiesRoot and SnapshotCamera in the Inspector.");
            return;
        }

        // 1. Find all pivot objects tagged as MockCamera
        GameObject[] pivots = GameObject.FindGameObjectsWithTag(cameraTag);
        if (pivots.Length == 0)
        {
            Debug.LogError("Error: No objects with the tag 'MockCamera' were found in the scene.");
            return;
        }

        List<Dictionary<string, object>> allData = new List<Dictionary<string, object>>();

        // 2. Iterate through all child objects under Proxies_Root (scene proxies)
        foreach (Transform proxy in proxiesRoot)
        {
            BoxCollider col = proxy.GetComponent<BoxCollider>();
            if (col == null) continue;

            Debug.Log($"Processing object: {proxy.name}");

            // 3. Find the closest pivot to this object
            Transform bestPivot = GetBestPivot(proxy.position, pivots);

            // 4. Determine which room the object belongs to
            string room = IdentifyRoom(col.bounds.center);

            // 5. Move camera to pivot and capture snapshot
            string base64Img = CaptureFromPivot(bestPivot, col.bounds);

            // 6. Package data in MongoDB-style format
            var entry = new Dictionary<string, object> {
                { "instance_id", proxy.gameObject.GetInstanceID().ToString() },
                { "label", proxy.name.Split('_')[0] }, // e.g. bed_01 → bed
                { "pos", new { x = col.bounds.center.x, y = col.bounds.center.y, z = col.bounds.center.z } },
                { "room_name", room },
                { "image_data", base64Img },
                { "timestamp", DateTimeOffset.Now.ToUnixTimeMilliseconds() }
            };

            allData.Add(entry);
        }

        // 7. Save JSON file to the project's Assets folder
        string savePath = Application.dataPath + "/scene_snapshots.json";
        File.WriteAllText(savePath, JsonConvert.SerializeObject(allData, Formatting.Indented));

        Debug.Log($"<color=green>Export complete! Data saved to: {savePath}</color>");

#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
    }

    // Find the closest pivot helper method
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

    // Room identification helper method
    string IdentifyRoom(Vector3 pos)
    {
        // Use a small overlap sphere to detect RoomLayer triggers
        Collider[] hits = Physics.OverlapSphere(pos, 0.1f, roomLayer, QueryTriggerInteraction.Collide);

        foreach (var hit in hits)
        {
            RoomArea ra = hit.GetComponent<RoomArea>();
            if (ra != null) return ra.roomName;

            // If no RoomArea script is attached, return the GameObject name
            return hit.gameObject.name;
        }

        return "Unknown Room";
    }

    // Core snapshot function
    string CaptureFromPivot(Transform pivot, Bounds bounds)
    {
        // A. Set camera position and orientation
        snapshotCam.transform.position = pivot.position;
        snapshotCam.transform.LookAt(bounds.center);

        // B. Automatically adjust orthographic size based on object size
        snapshotCam.orthographic = true;
        snapshotCam.orthographicSize = Mathf.Max(bounds.size.x, bounds.size.y) * 0.8f;

        // Render only the specified view layer (exclude proxy layer)
        snapshotCam.cullingMask = viewLayer;

        // C. Render to texture
        RenderTexture rt = new RenderTexture(resolution, resolution, 24);
        snapshotCam.targetTexture = rt;
        snapshotCam.Render();

        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
        tex.Apply();

        // D. Cleanup and encode
        byte[] bytes = tex.EncodeToPNG();
        snapshotCam.targetTexture = null;
        RenderTexture.active = null;

        DestroyImmediate(tex);
        DestroyImmediate(rt);

        return Convert.ToBase64String(bytes);
    }
}