using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 掛在 VirtualCameraBrain 同一個空物件上
/// 每次拍照後自動儲存最新影像，可在 Game 視窗即時預覽
/// </summary>
public class CameraDebugViewer : MonoBehaviour
{
    public static CameraDebugViewer Instance;

    [Header("預覽設定")]
    public bool enablePreview = true;

    [Tooltip("預覽視窗在 Game 視窗的位置")]
    public Vector2 windowPosition = new Vector2(10, 10);

    [Tooltip("每張預覽圖的大小（像素）")]
    public int previewSize = 200;

    // 儲存最近拍攝的影像（最多 3 張）
    private Texture2D[] _previews = new Texture2D[3];
    private string[]    _labels   = new string[3];
    private int         _count    = 0;
    private string      _lastUser     = "";
    private string      _lastActivity = "";
    private string      _lastTime     = "";

    void Awake() { Instance = this; }

    // ─────────────────────────────────────────────
    // 由 VirtualCameraBrain 呼叫，儲存預覽圖
    // ─────────────────────────────────────────────
    public void RegisterCapture(Texture2D tex, string nodeName, string userID, string activity)
    {
        if (!enablePreview) return;
        if (_count >= 3) _count = 0; // 超過 3 張從頭覆蓋

        // 複製一份（原本的 tex 會被 Destroy）
        Texture2D copy = new Texture2D(tex.width, tex.height, tex.format, false);
        Graphics.CopyTexture(tex, copy);

        if (_previews[_count] != null) Destroy(_previews[_count]);
        _previews[_count] = copy;
        _labels[_count]   = nodeName;
        _count++;

        _lastUser     = userID;
        _lastActivity = activity;
        _lastTime     = System.DateTime.Now.ToString("HH:mm:ss");

        Debug.Log($"<color=cyan>[Preview]</color> 已更新預覽：{nodeName} | {userID} | {activity}");
    }

    // ─────────────────────────────────────────────
    // 在 Game 視窗右下角顯示預覽
    // ─────────────────────────────────────────────
    void OnGUI()
    {
        if (!enablePreview) return;

        int filled = 0;
        for (int i = 0; i < 3; i++) if (_previews[i] != null) filled++;
        if (filled == 0) return;

        float padding = 8f;
        float totalW  = filled * (previewSize + padding) + padding;
        float totalH  = previewSize + 60f;

        // 背景框
        GUI.color = new Color(0, 0, 0, 0.75f);
        GUI.DrawTexture(new Rect(windowPosition.x, windowPosition.y, totalW, totalH), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // 標題列
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 11,
            fontStyle = FontStyle.Bold,
            normal    = { textColor = Color.cyan }
        };
        GUI.Label(
            new Rect(windowPosition.x + padding, windowPosition.y + 4, totalW, 18),
            $"📷 {_lastUser} · {_lastActivity} · {_lastTime}",
            titleStyle
        );

        // 影像預覽
        GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };

        int col = 0;
        for (int i = 0; i < 3; i++)
        {
            if (_previews[i] == null) continue;
            float x = windowPosition.x + padding + col * (previewSize + padding);
            float y = windowPosition.y + 24;

            GUI.DrawTexture(new Rect(x, y, previewSize, previewSize), _previews[i], ScaleMode.ScaleToFit);
            GUI.Label(new Rect(x, y + previewSize + 2, previewSize, 16), _labels[i], labelStyle);
            col++;
        }
    }

    void OnDestroy()
    {
        foreach (var t in _previews)
            if (t != null) Destroy(t);
    }
}