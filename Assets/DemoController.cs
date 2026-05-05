using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class DemoController : MonoBehaviour
{
    [Header("Users")]
    public UserEntity userMom;
    public UserEntity userDad;

    [Header("Robot")]
    public Transform robotStartPosition;
    public NavBrain  navBrain;

    [Header("Bubbles")]
    public UserBubble  userBubble;
    public RobotBubble robotBubble;

    [Header("Timing")]
    public float typewriterSpeed      = 0.04f;
    public float afterUserBubble      = 1.5f;
    public float afterChatResponse    = 18f;
    public float afterArrival         = 2.5f;
    public float navPollInterval      = 0.5f;
    public float navTimeout           = 20f;
    public float observeWaitPerAction = 2.5f;
    public int   observeActionsCount  = 5;
    public float afterObserveWait     = 10f;
    public float betweenPhasePause    = 3f;

    [Header("Backend")]
    public string backendUrl = "http://localhost:5000";

    string _guiPhase  = "";
    string _guiUser   = "";
    string _guiStatus = "";
    bool   _showGui   = false;

    struct DemoLine
    {
        public string userID;
        public string query;
        public string phase;
    }

    static readonly DemoLine[] Script = new[]
    {
        new DemoLine { userID="User_Mom", phase="cold",
            query="I am thirsty" },
        new DemoLine { userID="User_Mom", phase="cold",
            query="Is there any beer?" },

        new DemoLine { userID="", phase="observe", query="" },

        new DemoLine { userID="User_Mom", phase="personalized",
            query="I am thirsty" },
        new DemoLine { userID="User_Mom", phase="personalized",
            query="I don't want juice today" },
        new DemoLine { userID="User_Mom", phase="personalized",
            query="Is there any cola?" },

        new DemoLine { userID="User_Dad", phase="personalized",
            query="I am thirsty" },
        new DemoLine { userID="User_Dad", phase="personalized",
            query="Is there any beer?" },
    };

    void Start()
    {
        ExperimentRunner runner = FindObjectOfType<ExperimentRunner>();
        if (runner == null || runner.mode != ExperimentRunner.RunMode.Demo)
            return;

        StartCoroutine(RunDemo());
    }

    IEnumerator RunDemo()
    {
        _showGui = true;

        if (robotStartPosition != null && navBrain != null)
        {
            navBrain.transform.position = robotStartPosition.position;
            navBrain.transform.rotation = robotStartPosition.rotation;
        }

        yield return new WaitForSeconds(0.5f);

        if (userDad != null) userDad.gameObject.SetActive(false);
        if (userMom != null)
        {
            userMom.gameObject.SetActive(true);
            if (!userMom.IsBusy)
                yield return StartCoroutine(userMom.SwitchActivity("Watching"));
        }

        yield return new WaitForSeconds(1.5f);

        string currentUserID = "User_Mom";
        string currentPhase  = "";

        foreach (var line in Script)
        {
            if (line.phase != currentPhase && line.phase != "observe")
            {
                _guiStatus = "Next phase...";
                yield return new WaitForSeconds(betweenPhasePause);
                currentPhase = line.phase;
            }

            if (!string.IsNullOrEmpty(line.userID) && line.userID != currentUserID)
            {
                yield return StartCoroutine(SwitchUser(line.userID));
                currentUserID = line.userID;
            }

            yield return StartCoroutine(RunLine(line));
        }

        _guiStatus = "Demo complete.";
        Debug.Log("[DemoController] Demo complete.");
    }

    IEnumerator RunLine(DemoLine line)
    {
        if (line.phase == "observe")
        {
            yield return StartCoroutine(RunObservationPhase());
            yield break;
        }

        _guiPhase  = line.phase == "cold" ? "Phase 1: Cold Start" : "Phase 3: Personalized";
        _guiUser   = line.userID;
        _guiStatus = "User speaking...";

        if (userBubble != null)
            yield return StartCoroutine(TypewriterBubble(line.query));

        yield return new WaitForSeconds(afterUserBubble);

        _guiStatus = "Waiting for robot...";
        string intentType = "chat";
        yield return StartCoroutine(SendQuery(line.query, line.userID, r => intentType = r));

        bool isChat = intentType == "chat" || intentType == "interrupt";
        _guiStatus  = isChat ? "Chat response" : $"Navigating ({intentType})";

        if (isChat)
            yield return new WaitForSeconds(afterChatResponse);
        else
            yield return StartCoroutine(WaitForNavArrival());

        yield return new WaitForSeconds(afterArrival);
        _guiStatus = "";
    }

    IEnumerator RunObservationPhase()
    {
        _guiPhase  = "Phase 2: Camera Observation";
        _guiStatus = "Observing User_Mom...";

        if (userDad != null) userDad.gameObject.SetActive(false);
        if (userMom != null) userMom.gameObject.SetActive(true);

        for (int i = 0; i < observeActionsCount; i++)
        {
            _guiStatus = $"User_Mom: Drink ({i + 1}/{observeActionsCount})";
            yield return StartCoroutine(userMom.SwitchActivity("Drink"));
            yield return new WaitForSeconds(observeWaitPerAction);
            yield return StartCoroutine(userMom.ReturnToStanding());
            yield return new WaitForSeconds(1f);
        }

        _guiStatus = "Observing User_Dad...";

        if (userMom != null) userMom.gameObject.SetActive(false);
        if (userDad != null) userDad.gameObject.SetActive(true);

        for (int i = 0; i < observeActionsCount; i++)
        {
            _guiStatus = $"User_Dad: Typing ({i + 1}/{observeActionsCount})";
            yield return StartCoroutine(userDad.SwitchActivity("Typing"));
            yield return new WaitForSeconds(observeWaitPerAction);
            yield return StartCoroutine(userDad.ReturnToStanding());
            yield return new WaitForSeconds(1f);
        }

        _guiStatus = "Waiting for SKILL.md update...";
        yield return new WaitForSeconds(afterObserveWait);

        if (userDad != null) userDad.gameObject.SetActive(false);
        if (userMom != null)
        {
            userMom.gameObject.SetActive(true);
            yield return StartCoroutine(userMom.SwitchActivity("Watching"));
        }

        _guiStatus = "Observation complete.";
        yield return new WaitForSeconds(betweenPhasePause);
    }

    IEnumerator SwitchUser(string targetID)
    {
        _guiStatus = $"Switching to {targetID}...";

        UserEntity target = targetID == "User_Mom" ? userMom : userDad;
        UserEntity other  = targetID == "User_Mom" ? userDad : userMom;

        if (other  != null) other.gameObject.SetActive(false);
        if (target != null)
        {
            target.gameObject.SetActive(true);
            if (!target.IsBusy)
                yield return StartCoroutine(target.SwitchActivity("Watching"));
        }

        yield return new WaitForSeconds(1.5f);
        _guiStatus = "";
    }

    IEnumerator TypewriterBubble(string text)
    {
        string current = "";
        userBubble.ShowMessage("");
        foreach (char c in text)
        {
            current += c;
            userBubble.ShowMessage(current);
            yield return new WaitForSeconds(typewriterSpeed);
        }
    }

    IEnumerator SendQuery(string query, string userID,
                          System.Action<string> onIntent)
    {
        string escaped = query.Replace("\"", "\\\"");
        string json    = $"{{\"query\":\"{escaped}\"," +
                         $"\"userID\":\"{userID}\"," +
                         $"\"room\":\"\"}}";
        byte[] body    = System.Text.Encoding.UTF8.GetBytes(json);

        using var req = new UnityWebRequest($"{backendUrl}/interact/stream", "POST");
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 60;

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[DemoController] Request failed: {req.error}");
            onIntent?.Invoke("chat");
            yield break;
        }

        string raw        = req.downloadHandler.text;
        string intentType = "chat";

        foreach (var line in raw.Split('\n'))
        {
            if (!line.StartsWith("data: ")) continue;
            string data = line.Substring(6).Trim();
            if (string.IsNullOrEmpty(data)) continue;
            try
            {
                var m = Regex.Match(data, "\"intent\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success) intentType = m.Groups[1].Value;
            }
            catch { }
        }

        onIntent?.Invoke(intentType);
    }

    IEnumerator WaitForNavArrival()
    {
        if (navBrain == null) yield break;

        var agent = navBrain.GetComponent<UnityEngine.AI.NavMeshAgent>();
        yield return new WaitForSeconds(2f);

        float elapsed = 0f;
        while (elapsed < navTimeout)
        {
            yield return new WaitForSeconds(navPollInterval);
            elapsed += navPollInterval;

            if (agent == null) yield break;
            if (!agent.pathPending &&
                agent.remainingDistance <= agent.stoppingDistance + 0.1f)
                yield break;
        }

        Debug.Log("[DemoController] Nav timeout, continuing.");
    }

    void OnGUI()
    {
        if (!_showGui) return;

        GUI.Box(new Rect(8, 8, 440, 90), "");
        GUI.Label(new Rect(16, 14, 420, 22), $"[Demo] {_guiPhase}");

        if (!string.IsNullOrEmpty(_guiUser))
            GUI.Label(new Rect(16, 36, 420, 22), $"User: {_guiUser}");

        if (!string.IsNullOrEmpty(_guiStatus))
            GUI.Label(new Rect(16, 58, 420, 22), $"Status: {_guiStatus}");

        string hint =
            _guiPhase.Contains("1") ? "SKILL.md is empty" :
            _guiPhase.Contains("2") ? "Camera learning habits..." :
            _guiPhase.Contains("3") ? "Personalized responses active" : "";

        if (!string.IsNullOrEmpty(hint))
            GUI.Label(new Rect(Screen.width - 340, 14, 320, 22), $"[ {hint} ]");
    }
}