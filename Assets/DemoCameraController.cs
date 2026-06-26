using System.Collections;
using UnityEngine;

public class DemoCameraController : MonoBehaviour
{
    public static DemoCameraController Instance { get; private set; }

    [Header("Scene References")]
    public UserEntity userMom;
    public UserEntity userDad;
    public Transform  miniPC;
    public Transform  tv;

    [Header("Demo Camera")]
    public Camera demoCamera;

    [Header("Camera Move Speed")]
    public float moveDuration = 0.8f;

    [Header("Follow Smoothness")]
    public float followSpeed = 4f;

    const float CAM_HEIGHT  = 2.5f;
    const float CAM_SOUTH_Z = 7.8f;

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
        if (demoCamera == null) { Debug.LogError("[DemoCam] demoCamera not set"); return; }
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

        Vector3 pos; Quaternion rot;

        switch (cameraName)
        {
            case "Cam_Overview":  (pos, rot) = CalcOverview();              break;
            case "Cam_MamiCall":  (pos, rot) = CalcMamiCall();              break;
            case "Cam_Dialogue":  (pos, rot) = CalcDialogue(_currentActor); break;
            case "Cam_TV":        (pos, rot) = CalcTV();                    break;
            default: Debug.LogWarning($"[DemoCam] Unknown: {cameraName}"); return;
        }

        if (_moveCoroutine != null) StopCoroutine(_moveCoroutine);
        _moveCoroutine = StartCoroutine(MoveCamera(pos, rot));
        Debug.Log($"[DemoCam] -> {cameraName}");
    }

    (Vector3, Quaternion) CalcOverview()
    {
        UserEntity anchor = _currentActor ?? userMom;
        Vector3    aPos   = anchor != null ? anchor.transform.position : new Vector3(4.0f, 0f, 3.3f);
        Vector3    pos; Vector3 lookAt = new Vector3(aPos.x, aPos.y + 0.9f, aPos.z);

        if (aPos.x < 1.0f && aPos.z < 2.5f)
        {
            pos    = new Vector3(-0.5f, 1.6f, 1.92f);
            lookAt = new Vector3(aPos.x, aPos.y + 0.9f, aPos.z);
        }
        else if (aPos.z > 5.0f)
        {
            pos = new Vector3(aPos.x, CAM_HEIGHT, aPos.z - 3.0f);
        }
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
        Vector3 pos  = new Vector3(mPos.x - 0.3f, mPos.y + 0.3f, 1.3f);
        return (pos, Quaternion.LookRotation(mPos - pos));
    }

    (Vector3, Quaternion) CalcDialogue(UserEntity actor)
    {
        Vector3 aPos = actor != null ? actor.transform.position : new Vector3(4.0f, 0f, 3.0f);
        Vector3 mPos = miniPC != null ? miniPC.position : new Vector3(6.254f, 0.41f, 0.51f);
        Vector3 pos;

        if (aPos.x < 1.0f)
            pos = new Vector3(-0.5f, 1.4f, 1.92f);
        else
        {
            float camZ = Mathf.Min(aPos.z + 1.2f, CAM_SOUTH_Z);
            pos = new Vector3(aPos.x - 0.4f, aPos.y + 1.3f, camZ);
        }

        return (pos, Quaternion.LookRotation(mPos - pos));
    }

    (Vector3, Quaternion) CalcTV()
    {
        Vector3 tvPos = tv != null ? tv.position : new Vector3(5.447f, 1.08f, 2.228f);
        UserEntity anchor = _currentActor ?? userMom;
        Vector3 aPos = anchor != null ? anchor.transform.position : new Vector3(4.0f, 0f, 3.3f);

        Vector3 pos = new Vector3(aPos.x - 0.5f, aPos.y + 1.2f, aPos.z + 1.5f);
        Vector3 lookAt = new Vector3(tvPos.x, tvPos.y, tvPos.z);
        return (pos, Quaternion.LookRotation(lookAt - pos));
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