using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

namespace U3D
{
    /// <summary>
    /// Streams a video from a URL onto a quad in the scene.
    /// The video file is not built into the WebGL player; it streams at runtime
    /// from whatever URL the creator provides (.mp4 or .webm).
    ///
    /// The editor tool creates a controls canvas as a sibling object with
    /// play/pause button, progress slider, and time display. These are
    /// fully editable in the hierarchy. If the controls references are left
    /// unassigned, the video still plays without UI.
    ///
    /// Public methods (Play, Pause, Stop, TogglePlayPause) are designed
    /// for UnityEvent wiring from triggers like U3DInteractTrigger or U3DEnterTrigger.
    /// </summary>
    [RequireComponent(typeof(VideoPlayer))]
    [RequireComponent(typeof(AudioSource))]
    public class U3DVideoPlayer : MonoBehaviour
    {
        [Header("Video Source")]
        [Tooltip("Direct URL to an .mp4 or .webm file. YouTube/Vimeo links will not work. The file streams at runtime and is not included in your build.")]
        public string videoURL = "";

        [Header("Playback Settings")]
        [Tooltip("Start playing as soon as the scene loads")]
        public bool playOnAwake = false;

        [Tooltip("Loop the video when it reaches the end")]
        public bool loopVideo = false;

        [Header("Render Settings")]
        [Tooltip("Width of the RenderTexture created at runtime. Higher values use more memory.")]
        [SerializeField] private int renderTextureWidth = 1920;

        [Tooltip("Height of the RenderTexture created at runtime. Higher values use more memory.")]
        [SerializeField] private int renderTextureHeight = 1080;

        [Header("Controls UI (Optional)")]
        [Tooltip("Button that toggles play/pause. Wired automatically by the editor tool.")]
        public Button playPauseButton;

        [Tooltip("Slider for scrubbing through the video. Wired automatically by the editor tool.")]
        public Slider progressSlider;

        [Tooltip("Text showing current time / total duration. Wired automatically by the editor tool.")]
        public TextMeshProUGUI timeDisplay;

        [Tooltip("Text on the play/pause button. Updated automatically to show Play or Pause.")]
        public TextMeshProUGUI playPauseButtonText;

        [Header("Events")]
        public UnityEvent OnVideoStarted;
        public UnityEvent OnVideoFinished;
        public UnityEvent OnVideoError;

        private VideoPlayer videoPlayer;
        private AudioSource audioSource;
        private RenderTexture renderTexture;
        private Renderer quadRenderer;
        private bool isUserScrubbing = false;
        private bool hasStartedOnce = false;

        void Awake()
        {
            videoPlayer = GetComponent<VideoPlayer>();
            audioSource = GetComponent<AudioSource>();
            quadRenderer = GetComponent<Renderer>();

            videoPlayer.hideFlags = HideFlags.HideInInspector;
            audioSource.hideFlags = HideFlags.HideInInspector;

            ConfigureVideoPlayer();
            CreateRenderTexture();
            WireControls();
        }

        void Update()
        {
            if (videoPlayer.isPrepared && !isUserScrubbing)
                UpdateControlsUI();
        }

        void OnDestroy()
        {
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }

            videoPlayer.errorReceived -= HandleVideoError;
            videoPlayer.started -= HandleVideoStarted;
            videoPlayer.loopPointReached -= HandleVideoFinished;
        }

        // ───────────────────────────────────────────
        // Configuration
        // ───────────────────────────────────────────

        private void ConfigureVideoPlayer()
        {
            videoPlayer.source = VideoSource.Url;
            if (!string.IsNullOrEmpty(videoURL))
                videoPlayer.url = videoURL;
            else if (!string.IsNullOrEmpty(videoPlayer.url))
                videoURL = videoPlayer.url;
            videoPlayer.playOnAwake = false;
            videoPlayer.isLooping = loopVideo;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            videoPlayer.SetTargetAudioSource(0, audioSource);

            videoPlayer.errorReceived += HandleVideoError;
            videoPlayer.started += HandleVideoStarted;
            videoPlayer.loopPointReached += HandleVideoFinished;

            if (playOnAwake && !string.IsNullOrEmpty(videoURL))
                Play();
        }

        private void CreateRenderTexture()
        {
            renderTexture = new RenderTexture(renderTextureWidth, renderTextureHeight, 0);
            renderTexture.Create();

            videoPlayer.targetTexture = renderTexture;

            if (quadRenderer != null)
                quadRenderer.material.mainTexture = renderTexture;
        }

        private void WireControls()
        {
            if (playPauseButton != null)
                playPauseButton.onClick.AddListener(TogglePlayPause);

            if (progressSlider != null)
            {
                progressSlider.onValueChanged.AddListener(OnSliderChanged);

                var eventTrigger = progressSlider.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();

                var pointerDown = new UnityEngine.EventSystems.EventTrigger.Entry();
                pointerDown.eventID = UnityEngine.EventSystems.EventTriggerType.PointerDown;
                pointerDown.callback.AddListener((_) => { isUserScrubbing = true; });
                eventTrigger.triggers.Add(pointerDown);

                var pointerUp = new UnityEngine.EventSystems.EventTrigger.Entry();
                pointerUp.eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp;
                pointerUp.callback.AddListener((_) => { isUserScrubbing = false; });
                eventTrigger.triggers.Add(pointerUp);
            }
        }

        // ───────────────────────────────────────────
        // Public Methods (for UnityEvent wiring)
        // ───────────────────────────────────────────

        public void Play()
        {
            if (string.IsNullOrEmpty(videoURL) && !string.IsNullOrEmpty(videoPlayer.url))
                videoURL = videoPlayer.url;

            if (string.IsNullOrEmpty(videoURL)) return;

            videoPlayer.url = videoURL;
            videoPlayer.isLooping = loopVideo;

            if (!videoPlayer.isPrepared)
            {
                videoPlayer.prepareCompleted += OnPrepareCompleted;
                videoPlayer.Prepare();
            }
            else
            {
                videoPlayer.Play();
            }
        }

        public void Pause()
        {
            if (videoPlayer.isPlaying)
                videoPlayer.Pause();
        }

        public void Stop()
        {
            videoPlayer.Stop();
            UpdatePlayPauseText(false);
        }

        public void TogglePlayPause()
        {
            if (videoPlayer.isPlaying)
                Pause();
            else
                Play();
        }

        // ───────────────────────────────────────────
        // Event Handlers
        // ───────────────────────────────────────────

        private void OnPrepareCompleted(VideoPlayer source)
        {
            videoPlayer.prepareCompleted -= OnPrepareCompleted;
            videoPlayer.Play();
        }

        private void HandleVideoStarted(VideoPlayer source)
        {
            hasStartedOnce = true;
            UpdatePlayPauseText(true);
            OnVideoStarted?.Invoke();
        }

        private void HandleVideoFinished(VideoPlayer source)
        {
            if (!loopVideo)
                UpdatePlayPauseText(false);

            OnVideoFinished?.Invoke();
        }

        private void HandleVideoError(VideoPlayer source, string message)
        {
            OnVideoError?.Invoke();
        }

        // ───────────────────────────────────────────
        // Controls UI Updates
        // ───────────────────────────────────────────

        private void UpdateControlsUI()
        {
            if (videoPlayer.frameCount == 0) return;

            double currentTime = videoPlayer.time;
            double totalTime = videoPlayer.length;

            if (progressSlider != null)
                progressSlider.SetValueWithoutNotify((float)(currentTime / totalTime));

            if (timeDisplay != null)
                timeDisplay.text = $"{FormatTime(currentTime)} / {FormatTime(totalTime)}";

            UpdatePlayPauseText(videoPlayer.isPlaying);
        }

        private void OnSliderChanged(float value)
        {
            if (!isUserScrubbing) return;
            if (!videoPlayer.isPrepared) return;

            videoPlayer.time = value * videoPlayer.length;
        }

        private void UpdatePlayPauseText(bool isPlaying)
        {
            if (playPauseButtonText != null)
                playPauseButtonText.text = isPlaying ? "Pause" : "Play";
        }

        private string FormatTime(double seconds)
        {
            int totalSeconds = Mathf.FloorToInt((float)seconds);
            int minutes = totalSeconds / 60;
            int secs = totalSeconds % 60;
            return $"{minutes}:{secs:D2}";
        }
    }
}