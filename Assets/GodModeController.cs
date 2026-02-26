using UnityEngine;

public class GodModeController : MonoBehaviour
{
    [Header("User Entity References (User Proxies)")]
    public UserEntity momEntity; // Drag in the object with the UserEntity (ID: User_Mom)
    public UserEntity dadEntity; // Drag in the object with the UserEntity (ID: User_Dad)

    [Header("Robot Control (NavMesh)")]
    public RobotPatrol robotControl; // Drag in the robot object with the RobotPatrol script

    void Update()
    {
        // --- Mom's Behavior Control (Keyboard number keys 1, 2, 3, 4) ---
        // Switch models by matching child object names via strings
        if (Input.GetKeyDown(KeyCode.Alpha1)) momEntity.SwitchActivity("sleeping");
        if (Input.GetKeyDown(KeyCode.Alpha2)) momEntity.SwitchActivity("typing");
        if (Input.GetKeyDown(KeyCode.Alpha3)) momEntity.SwitchActivity("drinking");
        if (Input.GetKeyDown(KeyCode.Alpha4)) momEntity.SwitchActivity("sitting");

        // --- Dad's Behavior Control (Keyboard number keys 7, 8, 9, 0) ---
        if (Input.GetKeyDown(KeyCode.Alpha7)) dadEntity.SwitchActivity("sleeping");
        if (Input.GetKeyDown(KeyCode.Alpha8)) dadEntity.SwitchActivity("typing");
        if (Input.GetKeyDown(KeyCode.Alpha9)) dadEntity.SwitchActivity("drinking");
        if (Input.GetKeyDown(KeyCode.Alpha0)) dadEntity.SwitchActivity("swinging");

        // --- Robot Patrol Toggle (Keyboard P key) ---
        // Trigger the robot's NavMesh patrol path
        if (Input.GetKeyDown(KeyCode.P))
        {
            if (robotControl != null)
            {
                robotControl.TogglePatrol();
            }
            else
            {
                Debug.LogError("GodModeController: RobotControl reference is not assigned!");
            }
        }

        // --- Clear All and Stop (Spacebar) ---
        if (Input.GetKeyDown(KeyCode.Space))
        {
            // Hide all model labels
            if (momEntity != null) momEntity.HideAllModels();
            if (dadEntity != null) dadEntity.HideAllModels();

            // Stop robot movement and reset path
            if (robotControl != null)
            {
                // If you've added a StopPatrol method in RobotPatrol, it's better to use it
                robotControl.TogglePatrol();
            }

            Debug.Log("<color=white>[God Mode]</color> All activities cleared and robot state reset attempt.");
        }
    }
}