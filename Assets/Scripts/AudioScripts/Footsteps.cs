using UnityEngine;
using System.Collections;

public class Footsteps : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private AudioSource audioSource;

    [Header("Footstep Settings")]
    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField] private float minSpeedToStep = 0.2f;
    [SerializeField] private float baseStepDelay = 0.5f;

    private bool isStepping;

    void Update()
    {
        if (playerMovement == null) return;

        float speed = playerMovement.CurrentSpeed;

        if (speed > minSpeedToStep && playerMovement.IsGrounded)
        {
            if (!isStepping)
            {
                StartCoroutine(Footstep(speed));
            }
        }
    }

    IEnumerator Footstep(float speed)
    {
        isStepping = true;

        // Pick random clip
        AudioClip clip = footstepClips[Random.Range(0, footstepClips.Length)];

        // Slight pitch variation
        audioSource.pitch = Random.Range(0.95f, 1.05f);

        // Play clip
        audioSource.PlayOneShot(clip);

        // Faster movement = shorter delay
        float delay = baseStepDelay / Mathf.Max(speed, 1f);
        delay = Mathf.Clamp(delay, 0.25f, 0.7f);

        yield return new WaitForSeconds(delay);

        isStepping = false;
    }
}