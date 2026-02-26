using System.Collections;
using UnityEngine;

public class RobotCameraManager : MonoBehaviour
{
    public static RobotCameraManager Instance;
    public Camera robotFPVCamera;
    public int resolution = 512;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    // --- Key Fix 2: Ensure method name matches the one called by StaticCameraManager ---
    public void RequestRobotSnapshot(string userID, string activity)
    {
        StartCoroutine(CaptureAndUploadRoutine(userID, activity));
    }

    private IEnumerator CaptureAndUploadRoutine(string userID, string activity)
    {
        yield return new WaitForEndOfFrame();

        string base64Img = TakeSnapshot();
        if (string.IsNullOrEmpty(base64Img)) yield break;

        ObservationPayload payload = new ObservationPayload
        {
            image = base64Img,
            source = "Robot_FPV_Verification",
            userID = userID,
            timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        Debug.Log($"<color=cyan>[Robot Camera]</color> Capturing snapshot and sending to backend...");

        // Note: NetworkClient script must be available here
        yield return StartCoroutine(NetworkClient.Instance.PostToPredict(payload, (result) => {
            Debug.Log($"<color=green>[VLM Result]</color> Backend confirmation: {result}");
        }));
    }

    public string TakeSnapshot()
    {
        if (robotFPVCamera == null) return "";
        RenderTexture rt = new RenderTexture(resolution, resolution, 24);
        robotFPVCamera.targetTexture = rt;
        robotFPVCamera.Render();
        RenderTexture.active = rt;
        Texture2D ss = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
        ss.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
        ss.Apply();
        robotFPVCamera.targetTexture = null;
        RenderTexture.active = null;
        byte[] bytes = ss.EncodeToJPG(80);
        Destroy(rt); Destroy(ss);
        return System.Convert.ToBase64String(bytes);
    }
}