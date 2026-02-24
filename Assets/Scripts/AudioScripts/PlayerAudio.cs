using UnityEngine;

public class PlayerAudio : MonoBehaviour
{
    [SerializeField] private AudioSource audio3D;
    [SerializeField] private AudioSource audio2D;

    // Play 3D sound
    public void Play3D(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null || audio3D == null) return;

        audio3D.pitch = pitch;
        audio3D.PlayOneShot(clip, volume);
        audio3D.pitch = 1f; // reset pitch
    }

    // Play 2D sound
    public void Play2D(AudioClip clip, float volume = 1f)
    {
        if (clip == null || audio2D == null) return;

        audio2D.PlayOneShot(clip, volume);
    }
}