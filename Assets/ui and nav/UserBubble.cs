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
    public Vector3 offset = new Vector3(0f, 2.2f, 0f);

    Camera _cam;

    void Start()
    {
        _cam = Camera.main;
        if (bubbleRoot != null)
            bubbleRoot.SetActive(false);
    }

    void LateUpdate()
    {
        if (_cam == null) return;
        transform.LookAt(transform.position + _cam.transform.rotation * Vector3.forward,
                         _cam.transform.rotation * Vector3.up);
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