using UnityEngine;
using UnityEngine.Events;
using TMPro;

namespace U3D
{
    public class U3DScorable : MonoBehaviour
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

        private int currentScore;

        private void Start()
        {
            if (scoreText == null)
                scoreText = GetComponentInChildren<TextMeshProUGUI>();

            currentScore = startingScore;
            UpdateDisplay();
        }

        public void AddScore() => SetScore(currentScore + incrementAmount);

        public void SubtractScore() => SetScore(currentScore - decrementAmount);

        public void AddAmount(int amount) => SetScore(currentScore + amount);

        public void SetScore(int value)
        {
            currentScore = value;
            UpdateDisplay();
            OnScoreChanged?.Invoke(currentScore);
        }

        public void ResetScore()
        {
            currentScore = startingScore;
            UpdateDisplay();
            OnScoreReset?.Invoke(currentScore);
        }

        private void UpdateDisplay()
        {
            if (scoreText != null)
                scoreText.text = string.Format(displayFormat, currentScore);
        }

        public int CurrentScore => currentScore;
    }
}