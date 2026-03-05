using System;
using UnityEngine;

/// <summary>
/// 統一資料結構定義檔（最終版）
/// VirtualCameraBrain 和 NetworkClient 共用同一份定義
/// 不要在其他 .cs 裡重複定義這些 class
/// </summary>

// ─────────────────────────────────────────────
// Unity → Python 傳送 Payload
// 對齊 Python PerceptionEngine.analyze_action_burst(payload)
// ─────────────────────────────────────────────
[Serializable]
public class MultiImagePayload
{
    // ── VLM 核心（必填）──
    public string[] image_list;      // Base64 JPEG 字串陣列
    public int      image_count;     // 本次傳出幾張

    // ── 相機 metadata（必填）──
    public string[] source_nodes;    // 各影像對應的 CameraNode.nodeName
    public float[]  node_scores;     // 各節點評分，供 Python 端加權或 log

    // ── 用戶資訊（必填）──
    public string       userID;
    public string       activity;    // 觸發拍攝的行為，對應 Python 的 activity_hint
    public Vector3_Data user_pos;    // UserEntity 世界座標 → Python bind_and_update 的 est_pos
    public string       timestamp;

    // ── 機器人欄位（選填，定點相機模式填預設值）──
    // 角色未定前保留，確定不需要再刪除
    public Vector3_Data robot_pos;          // 定點模式：不填（null）
    public float        robot_rotation_y;   // 定點模式：0
    public float        camera_fov;         // 定點模式：0
}

// ─────────────────────────────────────────────
// Python → Unity 回應
// ─────────────────────────────────────────────
[Serializable]
public class PredictResponse
{
    public string action;
}

// ─────────────────────────────────────────────
// Vector3 序列化輔助（JsonUtility 不支援原生 Vector3）
// ─────────────────────────────────────────────
[Serializable]
public class Vector3_Data
{
    public float x, y, z;
    public Vector3_Data() { }
    public Vector3_Data(Vector3 v) { x = v.x; y = v.y; z = v.z; }
    public Vector3 ToVector3() => new Vector3(x, y, z);
}