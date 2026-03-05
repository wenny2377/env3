using UnityEngine;
using System.Collections.Generic;

public class UserEntity : MonoBehaviour
{
    public string userID = "User_Mom";
    public string currentActivity = "Idle";

    private List<GameObject> behaviorModels = new List<GameObject>();

    void Awake()
    {
        // 1. 初始化模型清單
        foreach (Transform child in transform)
            behaviorModels.Add(child.gameObject);

        // 2. 確保父物件有 Rigidbody (觸發 RoomArea 必備)
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; // 設為 Kinematic 避免受重力掉落
        }

        HideAllModels();
    }

    void Update()
    {
        // 核心修正：每一幀都讓父物件座標追隨當前顯示的子模型
        SyncPosition();
    }

    // ─────────────────────────────────────────────
    // 座標同步邏輯：解決父物件留在 (0,0,0) 的問題
    // ─────────────────────────────────────────────
    private void SyncPosition()
    {
        foreach (GameObject model in behaviorModels)
        {
            if (model.activeInHierarchy)
            {
                // 如果子模型跟父物件位置不一致
                if (Vector3.Distance(transform.position, model.transform.position) > 0.05f)
                {
                    // 記錄目前子模型在世界空間中的位置
                    Vector3 worldPos = model.transform.position;
                    
                    // 將父物件移動到該位置
                    transform.position = worldPos;

                    // 同步後，將子模型的局部座標歸零，防止位置雙倍偏移
                    model.transform.localPosition = Vector3.zero;
                }
                break;
            }
        }
    }

    // ─────────────────────────────────────────────
    // 切換行為
    // ─────────────────────────────────────────────
    public void SwitchActivity(string activityName)
    {
        currentActivity = activityName;
        bool found = false;

        foreach (GameObject model in behaviorModels)
        {
            if (model.name.ToLower().Contains(activityName.ToLower()))
            {
                model.SetActive(true);

                // 立即進行一次位置同步，確保觸發器反應即時
                transform.position = model.transform.position;
                model.transform.localPosition = Vector3.zero;

                // 啟用子物件上的 Collider，讓 Raycast 射得中
                Collider col = model.GetComponent<Collider>();
                if (col != null) col.enabled = true;
                else Debug.LogWarning($"[{userID}] {model.name} 沒有 Collider，請加上 CapsuleCollider");

                Debug.Log($"<color=lime>[{userID}]</color> 啟動動作：{model.name} 並同步父物件位置");
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

    // ─────────────────────────────────────────────
    // 供 StaticCameraManager 使用
    // ─────────────────────────────────────────────
    public Vector3 GetAimPosition()
    {
        foreach (GameObject model in behaviorModels)
        {
            if (!model.activeInHierarchy) continue;

            // 1. 優先用 Collider 中心
            Collider col = model.GetComponent<Collider>();
            if (col != null) return col.bounds.center;

            // 2. 次選 Renderer 中心
            Renderer rend = model.GetComponentInChildren<Renderer>();
            if (rend != null) return rend.bounds.center;

            // 3. Fallback
            return model.transform.position + Vector3.up * 1.0f;
        }
        return transform.position + Vector3.up * 1.0f;
    }

    public bool HasActiveModel() => currentActivity != "Idle";
}