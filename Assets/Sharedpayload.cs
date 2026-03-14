using System;
using UnityEngine;

[Serializable]
public class MultiImagePayload
{

    public string[] image_list;      // Base64 JPEG 字串陣列
    public int      image_count;     // 本次傳出幾張

    // ── 相機 metadata（必填）──
    public string[] source_nodes;    // 各影像對應的 CameraNode.nodeName
    public float[]  node_scores;     // 各節點評分，供 Python 端加權或 log

    // ── 用戶資訊（必填）──
    public string       userID;
    public string       activity;    // 觸發拍攝的行為（ground truth，不送給 VLM）
    public string       room_name;   // ← 新增：相機所在房間名稱，對齊 Python room_name
    public Vector3_Data user_pos;    // UserEntity 世界座標 → Python bind_and_update 的 est_pos
    public string       timestamp;

}

[Serializable]
public class PredictResponse
{
    public string action;
}


[Serializable]
public class Vector3_Data
{
    public float x, y, z;
    public Vector3_Data() { }
    public Vector3_Data(Vector3 v) { x = v.x; y = v.y; z = v.z; }
    public Vector3 ToVector3() => new Vector3(x, y, z);
}