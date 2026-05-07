using UnityEngine;

public class FixedPosAnimationTester : MonoBehaviour
{
    [Header("目標角色")]
    public Animator characterAnim;

    [Header("測試固定位置設定")]
    public Vector3 testPosition = new Vector3(3.4f, 0f, 3.4f);
    public Vector3 testRotation = new Vector3(0f, 0f, 0f); // 歐拉角 (例如 0, 180, 0)

    [System.Serializable]
    public class ItemPair
    {
        public string actionName;
        public GameObject heldItem;   // 手上拿的
        public GameObject sceneItem;  // 桌上放的
    }

    [Header("物品切換配置 (與 Animator State 同名)")]
    public ItemPair[] items;

    void Update()
    {
        if (characterAnim == null) return;

        // --- 鍵盤 1~0 核心動作 ---
        if (Input.GetKeyDown(KeyCode.Alpha1)) Execute("Standing");
        if (Input.GetKeyDown(KeyCode.Alpha2)) Execute("Sitting");
        if (Input.GetKeyDown(KeyCode.Alpha3)) Execute("Walking");
        if (Input.GetKeyDown(KeyCode.Alpha4)) Execute("Drinking");
        if (Input.GetKeyDown(KeyCode.Alpha5)) Execute("Laying");
        if (Input.GetKeyDown(KeyCode.Alpha6)) Execute("Reading");
        if (Input.GetKeyDown(KeyCode.Alpha7)) Execute("Typing");
        if (Input.GetKeyDown(KeyCode.Alpha8)) Execute("Watching");
        if (Input.GetKeyDown(KeyCode.Alpha9)) Execute("PhoneUse");
        if (Input.GetKeyDown(KeyCode.Alpha0)) Execute("Cleaning");

        // --- 字母鍵 進階互動 ---
        if (Input.GetKeyDown(KeyCode.E)) Execute("Eating");
        if (Input.GetKeyDown(KeyCode.O)) Execute("Opening");
        if (Input.GetKeyDown(KeyCode.P)) Execute("PickingUp");
        if (Input.GetKeyDown(KeyCode.U)) Execute("PuttingDown"); // 倒播測試
    }

    void Execute(string stateName)
    {
        // 1. 強制瞬移到 (3.4, 0, 3.4)
        characterAnim.transform.position = testPosition;
        characterAnim.transform.rotation = Quaternion.Euler(testRotation);

        // 2. 處理物品切換
        ToggleItems(stateName);

        // 3. 播放動畫 (Animator 裡的 Speed -1 會自動生效)
        characterAnim.Play(stateName, 0, 0f);
        
        Debug.Log($"[Fixed Test] 位置: {testPosition} | 播放狀態: {stateName}");
    }

    void ToggleItems(string stateName)
    {
        // 先重置：手上關閉，場景開啟
        foreach (var pair in items)
        {
            if (pair.heldItem != null) pair.heldItem.SetActive(false);
            if (pair.sceneItem != null) pair.sceneItem.SetActive(true);
        }

        // 啟動目前動作的物品
        foreach (var pair in items)
        {
            if (pair.actionName == stateName)
            {
                if (pair.heldItem != null) pair.heldItem.SetActive(true);
                if (pair.sceneItem != null) pair.sceneItem.SetActive(false);
            }
        }
    }
}