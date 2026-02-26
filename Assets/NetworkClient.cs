using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System;

public class NetworkClient : MonoBehaviour
{
    public static NetworkClient Instance;

    [Header("Server Settings")]
    [Tooltip("Enter the Flask address of the Mac Mini, e.g., http://127.0.0.1:5000/predict")]
    public string flaskUrl = "http://127.0.0.1:5000/predict";

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    /// <summary>
    /// Send observation data to Flask and wait for VLM to return the result
    /// </summary>
    public IEnumerator PostToPredict(ObservationPayload payload, Action<string> onResultReceived)
    {
        string jsonData = JsonUtility.ToJson(payload);

        using (UnityWebRequest request = new UnityWebRequest(flaskUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                // Parse the returned JSON (assumed response format: {"action": "..."})
                string responseText = request.downloadHandler.text;
                PredictResponse response = JsonUtility.FromJson<PredictResponse>(responseText);

                onResultReceived?.Invoke(response.action);
            }
            else
            {
                Debug.LogError($"[Network] Sending failed: {request.error}");
                onResultReceived?.Invoke("Error");
            }
        }
    }
}

// --- Data Structure Definitions ---

[Serializable]
public class ObservationPayload
{
    public string image;       // Base64 JPG
    public string source;      // e.g., "Robot_FPV"
    public string userID;      // Optional, for stationary cameras
    public Vector3 robot_pos;  // Robot's current position
    public string timestamp;
}

[Serializable]
public class PredictResponse
{
    public string status;
    public string action; // Action label inferred by VLM
}