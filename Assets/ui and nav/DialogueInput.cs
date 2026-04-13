using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class DialogueInput : MonoBehaviour
{
    [Header("Backend")]
    public string backendUrl = "http://localhost:5000";
    public string userID     = "User_Mom";

    [Header("UI")]
    public TMP_InputField inputField;
    public Button         sendButton;
    public TMP_Text       statusText;

    [Header("References")]
    public UserBubble userBubble;

    bool _sending = false;

    void Start()
    {
        if (sendButton != null)
            sendButton.onClick.AddListener(OnSend);

        if (inputField != null)
            inputField.onSubmit.AddListener(_ => OnSend());

        if (statusText != null)
            statusText.text = "";
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return) && !_sending)
            OnSend();
    }

    void OnSend()
    {
        if (_sending) return;
        if (inputField == null) return;

        string query = inputField.text.Trim();
        if (string.IsNullOrEmpty(query)) return;

        if (userBubble != null)
            userBubble.ShowMessage(query);

        inputField.text = "";

        StartCoroutine(SendQuery(query));
    }

    IEnumerator SendQuery(string query)
    {
        _sending = true;

        if (statusText != null)
            statusText.text = "Thinking...";

        string json = $"{{\"query\":\"{EscapeJson(query)}\",\"userID\":\"{userID}\",\"room\":\"\"}}";
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);

        using var req = new UnityWebRequest($"{backendUrl}/interact/stream", "POST");
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 60;

        yield return req.SendWebRequest();

        if (statusText != null)
            statusText.text = "";

        _sending = false;

        if (inputField != null)
        {
            inputField.ActivateInputField();
            inputField.Select();
        }
    }

    string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
    }
}