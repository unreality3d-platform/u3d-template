using UnityEngine;
using UnityEngine.Events;
using TMPro;
using Fusion;

namespace U3D
{
    public class U3DScorable : NetworkBehaviour
    {
        [Header("Score Configuration")]
        [Tooltip("Starting score value")]
        [SerializeField] private int startingScore = 0;

        [Tooltip("Amount to add per increment")]
        [SerializeField] private int incrementAmount = 1;

        [Tooltip("Amount to subtract per decrement")]
        [SerializeField] private int decrementAmount = 1;

        [Header("Display")]
        [Tooltip("TextMeshPro component to display the score. Searches this GameObject's hierarchy if not assigned.")]
        [SerializeField] private TextMeshProUGUI scoreText;

        [Tooltip("Format string for score display. Use {0} for the score value.")]
        [SerializeField] private string displayFormat = "{0}";

        [Header("Events")]
        public UnityEvent<int> OnScoreChanged;
        public UnityEvent<int> OnScoreReset;

        [Networked, OnChangedRender(nameof(OnNetworkScoreChanged))]
        private int NetworkScore { get; set; }

        private bool _isNetworked = false;
        private int _localScore;

        private void Start()
        {
            if (scoreText == null)
                scoreText = GetComponentInChildren<TextMeshProUGUI>();

            if (!_isNetworked)
            {
                _localScore = startingScore;
                UpdateDisplay(_localScore);
            }
        }

        public override void Spawned()
        {
            _isNetworked = true;

            if (scoreText == null)
                scoreText = GetComponentInChildren<TextMeshProUGUI>();

            if (Object.HasStateAuthority)
                NetworkScore = startingScore;

            // Always sync display on join — OnChangedRender won't fire at spawn.
            UpdateDisplay(NetworkScore);
        }

        private void OnNetworkScoreChanged()
        {
            UpdateDisplay(NetworkScore);
            OnScoreChanged?.Invoke(NetworkScore);
        }

        // ── Public API ─────────────────────────────────────────────────────────

        public void AddScore()
        {
            if (_isNetworked) RequestDelta(incrementAmount);
            else { _localScore += incrementAmount; UpdateDisplay(_localScore); OnScoreChanged?.Invoke(_localScore); }
        }

        public void SubtractScore()
        {
            if (_isNetworked) RequestDelta(-decrementAmount);
            else { _localScore -= decrementAmount; UpdateDisplay(_localScore); OnScoreChanged?.Invoke(_localScore); }
        }

        public void AddAmount(int amount)
        {
            if (_isNetworked) RequestDelta(amount);
            else { _localScore += amount; UpdateDisplay(_localScore); OnScoreChanged?.Invoke(_localScore); }
        }

        public void SetScore(int value)
        {
            if (_isNetworked)
            {
                if (Object.HasStateAuthority) NetworkScore = value;
                else RPC_SetScore(value);
            }
            else { _localScore = value; UpdateDisplay(_localScore); OnScoreChanged?.Invoke(_localScore); }
        }

        public void ResetScore()
        {
            if (_isNetworked)
            {
                if (Object.HasStateAuthority) NetworkScore = startingScore;
                else RPC_SetScore(startingScore);
                OnScoreReset?.Invoke(NetworkScore);
            }
            else { _localScore = startingScore; UpdateDisplay(_localScore); OnScoreReset?.Invoke(_localScore); }
        }

        public int CurrentScore => _isNetworked ? NetworkScore : _localScore;

        // ── Internal ───────────────────────────────────────────────────────────

        private void RequestDelta(int delta)
        {
            if (Object.HasStateAuthority) NetworkScore += delta;
            else RPC_AddDelta(delta);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_AddDelta(int delta) => NetworkScore += delta;

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_SetScore(int value) => NetworkScore = value;

        private void UpdateDisplay(int value)
        {
            if (scoreText != null)
                scoreText.text = string.Format(displayFormat, value);
        }
    }
}