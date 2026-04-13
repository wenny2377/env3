using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public class RobotBubble : MonoBehaviour
{
    [Header("Backend")]
    public string backendUrl   = "http://localhost:5000";
    public float  pollInterval = 1.0f;

    [Header("Bubble UI")]
    public GameObject bubbleRoot;
    public TMP_Text   bubbleText;

    [Header("Settings")]
    public float displayDuration = 6f;

    string _lastAnswer = "";

    void Start()
    {
        if (bubbleRoot != null)
            bubbleRoot.SetActive(false);
        StartCoroutine(PollLoop());
    }

    IEnumerator PollLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(pollInterval);
            yield return StartCoroutine(FetchAnswer());
        }
    }

    IEnumerator FetchAnswer()
    {
        using var req = UnityWebRequest.Get($"{backendUrl}/last_answer");
        req.timeout = 3;
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            yield break;

        var data   = JsonUtility.FromJson<AnswerResponse>(req.downloadHandler.text);
        string ans = data?.answer ?? "";

        if (string.IsNullOrEmpty(ans) || ans == _lastAnswer)
            yield break;

        _lastAnswer = ans;
        ShowMessage(ans);
    }

    void ShowMessage(string message)
    {
        if (bubbleText != null)
            bubbleText.text = message;
        if (bubbleRoot != null)
            bubbleRoot.SetActive(true);

        StopAllCoroutines();
        StartCoroutine(PollLoop());
        StartCoroutine(HideAfter(displayDuration));
    }

    IEnumerator HideAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (bubbleRoot != null)
            bubbleRoot.SetActive(false);
    }

    [System.Serializable]
    class AnswerResponse { public string answer; }
}