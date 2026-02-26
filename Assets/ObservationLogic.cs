using UnityEngine;
using System.Collections;

public class ObservationLogic : MonoBehaviour
{
    [Header("Detection Settings")]
    public float moveThreshold = 1.0f;    // 1 meter
    public float angleThreshold = 45.0f;  // 45 degrees

    private Vector3 lastPosition;
    private float lastRotationY;
    private bool isProcessing = false;

    void Start()
    {
        // Record initial position and rotation
        lastPosition = transform.position;
        lastRotationY = transform.eulerAngles.y;
    }

    void Update()
    {
        // If currently processing the previous VLM request, skip detection
        if (isProcessing) return;

        float distanceMoved = Vector3.Distance(transform.position, lastPosition);
        float angleMoved = Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, lastRotationY));

        if (distanceMoved >= moveThreshold || angleMoved >= angleThreshold)
        {
            StartCoroutine(TriggerObservation());

            // Update reference values
            lastPosition = transform.position;
            lastRotationY = transform.eulerAngles.y;
        }
    }

    private IEnumerator TriggerObservation()
    {
        isProcessing = true;
        Debug.Log("<color=lime>[Robot Perception]</color> Movement detected, starting recognition...");

        // 1. Capture image from camera manager
        string base64Image = RobotCameraManager.Instance.TakeSnapshot();

        // 2. Package data (assuming userID handled server-side)
        ObservationPayload payload = new ObservationPayload
        {
            image_data = base64Image,
            source = "Robot_FPV",
            robot_pos = transform.position,
            timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        // 3. Send to Flask server and wait for response (via callback)
        yield return StartCoroutine(
            NetworkClient.Instance.PostToPredict(payload, (result) =>
            {
                Debug.Log($"<color=cyan>[Robot Brain]</color> VLM Analysis Result: {result}");
                isProcessing = false; // Allow next detection
            })
        );
    }
}