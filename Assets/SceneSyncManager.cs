using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public class SceneSyncManager : MonoBehaviour
{
    [Header("API Settings")]
    public string apiEndpoint = "http://localhost:5000/scene";

    [Header("Execution Control")]
    public bool  scanOnStart    = true;
    public float updateInterval = 600f;

    [Header("Scene Structure")]
    public Transform proxiesRoot;
    public string    mockCameraTag = "MockCamera";

    [Header("Physics Layer Settings")]
    public LayerMask roomLayer;
    public LayerMask furnitureLayer;

    [Header("Snapshot Camera")]
    public Camera snapshotCam;
    public int    resolution = 512;

    void Start()
    {
        if (scanOnStart)
            Invoke(nameof(StartGlobalScan), 3f);

        if (updateInterval > 0f)
            InvokeRepeating(nameof(StartGlobalScan), updateInterval, updateInterval);
    }

    [ContextMenu("Run Full Scene Scan")]
    public void StartGlobalScan()
    {
        Debug.Log($"[SceneSync] Scan started | {DateTime.Now:HH:mm:ss}");
        StartCoroutine(ScanRoutine());
    }

    IEnumerator ScanRoutine()
    {
        if (proxiesRoot == null)
        {
            Debug.LogError("[SceneSync] proxiesRoot is not assigned.");
            yield break;
        }
        if (snapshotCam == null)
        {
            Debug.LogError("[SceneSync] snapshotCam is not assigned.");
            yield break;
        }

        GameObject[] pivots = GameObject.FindGameObjectsWithTag(mockCameraTag);
        if (pivots.Length == 0)
        {
            Debug.LogError($"[SceneSync] No GameObjects with Tag='{mockCameraTag}' found.");
            yield break;
        }

        var objectList = new List<Dictionary<string, object>>();

        foreach (Transform proxy in proxiesRoot)
        {
            BoxCollider col = proxy.GetComponent<BoxCollider>();
            if (col == null) continue;

            Transform bestPivot = GetBestPivot(proxy.position, pivots);
            string    room      = IdentifyRoom(col.bounds.center);
            string    base64Img = CaptureFromPivot(bestPivot, col.bounds);

            objectList.Add(new Dictionary<string, object>
            {
                { "id",    proxy.gameObject.GetInstanceID() },
                { "label", proxy.name.ToLower() },
                { "x",     col.bounds.center.x },
                { "y",     col.bounds.center.y },
                { "z",     col.bounds.center.z },
                { "room",  room },
                { "image", base64Img }
            });

            yield return null;
        }

        string json = JsonConvert.SerializeObject(new
        {
            objects   = objectList,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });

        yield return StartCoroutine(PostToBackend(json));
    }

    IEnumerator PostToBackend(string json)
    {
        using var req = new UnityWebRequest(apiEndpoint, "POST");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
            Debug.Log($"[SceneSync] Sync succeeded | {DateTime.Now:HH:mm:ss}");
        else
            Debug.LogWarning($"[SceneSync] Sync failed: {req.error}\nURL={apiEndpoint}");
    }

    Transform GetBestPivot(Vector3 targetPos, GameObject[] pivots)
    {
        Transform best    = pivots[0].transform;
        float     minDist = Vector3.Distance(targetPos, best.position);

        foreach (var p in pivots)
        {
            float d = Vector3.Distance(targetPos, p.transform.position);
            if (d < minDist) { minDist = d; best = p.transform; }
        }
        return best;
    }

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

    string CaptureFromPivot(Transform pivot, Bounds bounds)
    {
        snapshotCam.transform.position = pivot.position;
        snapshotCam.transform.LookAt(bounds.center);

        snapshotCam.orthographic     = true;
        snapshotCam.orthographicSize =
            Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * 0.6f;
        snapshotCam.cullingMask = furnitureLayer;

        RenderTexture rt = new RenderTexture(resolution, resolution, 24);
        snapshotCam.targetTexture = rt;
        snapshotCam.Render();

        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
        tex.Apply();

        byte[] bytes = tex.EncodeToJPG(85);

        snapshotCam.targetTexture = null;
        RenderTexture.active      = null;
        DestroyImmediate(tex);
        DestroyImmediate(rt);

        return Convert.ToBase64String(bytes);
    }
}