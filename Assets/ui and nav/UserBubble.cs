using System.Collections;
using TMPro;
using UnityEngine;

public class UserBubble : MonoBehaviour
{
    [Header("Bubble UI")]
    public GameObject bubbleRoot;
    public TMP_Text   bubbleText;

    [Header("Settings")]
    public float displayDuration = 5f;

    void Start()
    {
        if (bubbleRoot != null)
            bubbleRoot.SetActive(false);
    }

    public void ShowMessage(string message)
    {
        if (bubbleText != null)
            bubbleText.text = message;
        if (bubbleRoot != null)
            bubbleRoot.SetActive(true);

        StopAllCoroutines();
        if (!string.IsNullOrEmpty(message))
            StartCoroutine(HideAfter(displayDuration));
    }

    public void UpdateText(string message)
    {
        if (bubbleText != null)
            bubbleText.text = message;
        if (bubbleRoot != null)
            bubbleRoot.SetActive(true);
    }

    IEnumerator HideAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (bubbleRoot != null)
            bubbleRoot.SetActive(false);
    }
}