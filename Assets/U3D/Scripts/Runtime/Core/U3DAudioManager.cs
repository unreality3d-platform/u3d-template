using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using System.Collections;

public class U3DAudioManager : MonoBehaviour
{
    [Header("AudioMixer")]
    [SerializeField] private AudioMixer mixer;

    [Header("Sliders")]
    [SerializeField] private Slider mainSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider voiceSlider;

    [Header("Parameters")]
    [SerializeField] private string masterParam = "MasterVolume";
    [SerializeField] private string musicParam = "MusicVolume";
    [SerializeField] private string voiceParam = "VoiceVolume";

    [Header("WebGL Fallback")]
    [SerializeField] private AudioSource[] mainAudioSources;
    [SerializeField] private AudioSource[] musicAudioSources;
    [SerializeField] private AudioSource[] voiceAudioSources;
    [SerializeField] private bool useWebGLFallback = true;

    private bool isWebGL;
    private Coroutine[] saveCoroutines = new Coroutine[3];

    private void Start()
    {
        isWebGL = Application.platform == RuntimePlatform.WebGLPlayer;

        SetupSlider(mainSlider, masterParam, 0);
        SetupSlider(musicSlider, musicParam, 1);
        SetupSlider(voiceSlider, voiceParam, 2);

        if (isWebGL && useWebGLFallback)
        {
            AutoFindAudioSources();
        }
    }

    private void SetupSlider(Slider slider, string parameter, int index)
    {
        if (slider == null) return;

        slider.minValue = 0.0001f;
        slider.maxValue = 1f;

        // Load saved value
        float defaultValue = 0.75f;
        if (mixer.GetFloat(parameter, out float currentVolume))
        {
            defaultValue = Mathf.Pow(10f, currentVolume / 20f);
        }

        float savedValue = PlayerPrefs.GetFloat(parameter, defaultValue);
        slider.value = Mathf.Clamp(savedValue, slider.minValue, slider.maxValue);

        // Apply initial volume
        ApplyVolume(savedValue, parameter, index);

        // Connect listener
        slider.onValueChanged.AddListener(value => HandleVolumeChanged(value, parameter, index));
    }

    private void HandleVolumeChanged(float value, string parameter, int index)
    {
        ApplyVolume(value, parameter, index);

        if (saveCoroutines[index] != null)
        {
            StopCoroutine(saveCoroutines[index]);
        }
        saveCoroutines[index] = StartCoroutine(DelayedSave(value, parameter));
    }

    private void ApplyVolume(float sliderValue, string parameter, int audioIndex)
    {
        sliderValue = Mathf.Max(0.0001f, sliderValue);
        float decibelValue = Mathf.Log10(sliderValue) * 20f;

        // Try AudioMixer first
        mixer.SetFloat(parameter, decibelValue);

        // WebGL fallback
        if (isWebGL && useWebGLFallback)
        {
            AudioSource[] sources = GetAudioSourcesForIndex(audioIndex);
            foreach (var source in sources)
            {
                if (source != null)
                {
                    source.volume = sliderValue;
                }
            }
        }
    }

    private AudioSource[] GetAudioSourcesForIndex(int index)
    {
        switch (index)
        {
            case 0: return mainAudioSources ?? new AudioSource[0];
            case 1: return musicAudioSources ?? new AudioSource[0];
            case 2: return voiceAudioSources ?? new AudioSource[0];
            default: return new AudioSource[0];
        }
    }

    private void AutoFindAudioSources()
    {
        if (mainAudioSources == null || mainAudioSources.Length == 0)
        {
            mainAudioSources = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        }
    }

    private IEnumerator DelayedSave(float value, string parameter)
    {
        yield return new WaitForSeconds(0.5f);
        PlayerPrefs.SetFloat(parameter, value);
        PlayerPrefs.Save();
    }

    private void OnDestroy()
    {
        if (mainSlider != null) mainSlider.onValueChanged.RemoveAllListeners();
        if (musicSlider != null) musicSlider.onValueChanged.RemoveAllListeners();
        if (voiceSlider != null) voiceSlider.onValueChanged.RemoveAllListeners();

        foreach (var coroutine in saveCoroutines)
        {
            if (coroutine != null) StopCoroutine(coroutine);
        }
    }
}