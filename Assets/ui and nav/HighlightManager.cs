using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// HighlightManager — polls /highlight from Flask.
/// Adds an emission glow to the matching object.
/// Attach to any persistent GameObject (e.g. _System).
/// </summary>
public class HighlightManager : MonoBehaviour
{
    [Header("Backend")]
    public string backendUrl   = "http://localhost:5000";
    public float  pollInterval = 1.0f;

    [Header("Highlight Color")]
    public Color highlightColor = new Color(1f, 0.8f, 0f, 1f);

    [Header("Objects that can be highlighted")]
    [Tooltip("All scene objects that might be recommended by AI")]
    public List<GameObject> highlightableObjects = new List<GameObject>();

    string _currentLabel = "";
    readonly Dictionary<GameObject, Material[]> _originalMats = new();

    void Start()
    {
        StartCoroutine(PollLoop());
    }

    IEnumerator PollLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(pollInterval);
            yield return StartCoroutine(FetchHighlight());
        }
    }

    IEnumerator FetchHighlight()
    {
        using var req = UnityWebRequest.Get($"{backendUrl}/highlight");
        req.timeout = 3;
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            yield break;

        var data = JsonUtility.FromJson<HighlightResponse>(req.downloadHandler.text);
        string label = data?.label ?? "";

        if (label == _currentLabel)
            yield break;

        ClearHighlight();
        _currentLabel = label;

        if (!string.IsNullOrEmpty(label))
            ApplyHighlight(label);
    }

    void ApplyHighlight(string label)
    {
        foreach (var obj in highlightableObjects)
        {
            if (obj == null) continue;
            if (!obj.name.ToLower().Contains(label.ToLower())) continue;

            var renderers = obj.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                if (!_originalMats.ContainsKey(r.gameObject))
                    _originalMats[r.gameObject] = r.materials;

                var mats = r.materials;
                foreach (var m in mats)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", highlightColor * 2f);
                }
                r.materials = mats;
            }

            Debug.Log($"[Highlight] {obj.name} highlighted");
        }
    }

    void ClearHighlight()
    {
        foreach (var kvp in _originalMats)
        {
            if (kvp.Key == null) continue;
            var r = kvp.Key.GetComponent<Renderer>();
            if (r != null)
            {
                r.materials = kvp.Value;
                foreach (var m in kvp.Value)
                    m.DisableKeyword("_EMISSION");
            }
        }
        _originalMats.Clear();
    }

    [System.Serializable]
    class HighlightResponse { public string label; }
}