using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

public class ProactiveServiceManager : MonoBehaviour
{
    [Header("Backend Settings")]
    public string backendURL = "http://localhost:5000";
    public float pollInterval = 3f;

    [Header("User")]
    public string userID = "User_Mom";

    [Header("UI Elements")]
    public GameObject proposalPanel;
    public TMP_Text questionText;
    public TMP_Text confidenceText;

    [Header("Character (Nod Animation)")]
    public UserEntity userEntity;

    private bool isPolling = false;
    private bool hasPending = false;
    private string pendingAction;

    void Start()
    {
        if (proposalPanel != null)
            proposalPanel.SetActive(false);

        StartCoroutine(PollLoop());
    }

    IEnumerator PollLoop()
    {
        isPolling = true;
        while (isPolling)
        {
            yield return new WaitForSeconds(pollInterval);

            if (!hasPending)
                yield return StartCoroutine(FetchProposal());
        }
    }

    IEnumerator FetchProposal()
    {
        string url = $"{backendURL}/service_proposal?userID={userID}";
        using var req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            yield break;
        }

        var json = req.downloadHandler.text;
        var data = JsonUtility.FromJson<ProposalResponse>(json);

        if (data?.proposal == null || string.IsNullOrEmpty(data.proposal.question))
            yield break;

        ShowProposal(data.proposal);
    }

    void ShowProposal(ProposalData p)
    {
        hasPending = true;
        pendingAction = p.predicted_action;

        if (questionText != null) questionText.text = p.question;
        if (confidenceText != null) confidenceText.text = $"Confidence: {p.confidence:P0}";
        if (proposalPanel != null) proposalPanel.SetActive(true);

        StartCoroutine(AutoIgnore(30f));

        Debug.Log($"[Proposal] Received: {p.question} ({p.predicted_action}, conf={p.confidence:F2})");
    }

    public void OnAccept()
    {
        StopAllCoroutines();
        HidePanel();

        if (userEntity != null)
            StartCoroutine(userEntity.Nod());

        StartCoroutine(PostResponse("accepted"));
        StartCoroutine(PollLoop());
    }

    public void OnReject()
    {
        StopAllCoroutines();
        HidePanel();
        StartCoroutine(PostResponse("rejected"));
        StartCoroutine(PollLoop());
    }

    IEnumerator AutoIgnore(float timeout)
    {
        yield return new WaitForSeconds(timeout);
        if (hasPending)
        {
            HidePanel();
            StartCoroutine(PostResponse("ignored"));
        }
    }

    IEnumerator PostResponse(string result)
    {
        string url = $"{backendURL}/service_response";
        string body = $"{{\"userID\":\"{userID}\",\"result\":\"{result}\"}}";

        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();

        Debug.Log($"[Proposal] Response sent: {result}");
    }

    void HidePanel()
    {
        hasPending = false;
        if (proposalPanel != null)
            proposalPanel.SetActive(false);
    }

    [System.Serializable] class ProposalResponse { public ProposalData proposal; }

    [System.Serializable]
    class ProposalData
    {
        public string user_id;
        public string predicted_action;
        public float confidence;
        public string question;
    }
}