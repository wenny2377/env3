using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System;

/// <summary>
/// 網路傳送模組（最終版）
/// 統一使用 MultiImagePayload（定義在 SharedPayload.cs）
/// </summary>
public class NetworkClient : MonoBehaviour
{
    public static NetworkClient Instance;

    [Header("Server Settings")]
    public string flaskUrl = "http://127.0.0.1:5000/predict";

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    public IEnumerator PostToPredict(MultiImagePayload payload, Action<string> onResultReceived)
    {
        string jsonData = JsonUtility.ToJson(payload);
        Debug.Log($"<color=cyan>[NetworkClient]</color> POST → {flaskUrl} | {payload.image_count} 張 | {jsonData.Length} chars");

        using (UnityWebRequest request = new UnityWebRequest(flaskUrl, "POST"))
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler   = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 20;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                PredictResponse res = JsonUtility.FromJson<PredictResponse>(request.downloadHandler.text);
                Debug.Log($"<color=lime>[NetworkClient]</color> 回應：{res.action}");
                onResultReceived?.Invoke(res.action);
            }
            else
            {
                Debug.LogError($"[NetworkClient] 失敗：{request.error}");
                onResultReceived?.Invoke("Error");
            }
        }
    }
}