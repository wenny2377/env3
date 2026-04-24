using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

public class ProactiveServiceManager : MonoBehaviour
{
    [Header("Backend Settings")]
    public string backendURL    = "http://localhost:5000";
    public float  pollInterval  = 3f;

    [Header("User")]
    public string userID = "User_Mom";

    [Header("UI Elements")]
    public GameObject proposalPanel;
    public TMP_Text   questionText;
    public TMP_Text   confidenceText;

    [Header("Character (Nod Animation)")]
    public UserEntity userEntity;

    bool      _hasPending       = false;
    string    _pendingProposalId = "";
    string    _pendingAction     = "";
    Coroutine _autoIgnoreCoroutine;

    void Start()
    {
        if (proposalPanel != null)
            proposalPanel.SetActive(false);

        StartCoroutine(PollLoop());
    }

    IEnumerator PollLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(pollInterval);
            if (!_hasPending)
                yield return StartCoroutine(FetchProposal());
        }
    }

    IEnumerator FetchProposal()
    {
        using var req = UnityWebRequest.Get($"{backendURL}/service_proposal");
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            yield break;

        var data = JsonUtility.FromJson<ProposalResponse>(
            req.downloadHandler.text);

        if (data == null || string.IsNullOrEmpty(data.proposal_id))
            yield break;

        if (string.IsNullOrEmpty(data.message))
            yield break;

        ShowProposal(data);
    }

    void ShowProposal(ProposalResponse p)
    {
        _hasPending        = true;
        _pendingProposalId = p.proposal_id;
        _pendingAction     = p.intent;

        if (questionText   != null) questionText.text   = p.message;
        if (confidenceText != null)
            confidenceText.text = $"Confidence: {p.confidence:P0}";
        if (proposalPanel  != null) proposalPanel.SetActive(true);

        if (_autoIgnoreCoroutine != null)
            StopCoroutine(_autoIgnoreCoroutine);
        _autoIgnoreCoroutine = StartCoroutine(AutoIgnore(30f));

        Debug.Log($"[Proposal] {p.message} " +
                  $"(intent={p.intent}, conf={p.confidence:F2})");
    }

    public void OnAccept()
    {
        if (_autoIgnoreCoroutine != null)
            StopCoroutine(_autoIgnoreCoroutine);

        HidePanel();

        if (userEntity != null)
            StartCoroutine(userEntity.Nod());

        StartCoroutine(PostResponse("accepted"));
    }

    public void OnReject()
    {
        if (_autoIgnoreCoroutine != null)
            StopCoroutine(_autoIgnoreCoroutine);

        HidePanel();
        StartCoroutine(PostResponse("rejected"));
    }

    IEnumerator AutoIgnore(float timeout)
    {
        yield return new WaitForSeconds(timeout);
        if (_hasPending)
        {
            HidePanel();
            StartCoroutine(PostResponse("ignored"));
        }
    }

    IEnumerator PostResponse(string result)
    {
        string body =
            $"{{" +
            $"\"user_id\":\"{userID}\"," +
            $"\"proposal_id\":\"{_pendingProposalId}\"," +
            $"\"result\":\"{result}\"" +
            $"}}";

        using var req = new UnityWebRequest(
            $"{backendURL}/service_response", "POST");
        req.uploadHandler = new UploadHandlerRaw(
            System.Text.Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 5;
        yield return req.SendWebRequest();

        Debug.Log($"[Proposal] Response sent: {result} " +
                  $"(proposal_id={_pendingProposalId})");
    }

    void HidePanel()
    {
        _hasPending = false;
        if (proposalPanel != null)
            proposalPanel.SetActive(false);
    }

    [System.Serializable]
    class ProposalResponse
    {
        public string proposal_id;
        public string user_id;
        public string intent;
        public float  confidence;
        public string message;
        public string nav_label;
    }
}