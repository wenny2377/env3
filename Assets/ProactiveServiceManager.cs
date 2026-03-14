using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

/// <summary>
/// ProactiveServiceManager ¡X Unity ÷Ý
/// ¨C 3 ¬ם½ü¸ß GET /service_proposal
/// ¦¬¨ל´£®׳«בֵד¥Ü¹ן¸Ü®״¡A¨ֳ¦b¨ֿ¥־×ּ¦^ְ³«ב POST /service_response
/// </summary>
public class ProactiveServiceManager : MonoBehaviour
{
    [Header("«ב÷Ý³]©w")]
    public string backendURL = "http://localhost:5000";
    public float pollInterval = 3f;

    [Header("¨ֿ¥־×ּ")]
    public string userID = "User_Mom";

    [Header("UI ₪¸¥ף")]
    public GameObject proposalPanel;      // ´£®׳¹ן¸Ü®״ Panel
    public TMP_Text questionText;        // °ÝֳD₪ו¦r
    public TMP_Text confidenceText;      // «H₪ß­ָ¡]Debug ¥־¡^

    [Header("¨₪¦ג¡]ֲIְY¥־¡^")]
    public UserEntity userEntity;

    // ¢w¢w ₪÷³¡×¬÷A ¢w¢w
    private bool isPolling = false;
    private bool hasPending = false;
    private string pendingAction;

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    void Start()
    {
        if (proposalPanel != null)
            proposalPanel.SetActive(false);

        StartCoroutine(PollLoop());
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    IEnumerator PollLoop()
    {
        isPolling = true;
        while (isPolling)
        {
            yield return new WaitForSeconds(pollInterval);

            // ¦³ pending ´£®׳®ֹ₪£­«½ֶ½ü¸ß
            if (!hasPending)
                yield return StartCoroutine(FetchProposal());
        }
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    IEnumerator FetchProposal()
    {
        string url = $"{backendURL}/service_proposal?userID={userID}";
        using var req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            // «ב÷Ý₪£¦b½u¡AְRְq©¿²₪
            yield break;
        }

        var json = req.downloadHandler.text;
        var data = JsonUtility.FromJson<ProposalResponse>(json);

        if (data?.proposal == null || string.IsNullOrEmpty(data.proposal.question))
            yield break;

        // ¦¬¨ל´£®׳ ¡ק ֵד¥Ü UI
        ShowProposal(data.proposal);
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    void ShowProposal(ProposalData p)
    {
        hasPending = true;
        pendingAction = p.predicted_action;

        if (questionText != null) questionText.text = p.question;
        if (confidenceText != null) confidenceText.text = $"«H₪ß­ָ¡G{p.confidence:P0}";
        if (proposalPanel != null) proposalPanel.SetActive(true);

        // 30 ¬םµL¦^ְ³ ¡ק ¦Û°Êµר¬° ignored
        StartCoroutine(AutoIgnore(30f));

        Debug.Log($"[Proposal] ¦¬¨ל´£®׳¡G{p.question}¡]{p.predicted_action}, conf={p.confidence:F2}¡^");
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // ¢w¢w «צ¶s¡G±µ¨ü ¢w¢w
    public void OnAccept()
    {
        StopAllCoroutines();
        HidePanel();

        // ¨₪¦גֲIְY
        if (userEntity != null)
            StartCoroutine(userEntity.Nod());

        StartCoroutine(PostResponse("accepted"));
        StartCoroutine(PollLoop()); // ­«±ׂ½ü¸ß
    }

    // ¢w¢w «צ¶s¡G©Úµ´ ¢w¢w
    public void OnReject()
    {
        StopAllCoroutines();
        HidePanel();
        StartCoroutine(PostResponse("rejected"));
        StartCoroutine(PollLoop());
    }

    // ¢w¢w 30 ¬ם¶W®ֹ ¡ק ignored ¢w¢w
    IEnumerator AutoIgnore(float timeout)
    {
        yield return new WaitForSeconds(timeout);
        if (hasPending)
        {
            HidePanel();
            StartCoroutine(PostResponse("ignored"));
        }
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    IEnumerator PostResponse(string result)
    {
        string url = $"{backendURL}/service_response";
        string body = $"{{\"userID\":\"{userID}\",\"result\":\"{result}\"}}";

        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();

        Debug.Log($"[Proposal] ¦^ְ³°e¥X¡G{result}");
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    void HidePanel()
    {
        hasPending = false;
        if (proposalPanel != null)
            proposalPanel.SetActive(false);
    }

    // שששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש
    // JSON ₪ֿ§ַ¦C₪ֶ¥־
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