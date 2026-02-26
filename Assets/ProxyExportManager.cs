using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System;

public class ProxyExportManager : MonoBehaviour
{
    [Header("API Connection Settings")]
    [Tooltip("Your Python Flask /scene endpoint URL")]
    public string apiEndpoint = "http://localhost:5000/scene"; 
    
    [Header("Automation Settings")]
    [Tooltip("Automatically scan once when Play is pressed")]
    public bool scanOnStart = true;
    [Tooltip("Auto update interval (seconds), 600 seconds = 10 minutes")]
    public float updateInterval = 600f;

    [Header("Scene Structure Settings")]
    [Tooltip("Drag Proxies_Root from the Hierarchy here")]
    public Transform proxiesRoot;
    public string cameraTag = "MockCamera"; // Pivot camera tag inside the room

    [Header("Physics & Rendering Settings")]
    public LayerMask roomLayer;       // Set to RoomLayer (for RoomArea)
    public LayerMask viewLayer;       // Set to Default (furniture layer)
    public Camera snapshotCam;        // Dedicated snapshot camera
    public int resolution = 512;

    private void Start()
    {
        // 1. Automatically execute first scan after game starts
        if (scanOnStart)
        {
            Invoke("StartGlobalScan", 3f); // Delay 3 seconds to ensure scene initialization
        }

        // 2. Enable scheduled update (every X seconds)
        InvokeRepeating("StartGlobalScan", updateInterval, updateInterval);
    }

    [ContextMenu("🚀 Run Full Scene Scan Manually")]
    public void StartGlobalScan()
    {
        Debug.Log($"<color=cyan>[Scene Sync]</color> Starting scan and updating MongoDB data... Time: {DateTime.Now}");
        StartCoroutine(ExecuteScanRoutine());
    }

    private IEnumerator ExecuteScanRoutine()
    {
        if (proxiesRoot == null || snapshotCam == null)
        {
            Debug.LogError("Error: Please assign ProxiesRoot and SnapshotCam in the Inspector.");
            yield break;
        }

        GameObject[] pivots = GameObject.FindGameObjectsWithTag(cameraTag);
        if (pivots.Length == 0)
        {
            Debug.LogError("Error: No MockCamera found in the scene.");
            yield break;
        }

        List<Dictionary<string, object>> objectList = new List<Dictionary<string, object>>();

        // Iterate through all furniture proxy objects under Proxies_Root
        foreach (Transform proxy in proxiesRoot)
        {
            BoxCollider col = proxy.GetComponent<BoxCollider>();
            if (col == null) continue;

            // A. Find the closest snapshot pivot for this object
            Transform bestPivot = GetBestPivot(proxy.position, pivots);
            
            // B. Identify which room the object belongs to
            string room = IdentifyRoom(col.bounds.center);
            
            // C. Capture snapshot and convert to Base64 string
            string base64Img = CaptureFromPivot(bestPivot, col.bounds);

            // D. Format data (aligned with your Python pipeline fields)
            var entry = new Dictionary<string, object> {
                { "id", proxy.gameObject.GetInstanceID() },
                { "label", proxy.name.ToLower() }, // e.g., "LivingRoom_Sofa" -> "sofa"
                { "x", col.bounds.center.x },
                { "y", col.bounds.center.y },
                { "z", col.bounds.center.z },
                { "room", room },
                { "image", base64Img }
            };
            objectList.Add(entry);
        }

        // 3. Send JSON data to backend via WebRequest
        string json = JsonConvert.SerializeObject(new { 
            objects = objectList, 
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") 
        });
        
        yield return SendToDatabase(json);
    }

    private IEnumerator SendToDatabase(string json)
    {
        using (UnityWebRequest request = new UnityWebRequest(apiEndpoint, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
                Debug.Log($"<color=lime>[Sync Success]</color> Scene snapshot updated successfully at {DateTime.Now}.");
            else
                Debug.LogError($"<color=red>[Sync Failed]</color> Unable to connect to AI Pipeline: {request.error}");
        }
    }

    // --- Utility Functions (Private) ---

    Transform GetBestPivot(Vector3 targetPos, GameObject[] pivots)
    {
        Transform best = pivots[0].transform;
        float minDist = Vector3.Distance(targetPos, best.position);
        foreach (var p in pivots)
        {
            float dist = Vector3.Distance(targetPos, p.transform.position);
            if (dist < minDist) { minDist = dist; best = p.transform; }
        }
        return best;
    }

    string IdentifyRoom(Vector3 pos)
    {
        Collider[] hits = Physics.OverlapSphere(pos, 0.2f, roomLayer, QueryTriggerInteraction.Collide);
        foreach (var hit in hits)
        {
            RoomArea ra = hit.GetComponent<RoomArea>();
            if (ra != null) return ra.roomName;
            return hit.gameObject.name;
        }
        return "Unknown Room";
    }

    string CaptureFromPivot(Transform pivot, Bounds bounds)
    {
        snapshotCam.transform.position = pivot.position;
        snapshotCam.transform.LookAt(bounds.center);
        snapshotCam.orthographic = true;
        snapshotCam.orthographicSize = Mathf.Max(bounds.size.x, bounds.size.y) * 0.9f;
        snapshotCam.cullingMask = viewLayer;

        RenderTexture rt = new RenderTexture(resolution, resolution, 24);
        snapshotCam.targetTexture = rt;
        snapshotCam.Render();

        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
        tex.Apply();

        byte[] bytes = tex.EncodeToJPG();
        snapshotCam.targetTexture = null;
        RenderTexture.active = null;

        DestroyImmediate(tex);
        DestroyImmediate(rt);
        return Convert.ToBase64String(bytes);
    }
}