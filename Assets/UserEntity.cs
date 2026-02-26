using UnityEngine;
using System.Collections.Generic;

public class UserEntity : MonoBehaviour
{
    public string userID = "User_Mom";
    public string currentActivity = "Idle";

    // Automatically manage all animation behavior child objects
    private List<GameObject> behaviorModels = new List<GameObject>();

    void Awake()
    {
        // 1. Automatically fetch all child objects (mom_typing, mom_drinking, etc.)
        foreach (Transform child in transform)
        {
            behaviorModels.Add(child.gameObject);
        }
        HideAllModels();
    }

    /// <summary>
    /// Switch activity: Show the corresponding model and hide others
    /// </summary>
    public void SwitchActivity(string activityName)
    {
        currentActivity = activityName;
        bool found = false;

        foreach (GameObject model in behaviorModels)
        {
            // Check if the object name contains the activity keyword (e.g., "drinking")
            if (model.name.ToLower().Contains(activityName.ToLower()))
            {
                model.SetActive(true);

                // --- Synchronize with the physics system ---
                // Ensure the Capsule Collider on the child object is enabled so that StaticCameraManager can detect it
                Collider col = model.GetComponent<Collider>();
                if (col != null) col.enabled = true;

                Debug.Log($"<color=lime>[{userID}]</color> Activating activity: {model.name}");
                found = true;
            }
            else
            {
                model.SetActive(false);
            }
        }

        if (!found) Debug.LogWarning($"[{userID}] Could not find corresponding activity model: {activityName}");
    }

    public void HideAllModels()
    {
        foreach (GameObject model in behaviorModels)
        {
            if (model != null) model.SetActive(false);
        }
        currentActivity = "Idle";
    }
}