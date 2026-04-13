using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class DemoController : MonoBehaviour
{
    [Header("Users")]
    public UserEntity userMom;

    [Header("Robot")]
    public Transform robotStartPosition;
    public NavBrain  navBrain;

    [Header("Demo Drink Objects (set active on demo start)")]
    public List<GameObject> demoDrinkObjects = new List<GameObject>();

    [Header("Held Items to Hide (skeleton-bound props)")]
    public List<GameObject> heldItemsToHide = new List<GameObject>();

    [Header("Bubbles")]
    public UserBubble  userBubble;
    public RobotBubble robotBubble;

    [Header("Timing")]
    public float typewriterSpeed   = 0.04f;
    public float afterUserBubble   = 1.5f;
    public float afterChatResponse = 18f;
    public float afterArrival      = 2.5f;
    public float navPollInterval   = 0.5f;
    public float navTimeout        = 20f;

    [Header("Backend")]
    public string backendUrl = "http://localhost:5000";
    public string userID     = "User_Mom";

    static readonly string[] Script = new[]
    {
        "I am thirsty",
        "I don't like cola, I prefer juice",
        "I am thirsty again",
        "I want cheese please",
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
        foreach (var obj in heldItemsToHide)
            if (obj != null) obj.SetActive(false);

        foreach (var obj in demoDrinkObjects)
            if (obj != null) obj.SetActive(true);

        if (robotStartPosition != null && navBrain != null)
        {
            navBrain.transform.position = robotStartPosition.position;
            navBrain.transform.rotation = robotStartPosition.rotation;
        }

        yield return new WaitForSeconds(0.5f);

        if (userMom != null && !userMom.IsBusy)
            yield return StartCoroutine(userMom.SwitchActivity("sit"));

        yield return new WaitForSeconds(1.5f);

        foreach (var line in Script)
            yield return StartCoroutine(RunLine(line));

        Debug.Log("[DemoController] Demo complete.");
    }

    IEnumerator RunLine(string query)
    {
        if (userBubble != null)
            yield return StartCoroutine(TypewriterBubble(query));

        yield return new WaitForSeconds(afterUserBubble);

        string intentType = "chat";
        yield return StartCoroutine(SendQuery(query, result => intentType = result));

        bool isChat = intentType == "chat" || intentType == "interrupt";

        if (isChat)
            yield return new WaitForSeconds(afterChatResponse);
        else
            yield return StartCoroutine(WaitForNavArrival());

        yield return new WaitForSeconds(afterArrival);
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

    IEnumerator SendQuery(string query, System.Action<string> onIntent)
    {
        string escaped = query.Replace("\"", "\\\"");
        string json    = $"{{\"query\":\"{escaped}\",\"userID\":\"{userID}\",\"room\":\"\"}}";
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
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f)
                yield break;
        }

        Debug.Log("[DemoController] Nav timeout, continuing.");
    }
}