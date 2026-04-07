using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// DialogueUI — polls /last_answer from Flask and displays on screen.
/// Attach to a Canvas GameObject. Assign answerText (TMP_Text).
/// </summary>
public class DialogueUI : MonoBehaviour
{
    [Header("Backend")]
    public string backendUrl   = "http://localhost:5000";
    public float  pollInterval = 0.8f;

    [Header("UI")]
    public TMP_Text answerText;

    [Tooltip("How long to show the answer before fading (0 = stay forever)")]
    public float displayDuration = 0f;

    string _lastAnswer = "";

    void Start()
    {
        if (answerText != null)
            answerText.text = "";
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

        var data = JsonUtility.FromJson<AnswerResponse>(req.downloadHandler.text);
        string answer = data?.answer ?? "";

        if (answer == _lastAnswer || string.IsNullOrEmpty(answer))
            yield break;

        _lastAnswer = answer;

        if (answerText != null)
        {
            answerText.text = answer;
            Debug.Log($"[DialogueUI] Updated: {answer}");

            if (displayDuration > 0f)
            {
                yield return new WaitForSeconds(displayDuration);
                answerText.text = "";
            }
        }
    }

    [System.Serializable]
    class AnswerResponse { public string answer; }
}