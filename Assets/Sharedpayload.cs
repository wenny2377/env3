using System;
using UnityEngine;

[Serializable]
public class MultiImagePayload
{
    public string[] image_list;
    public int      image_count;

    public string[] source_nodes;
    public float[]  node_scores;

    public string       userID;
    public string       activity;
    public string       room_name;
    public Vector3_Data user_pos;
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
