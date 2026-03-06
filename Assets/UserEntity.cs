using UnityEngine;
using System.Collections.Generic;

public class UserEntity : MonoBehaviour
{
    public string userID = "User_Mom";
    public string currentActivity = "Idle";

    private List<GameObject> behaviorModels = new List<GameObject>();
    
    // ✅ 記錄每個子模型的初始世界座標
    private Dictionary<GameObject, Vector3> modelInitialPositions = new Dictionary<GameObject, Vector3>();
    private Dictionary<GameObject, Quaternion> modelInitialRotations = new Dictionary<GameObject, Quaternion>();

    void Awake()
    {
        foreach (Transform child in transform)
        {
            behaviorModels.Add(child.gameObject);
            
            // ✅ 記錄初始世界座標（在 SetActive 之前）
            modelInitialPositions[child.gameObject] = child.position;
            modelInitialRotations[child.gameObject] = child.rotation;
        }

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
        }

        HideAllModels();
    }

    public void SwitchActivity(string activityName)
    {
        currentActivity = activityName;
        bool found = false;

        foreach (GameObject model in behaviorModels)
        {
            if (model.name.ToLower().Contains(activityName.ToLower()))
            {
                // ✅ 用記錄的初始世界座標移動父物件
                if (modelInitialPositions.TryGetValue(model, out Vector3 targetPos))
                {
                    transform.position = targetPos;
                    transform.rotation = modelInitialRotations[model];
                }

                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.SetActive(true);

                Collider col = model.GetComponent<Collider>();
                if (col != null) col.enabled = true;
                else Debug.LogWarning($"[{userID}] {model.name} 沒有 Collider");

                Debug.Log($"<color=lime>[{userID}]</color> 啟動：{model.name}");
                found = true;
            }
            else
            {
                model.SetActive(false);
            }
        }

        if (!found)
            Debug.LogWarning($"[{userID}] 找不到對應子模型：{activityName}");
    }

    public void HideAllModels()
    {
        foreach (GameObject model in behaviorModels)
            if (model != null) model.SetActive(false);
        currentActivity = "Idle";
    }

    public Vector3 GetAimPosition()
    {
        foreach (GameObject model in behaviorModels)
        {
            if (!model.activeInHierarchy) continue;
            Collider col = model.GetComponent<Collider>();
            if (col != null) return col.bounds.center;
            Renderer rend = model.GetComponentInChildren<Renderer>();
            if (rend != null) return rend.bounds.center;
            return model.transform.position + Vector3.up * 1.0f;
        }
        return transform.position + Vector3.up * 1.0f;
    }

    public bool HasActiveModel() => currentActivity != "Idle";
}