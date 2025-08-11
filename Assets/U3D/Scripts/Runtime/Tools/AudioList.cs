using System.Collections.Generic;
using UnityEngine;

namespace U3D
{
    public class AudioList : MonoBehaviour
    {
        [Header("Audio Configuration")]
        [SerializeField] private List<AudioClip> audioClips = new List<AudioClip>();
        [SerializeField] private AudioSource audioSource;

        [Header("Playback Settings")]
        [SerializeField] private float volumeScale = 1f;

        public void PlayRandomOneShot()
        {
            if (audioClips == null || audioClips.Count == 0)
            {
                Debug.LogWarning("AudioList: No audio clips assigned!", this);
                return;
            }

            if (audioSource == null)
            {
                Debug.LogWarning("AudioList: No AudioSource assigned!", this);
                return;
            }

            AudioClip randomClip = audioClips[Random.Range(0, audioClips.Count)];

            if (randomClip != null)
            {
                audioSource.PlayOneShot(randomClip, volumeScale);
            }
            else
            {
                Debug.LogWarning("AudioList: Selected audio clip is null!", this);
            }
        }

        public void AddAudioClip(AudioClip clip)
        {
            if (clip != null && !audioClips.Contains(clip))
            {
                audioClips.Add(clip);
            }
        }

        public void RemoveAudioClip(AudioClip clip)
        {
            audioClips.Remove(clip);
        }

        public void ClearAudioClips()
        {
            audioClips.Clear();
        }

        public int GetAudioClipCount()
        {
            return audioClips.Count;
        }

        private void Reset()
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = GetComponentInChildren<AudioSource>();
            }
        }
    }
}