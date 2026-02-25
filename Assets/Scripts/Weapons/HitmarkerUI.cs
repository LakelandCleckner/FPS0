using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class HitmarkerUI : MonoBehaviour
{
    [SerializeField] private RawImage hitmarkerImage;
    [SerializeField] private float displayTime = 0.2f;
    [SerializeField] private float baseScale = 1.5f;
    [SerializeField] private float killScale = 2f;
    [SerializeField] private float scaleDuration = 0.1f;
    [SerializeField] private float maxRotation = 5f; // degrees

    private Coroutine currentRoutine;
    private Vector3 originalScale;
    private float originalRotation;

    void Awake()
    {
        if (hitmarkerImage != null)
        {
            originalScale = hitmarkerImage.rectTransform.localScale;
            originalRotation = hitmarkerImage.rectTransform.eulerAngles.z;
        }
    }
    
    public void ShowHitmarker(Color color, bool isKill = false)
    {
        if (currentRoutine != null)
            StopCoroutine(currentRoutine);

        currentRoutine = StartCoroutine(Flash(color, isKill));
    }

    private IEnumerator Flash(Color color, bool isKill)
    {
        // Set color & alpha
        Color visibleColor = color;
        visibleColor.a = 1f;
        hitmarkerImage.color = visibleColor;

        // Set scale
        float targetScale = isKill ? killScale : baseScale;
        hitmarkerImage.rectTransform.localScale = originalScale * targetScale;

        // Add slight random rotation for normal hits
        float rotation = isKill ? 0f : Random.Range(-maxRotation, maxRotation);
        hitmarkerImage.rectTransform.eulerAngles = new Vector3(0f, 0f, originalRotation + rotation);

        // Animate back to normal scale/rotation
        float timer = 0f;
        while (timer < scaleDuration)
        {
            timer += Time.deltaTime;
            hitmarkerImage.rectTransform.localScale = Vector3.Lerp(hitmarkerImage.rectTransform.localScale, originalScale, timer / scaleDuration);
            hitmarkerImage.rectTransform.eulerAngles = new Vector3(0f, 0f, Mathf.LerpAngle(hitmarkerImage.rectTransform.eulerAngles.z, originalRotation, timer / scaleDuration));
            yield return null;
        }

        // Wait for display time
        yield return new WaitForSeconds(displayTime);

        // Fade out
        visibleColor.a = 0f;
        hitmarkerImage.color = visibleColor;

        // Reset
        hitmarkerImage.rectTransform.localScale = originalScale;
        hitmarkerImage.rectTransform.eulerAngles = new Vector3(0f, 0f, originalRotation);
    }
}