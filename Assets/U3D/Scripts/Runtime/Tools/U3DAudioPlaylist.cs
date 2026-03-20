using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace U3D
{
    public class U3DAudioPlaylist : MonoBehaviour
    {
        [Header("Audio Configuration")]
        [SerializeField] private List<AudioClip> audioClips = new List<AudioClip>();
        [SerializeField] private AudioSource audioSource;

        [Header("Volume & Pitch")]
        [SerializeField][Range(0f, 1f)] private float volumeScale = 1f;
        [SerializeField] private bool randomizeVolume = false;
        [SerializeField][Range(0f, 0.5f)] private float volumeVariation = 0.1f;
        [SerializeField] private bool randomizePitch = false;
        [SerializeField][Range(0f, 0.5f)] private float pitchVariation = 0.15f;

        [Header("Transition Settings")]
        [SerializeField][Range(0f, 10f)] private float gapBetweenClips = 0f;
        [SerializeField] private bool useCrossfade = false;
        [SerializeField][Range(0.1f, 5f)] private float fadeDuration = 1f;
        [SerializeField] private bool fadeOutOnStop = true;

        [Header("Looping")]
        [SerializeField] private bool loopPlayback = false;

        private AudioSource _sourceA;
        private AudioSource _sourceB;
        private AudioSource _activeSource;
        private Coroutine _playbackCoroutine;
        private Coroutine _fadeOutCoroutine;
        private bool _isPlaying;
        private float _originalPitch = 1f;

        private void Awake()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                    audioSource = GetComponentInChildren<AudioSource>();
            }

            if (audioSource != null)
            {
                _sourceA = audioSource;
                _originalPitch = audioSource.pitch;
                audioSource.playOnAwake = false;
            }
        }

        // ───────────────────────────────────────────
        // Public Methods (UnityEvent-compatible)
        // ───────────────────────────────────────────

        public void PlayRandomOneShot()
        {
            if (!ValidateSetup()) return;

            AudioClip clip = GetRandomClip();
            if (clip == null) return;

            ApplyRandomization(_sourceA);
            _sourceA.PlayOneShot(clip, GetRandomizedVolume());
        }

        public void PlaySequence()
        {
            if (!ValidateSetup()) return;
            StopPlaybackImmediate();

            _playbackCoroutine = StartCoroutine(SequenceCoroutine());
        }

        public void PlayShuffled()
        {
            if (!ValidateSetup()) return;
            StopPlaybackImmediate();

            _playbackCoroutine = StartCoroutine(ShuffledCoroutine());
        }

        public void StopPlaylist()
        {
            if (_playbackCoroutine != null)
            {
                StopCoroutine(_playbackCoroutine);
                _playbackCoroutine = null;
            }

            _isPlaying = false;

            if (fadeOutOnStop && (_sourceA.isPlaying || (_sourceB != null && _sourceB.isPlaying)))
            {
                _fadeOutCoroutine = StartCoroutine(FadeOutAllCoroutine());
            }
            else
            {
                StopAllSources();
            }
        }

        // ───────────────────────────────────────────
        // Clip Management
        // ───────────────────────────────────────────

        public void AddAudioClip(AudioClip clip)
        {
            if (clip != null && !audioClips.Contains(clip))
                audioClips.Add(clip);
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

        // ───────────────────────────────────────────
        // Sequential Playback
        // ───────────────────────────────────────────

        private IEnumerator SequenceCoroutine()
        {
            _isPlaying = true;

            do
            {
                for (int i = 0; i < audioClips.Count; i++)
                {
                    if (!_isPlaying) yield break;

                    AudioClip clip = audioClips[i];
                    if (clip == null) continue;

                    yield return StartCoroutine(PlayClipCoroutine(clip));

                    if (!_isPlaying) yield break;

                    if (gapBetweenClips > 0f && i < audioClips.Count - 1)
                        yield return new WaitForSeconds(gapBetweenClips);
                }
            }
            while (loopPlayback && _isPlaying);

            _isPlaying = false;
        }

        // ───────────────────────────────────────────
        // Shuffled Playback
        // ───────────────────────────────────────────

        private IEnumerator ShuffledCoroutine()
        {
            _isPlaying = true;
            List<int> shuffledIndices = new List<int>();

            do
            {
                shuffledIndices = GetShuffledIndices();

                for (int i = 0; i < shuffledIndices.Count; i++)
                {
                    if (!_isPlaying) yield break;

                    AudioClip clip = audioClips[shuffledIndices[i]];
                    if (clip == null) continue;

                    yield return StartCoroutine(PlayClipCoroutine(clip));

                    if (!_isPlaying) yield break;

                    if (gapBetweenClips > 0f && i < shuffledIndices.Count - 1)
                        yield return new WaitForSeconds(gapBetweenClips);
                }
            }
            while (loopPlayback && _isPlaying);

            _isPlaying = false;
        }

        // ───────────────────────────────────────────
        // Clip Playback (Handles Crossfade vs Direct)
        // ───────────────────────────────────────────

        private IEnumerator PlayClipCoroutine(AudioClip clip)
        {
            if (useCrossfade)
            {
                EnsureSecondSource();
                yield return StartCoroutine(CrossfadeToClip(clip));
            }
            else
            {
                ApplyRandomization(_sourceA);
                _sourceA.clip = clip;
                _sourceA.volume = GetRandomizedVolume();
                _sourceA.Play();
                _activeSource = _sourceA;

                yield return new WaitForSeconds(clip.length);
            }
        }

        // ───────────────────────────────────────────
        // Crossfade
        // ───────────────────────────────────────────

        private IEnumerator CrossfadeToClip(AudioClip clip)
        {
            AudioSource incoming = (_activeSource == _sourceA) ? _sourceB : _sourceA;
            AudioSource outgoing = _activeSource;

            float targetVolume = GetRandomizedVolume();

            ApplyRandomization(incoming);
            incoming.clip = clip;
            incoming.volume = 0f;
            incoming.Play();

            float outgoingStartVolume = (outgoing != null && outgoing.isPlaying) ? outgoing.volume : 0f;
            float elapsed = 0f;

            while (elapsed < fadeDuration)
            {
                if (!_isPlaying) yield break;

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fadeDuration);

                incoming.volume = Mathf.Lerp(0f, targetVolume, t);
                if (outgoing != null && outgoing.isPlaying)
                    outgoing.volume = Mathf.Lerp(outgoingStartVolume, 0f, t);

                yield return null;
            }

            incoming.volume = targetVolume;
            if (outgoing != null)
            {
                outgoing.Stop();
                outgoing.volume = 0f;
            }

            _activeSource = incoming;

            float remainingTime = clip.length - fadeDuration;
            if (remainingTime > 0f)
                yield return new WaitForSeconds(remainingTime);
        }

        // ───────────────────────────────────────────
        // Fade Out
        // ───────────────────────────────────────────

        private IEnumerator FadeOutAllCoroutine()
        {
            float startVolumeA = _sourceA.isPlaying ? _sourceA.volume : 0f;
            float startVolumeB = (_sourceB != null && _sourceB.isPlaying) ? _sourceB.volume : 0f;
            float elapsed = 0f;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fadeDuration);

                if (_sourceA.isPlaying)
                    _sourceA.volume = Mathf.Lerp(startVolumeA, 0f, t);
                if (_sourceB != null && _sourceB.isPlaying)
                    _sourceB.volume = Mathf.Lerp(startVolumeB, 0f, t);

                yield return null;
            }

            StopAllSources();
        }

        // ───────────────────────────────────────────
        // Randomization
        // ───────────────────────────────────────────

        private float GetRandomizedVolume()
        {
            float vol = volumeScale;
            if (randomizeVolume)
                vol += Random.Range(-volumeVariation, volumeVariation);
            return Mathf.Clamp01(vol);
        }

        private void ApplyRandomization(AudioSource source)
        {
            if (randomizePitch)
                source.pitch = _originalPitch + Random.Range(-pitchVariation, pitchVariation);
            else
                source.pitch = _originalPitch;
        }

        // ───────────────────────────────────────────
        // Helpers
        // ───────────────────────────────────────────

        private bool ValidateSetup()
        {
            if (audioClips == null || audioClips.Count == 0)
            {
                Debug.LogWarning("U3DAudioPlaylist: No audio clips assigned.", this);
                return false;
            }
            if (_sourceA == null)
            {
                Debug.LogWarning("U3DAudioPlaylist: No AudioSource assigned.", this);
                return false;
            }
            return true;
        }

        private AudioClip GetRandomClip()
        {
            AudioClip clip = audioClips[Random.Range(0, audioClips.Count)];
            if (clip == null)
                Debug.LogWarning("U3DAudioPlaylist: Selected clip is null.", this);
            return clip;
        }

        private List<int> GetShuffledIndices()
        {
            List<int> indices = new List<int>();
            for (int i = 0; i < audioClips.Count; i++)
                indices.Add(i);

            for (int i = indices.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                int temp = indices[i];
                indices[i] = indices[j];
                indices[j] = temp;
            }

            return indices;
        }

        private void EnsureSecondSource()
        {
            if (_sourceB != null) return;

            _sourceB = gameObject.AddComponent<AudioSource>();
            _sourceB.playOnAwake = false;
            _sourceB.outputAudioMixerGroup = _sourceA.outputAudioMixerGroup;
            _sourceB.spatialBlend = _sourceA.spatialBlend;
            _sourceB.minDistance = _sourceA.minDistance;
            _sourceB.maxDistance = _sourceA.maxDistance;
            _sourceB.rolloffMode = _sourceA.rolloffMode;
            _sourceB.dopplerLevel = _sourceA.dopplerLevel;
            _sourceB.spread = _sourceA.spread;
            _sourceB.priority = _sourceA.priority;
            _sourceB.reverbZoneMix = _sourceA.reverbZoneMix;
            _sourceB.bypassEffects = _sourceA.bypassEffects;
            _sourceB.bypassListenerEffects = _sourceA.bypassListenerEffects;
            _sourceB.bypassReverbZones = _sourceA.bypassReverbZones;
        }

        private void StopPlaybackImmediate()
        {
            if (_fadeOutCoroutine != null)
            {
                StopCoroutine(_fadeOutCoroutine);
                _fadeOutCoroutine = null;
            }

            if (_playbackCoroutine != null)
            {
                StopCoroutine(_playbackCoroutine);
                _playbackCoroutine = null;
            }

            _isPlaying = false;
            StopAllSources();
        }

        private void StopAllSources()
        {
            if (_sourceA != null)
            {
                _sourceA.Stop();
                _sourceA.pitch = _originalPitch;
            }
            if (_sourceB != null)
            {
                _sourceB.Stop();
                _sourceB.pitch = _originalPitch;
            }
        }

        private void OnDisable()
        {
            StopPlaybackImmediate();
        }

        private void OnDestroy()
        {
            StopPlaybackImmediate();
            if (_sourceB != null)
                Destroy(_sourceB);
        }

        private void Reset()
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = GetComponentInChildren<AudioSource>();
        }
    }
}