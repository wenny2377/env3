using System.Collections;
using UnityEngine;

public class DemoCameraController : MonoBehaviour
{
    public static DemoCameraController Instance { get; private set; }

    [Header("Scene References")]
    public UserEntity userMom;
    public UserEntity userDad;
    public Transform  miniPC;

    [Header("Demo Camera (拖入 DemoCamera 物件)")]
    public Camera demoCamera;

    [Header("Camera Move Speed")]
    public float moveDuration = 0.8f;

    [Header("Follow Smoothness")]
    public float followSpeed = 4f;

    // 房間固定參數（從座標分析得出）
    // 客廳中心約 X=4, Z=3.5
    // 南牆約 Z=0，北牆約 Z=8，東牆約 X=8，西牆約 X=0
    const float ROOM_CENTER_X  = 4.0f;
    const float ROOM_CENTER_Z  = 3.5f;
    const float CAM_SOUTH_Z    = 7.8f;  // 相機固定在南方（+Z 方向），往北看
    const float CAM_HEIGHT     = 2.5f;
    const float MAMI_CALL_Z    = 1.3f;  // MiniPC 前方

    Coroutine  _moveCoroutine;
    UserEntity _currentActor;
    string     _activeCameraName = "";
    bool       _transitioning    = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        if (demoCamera == null)
        {
            Debug.LogError("[DemoCam] demoCamera 未設定");
            return;
        }
        var (pos, rot) = CalcOverview();
        demoCamera.transform.SetPositionAndRotation(pos, rot);
        Debug.Log("[DemoCam] Ready");
    }

    void Update()
    {
        if (demoCamera == null || _transitioning) return;

        if (_activeCameraName == "Cam_Overview")
        {
            var (pos, rot) = CalcOverview();
            demoCamera.transform.position = Vector3.Lerp(demoCamera.transform.position, pos, followSpeed * Time.deltaTime);
            demoCamera.transform.rotation = Quaternion.Slerp(demoCamera.transform.rotation, rot, followSpeed * Time.deltaTime);
        }
        else if (_activeCameraName == "Cam_Dialogue" && _currentActor != null)
        {
            var (pos, rot) = CalcDialogue(_currentActor);
            demoCamera.transform.position = Vector3.Lerp(demoCamera.transform.position, pos, followSpeed * Time.deltaTime);
            demoCamera.transform.rotation = Quaternion.Slerp(demoCamera.transform.rotation, rot, followSpeed * Time.deltaTime);
        }
    }

    public void SetActiveCamera(string cameraName, UserEntity actor = null)
    {
        if (demoCamera == null) return;
        if (actor != null) _currentActor = actor;
        _activeCameraName = cameraName;

        Vector3    pos;
        Quaternion rot;

        switch (cameraName)
        {
            case "Cam_Overview":  (pos, rot) = CalcOverview();              break;
            case "Cam_MamiCall":  (pos, rot) = CalcMamiCall();              break;
            case "Cam_Dialogue":  (pos, rot) = CalcDialogue(_currentActor); break;
            default:
                Debug.LogWarning($"[DemoCam] Unknown: {cameraName}");
                return;
        }

        if (_moveCoroutine != null) StopCoroutine(_moveCoroutine);
        _moveCoroutine = StartCoroutine(MoveCamera(pos, rot));
        Debug.Log($"[DemoCam] -> {cameraName}");
    }

    // 判斷角色在哪個房間，選擇正確的退鏡方向
    (Vector3, Quaternion) CalcOverview()
    {
        UserEntity anchor = _currentActor ?? userMom;
        Vector3    aPos   = anchor != null ? anchor.transform.position : new Vector3(4.0f, 0f, 3.3f);

        Vector3 pos;
        Vector3 lookAt = new Vector3(aPos.x, aPos.y + 0.9f, aPos.z);

        // Dad 房間（X < 1, Z < 2.5）→ 實測好角度
        if (aPos.x < 1.0f && aPos.z < 2.5f)
        {
            pos    = new Vector3(-1.16f, 1.84f, 1.92f);
            lookAt = new Vector3(aPos.x, aPos.y + 0.9f, aPos.z);
        }
        // 廚房區域（Z > 5）→ 從 -Z 方向往 +Z 看（從南往北）
        else if (aPos.z > 5.0f)
        {
            pos = new Vector3(aPos.x, CAM_HEIGHT, aPos.z - 3.0f);
        }
        // 客廳（預設）→ 從 +Z 方向往 -Z 看，但限制不超過南牆
        else
        {
            float camZ = Mathf.Min(aPos.z + 3.5f, CAM_SOUTH_Z);
            pos = new Vector3(aPos.x, CAM_HEIGHT, camZ);
        }

        return (pos, Quaternion.LookRotation(lookAt - pos));
    }

    (Vector3, Quaternion) CalcMamiCall()
    {
        Vector3 mPos = miniPC != null ? miniPC.position : new Vector3(6.254f, 0.41f, 0.51f);
        Vector3 pos  = new Vector3(mPos.x - 0.3f, mPos.y + 0.3f, MAMI_CALL_Z);
        return (pos, Quaternion.LookRotation(mPos - pos));
    }

    (Vector3, Quaternion) CalcDialogue(UserEntity actor)
    {
        Vector3 aPos = actor != null ? actor.transform.position : new Vector3(4.0f, 0f, 3.0f);
        Vector3 mPos = miniPC != null ? miniPC.position : new Vector3(6.254f, 0.41f, 0.51f);

        Vector3 pos;

        // Dad 房間（X < 1）→ 固定角度過肩
        if (aPos.x < 1.0f)
        {
            pos = new Vector3(-1.16f, 1.5f, 1.92f);
        }
        else
        {
            float camZ = Mathf.Min(aPos.z + 1.2f, CAM_SOUTH_Z);
            pos = new Vector3(aPos.x - 0.4f, aPos.y + 1.3f, camZ);
        }

        return (pos, Quaternion.LookRotation(mPos - pos));
    }

    IEnumerator MoveCamera(Vector3 targetPos, Quaternion targetRot)
    {
        if (demoCamera == null) yield break;
        _transitioning = true;

        Vector3    startPos = demoCamera.transform.position;
        Quaternion startRot = demoCamera.transform.rotation;
        float      elapsed  = 0f;

        while (elapsed < moveDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / moveDuration);
            demoCamera.transform.position = Vector3.Lerp(startPos, targetPos, t);
            demoCamera.transform.rotation = Quaternion.Slerp(startRot, targetRot, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        demoCamera.transform.SetPositionAndRotation(targetPos, targetRot);
        _transitioning = false;
    }
}