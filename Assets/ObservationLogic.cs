using UnityEngine;
using System.Collections;

public class ObservationLogic : MonoBehaviour
{
    [Header("閾值設定")]
    public float moveThreshold = 1.0f;    // 1 公尺
    public float angleThreshold = 45.0f; // 45 度

    private Vector3 lastPosition;
    private float lastRotationY;
    private bool isProcessing = false;

    void Start()
    {
        // 紀錄初始位置與角度
        lastPosition = transform.position;
        lastRotationY = transform.eulerAngles.y;
    }

    void Update()
    {
        // 如果正在處理上一次的 VLM 請求，則跳過判斷
        if (isProcessing) return;

        float distanceMoved = Vector3.Distance(transform.position, lastPosition);
        float angleMoved = Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, lastRotationY));

        if (distanceMoved >= moveThreshold || angleMoved >= angleThreshold)
        {
            StartCoroutine(TriggerObservation());

            // 更新基準點
            lastPosition = transform.position;
            lastRotationY = transform.eulerAngles.y;
        }
    }

    private IEnumerator TriggerObservation()
    {
        isProcessing = true;
        Debug.Log("<color=lime>[Robot Perception]</color> 觸發位移觀測...");

        // 1. 從相機管理器獲取圖片
        string base64Image = RobotCameraManager.Instance.TakeSnapshot();

        // 2. 封裝數據 (不指定 userID，讓後端 VLM 辨識)
        ObservationPayload payload = new ObservationPayload
        {
            image = base64Image,
            source = "Robot_FPV",
            robot_pos = transform.position,
            timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        // 3. 發送至 Flask 並等待回傳 (Callback 模式)
        yield return StartCoroutine(NetworkClient.Instance.PostToPredict(payload, (result) => {
            Debug.Log($"<color=cyan>[Robot Brain]</color> VLM 辨識結果: {result}");
            isProcessing = false; // 解鎖，允許下一次觀測
        }));
    }
}