using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;
using System.Collections.Generic;
using U3D;

namespace U3D.Networking
{
    /// <summary>
    /// Enhanced player nametag component that displays above networked players
    /// Features: Player numbering, line-of-sight visibility, proper billboarding, performance optimized
    /// Proximity fade is measured from the local player's body position, not the camera,
    /// so third-person camera distance doesn't affect visibility.
    /// </summary>
    public class U3DPlayerNametag : MonoBehaviour
    {
        [Header("Nametag Configuration")]
        [SerializeField] private float maxDisplayDistance = 30f;
        [SerializeField] private float fadeStartDistance = 20f;
        [SerializeField] private Vector3 worldOffset = new Vector3(0, 2.25f, 0);
        [SerializeField] private bool requireLineOfSight = true;

        [Header("Performance Settings")]
        [SerializeField] private float updateFrequency = 0.1f;
        [SerializeField] private LayerMask lineOfSightLayers = -1;

        [Header("UI References")]
        [SerializeField] private Canvas nametagCanvas;
        [SerializeField] private TextMeshProUGUI playerNameText;
        [SerializeField] private CanvasGroup canvasGroup;

        // Static player numbering system
        private static Dictionary<PlayerRef, int> playerNumbers = new Dictionary<PlayerRef, int>();
        private static int nextPlayerNumber = 1;

        // Runtime references  
        private U3DPlayerController _playerController;
        private Camera _localPlayerCamera;
        private Transform _localPlayerTransform;
        private PlayerRef _playerRef;
        private NetworkObject _networkObject;

        // Performance optimization
        private float _lastUpdateTime;
        private float _currentAlpha = 1f;
        private bool _isInitialized = false;
        private bool _hasLineOfSight = true;

        public static void ResetPlayerNumbering()
        {
            playerNumbers.Clear();
            nextPlayerNumber = 1;
        }

        public static int GetPlayerNumber(PlayerRef playerRef)
        {
            if (!playerNumbers.ContainsKey(playerRef))
            {
                playerNumbers[playerRef] = nextPlayerNumber++;
            }
            return playerNumbers[playerRef];
        }

        public static void RemovePlayer(PlayerRef playerRef)
        {
            if (playerNumbers.ContainsKey(playerRef))
            {
                playerNumbers.Remove(playerRef);
            }
        }

        public void Initialize(U3DPlayerController playerController)
        {
            _playerController = playerController;
            _networkObject = playerController.GetComponent<NetworkObject>();

            if (_networkObject != null)
            {
                _playerRef = _networkObject.StateAuthority;
            }

            CreateNametagUI();
            FindLocalPlayerCamera();
            UpdatePlayerName();
            _isInitialized = true;
        }

        void CreateNametagUI()
        {
            if (nametagCanvas == null)
            {
                var canvasObject = new GameObject("NametagCanvas");
                canvasObject.transform.SetParent(transform);
                canvasObject.transform.localPosition = Vector3.zero;

                nametagCanvas = canvasObject.AddComponent<Canvas>();
                nametagCanvas.renderMode = RenderMode.WorldSpace;
                nametagCanvas.worldCamera = null;

                var canvasRect = nametagCanvas.GetComponent<RectTransform>();
                canvasRect.sizeDelta = new Vector2(300, 80);
                canvasRect.localScale = new Vector3(0.002f, 0.002f, 0.002f);

                canvasGroup = canvasObject.AddComponent<CanvasGroup>();
            }

            var tmpResources = new TMP_DefaultControls.Resources();

            var uiResources = new DefaultControls.Resources();
            var panelObject = DefaultControls.CreatePanel(uiResources);
            panelObject.name = "NametagPanel";
            panelObject.transform.SetParent(nametagCanvas.transform, false);

            var nametagRect = panelObject.GetComponent<RectTransform>();
            nametagRect.anchorMin = Vector2.zero;
            nametagRect.anchorMax = Vector2.one;
            nametagRect.offsetMin = Vector2.zero;
            nametagRect.offsetMax = Vector2.zero;

            U3DUIStyle.ApplyPanelStyle(panelObject);

            var panelImage = panelObject.GetComponent<Image>();
            panelImage.raycastTarget = false;

            var nameTextObject = TMP_DefaultControls.CreateText(tmpResources);
            nameTextObject.name = "PlayerName";
            nameTextObject.transform.SetParent(panelObject.transform, false);

            var nameRect = nameTextObject.GetComponent<RectTransform>();
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = Vector2.one;
            nameRect.offsetMin = new Vector2(10, 10);
            nameRect.offsetMax = new Vector2(-10, -10);

            playerNameText = nameTextObject.GetComponent<TextMeshProUGUI>();
            playerNameText.text = "Player";
            playerNameText.fontSize = 24;
            playerNameText.color = U3DUIStyle.TextColor;
            playerNameText.alignment = TextAlignmentOptions.Center;
            playerNameText.raycastTarget = false;

            playerNameText.enableAutoSizing = true;
            playerNameText.fontSizeMin = 20;
            playerNameText.fontSizeMax = 24;
        }

        void FindLocalPlayerCamera()
        {
            StartCoroutine(SearchForLocalPlayerCamera());
        }

        private System.Collections.IEnumerator SearchForLocalPlayerCamera()
        {
            float maxSearchTime = 10f;
            float searchStartTime = Time.time;

            while (_localPlayerCamera == null && (Time.time - searchStartTime) < maxSearchTime)
            {
                var allPlayers = FindObjectsByType<U3DPlayerController>(FindObjectsSortMode.None);
                foreach (var player in allPlayers)
                {
                    if (player.IsLocalPlayer)
                    {
                        _localPlayerTransform = player.transform;

                        var camera = player.GetComponentInChildren<Camera>();
                        if (camera != null && camera.enabled)
                        {
                            _localPlayerCamera = camera;
                            break;
                        }
                    }
                }

                if (_localPlayerCamera == null)
                {
                    var mainCamera = Camera.main;
                    if (mainCamera != null && mainCamera.enabled)
                    {
                        _localPlayerCamera = mainCamera;
                    }
                    else
                    {
                        var activeCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
                        foreach (var cam in activeCameras)
                        {
                            if (cam.enabled && cam.gameObject.activeInHierarchy)
                            {
                                _localPlayerCamera = cam;
                                break;
                            }
                        }
                    }
                }

                if (_localPlayerCamera != null)
                    break;

                yield return new WaitForSeconds(0.2f);
            }

            if (_localPlayerCamera != null)
            {
                if (nametagCanvas != null)
                {
                    nametagCanvas.worldCamera = _localPlayerCamera;
                }
            }
            else
            {
                Debug.LogWarning("Failed to find local player camera after 10 seconds - nametag may not billboard correctly");
            }
        }

        void UpdatePlayerName()
        {
            if (_networkObject == null || playerNameText == null) return;

            int playerNumber = GetPlayerNumber(_playerRef);
            string displayName = $"Player {playerNumber}";

            playerNameText.text = displayName;
        }

        string GetDisplayName()
        {
            if (_networkObject == null) return "Unknown Player";
            int playerNumber = GetPlayerNumber(_playerRef);
            return $"Player {playerNumber}";
        }

        void Update()
        {
            if (!_isInitialized || _playerController == null)
                return;

            if (_localPlayerCamera == null)
            {
                var allPlayers = FindObjectsByType<U3DPlayerController>(FindObjectsSortMode.None);
                foreach (var player in allPlayers)
                {
                    if (player.IsLocalPlayer)
                    {
                        _localPlayerTransform = player.transform;

                        var camera = player.GetComponentInChildren<Camera>();
                        if (camera != null && camera.enabled)
                        {
                            _localPlayerCamera = camera;
                            if (nametagCanvas != null)
                            {
                                nametagCanvas.worldCamera = _localPlayerCamera;
                            }
                            break;
                        }
                    }
                }

                if (_localPlayerCamera == null)
                    return;
            }

            if (_localPlayerTransform == null)
            {
                var localPlayer = U3DPlayerController.FindLocalPlayer();
                if (localPlayer != null)
                    _localPlayerTransform = localPlayer.transform;
            }

            if (Time.time - _lastUpdateTime < updateFrequency)
                return;

            _lastUpdateTime = Time.time;

            UpdatePosition();
            UpdateVisibilityAndLineOfSight();
            UpdateBillboarding();
        }

        void UpdatePosition()
        {
            transform.position = _playerController.transform.position + worldOffset;
        }

        void UpdateVisibilityAndLineOfSight()
        {
            // Distance fade uses the player body, not camera, so third-person zoom doesn't affect visibility
            Transform distanceSource = _localPlayerTransform != null
                ? _localPlayerTransform
                : _localPlayerCamera.transform;

            float distance = Vector3.Distance(transform.position, distanceSource.position);

            if (distance > maxDisplayDistance)
            {
                SetAlpha(0f);
                return;
            }

            if (requireLineOfSight)
            {
                _hasLineOfSight = CheckLineOfSight();
                if (!_hasLineOfSight)
                {
                    SetAlpha(0f);
                    return;
                }
            }

            // Behind-camera check still uses camera (this is a view-space check)
            Vector3 directionToNametag = (transform.position - _localPlayerCamera.transform.position).normalized;
            float dotProduct = Vector3.Dot(_localPlayerCamera.transform.forward, directionToNametag);

            if (dotProduct < 0.1f)
            {
                SetAlpha(0f);
                return;
            }

            float alpha = 1f;
            if (distance > fadeStartDistance)
            {
                float fadeRange = maxDisplayDistance - fadeStartDistance;
                float fadeProgress = (distance - fadeStartDistance) / fadeRange;
                alpha = 1f - Mathf.Clamp01(fadeProgress);
            }

            SetAlpha(alpha);
        }

        bool CheckLineOfSight()
        {
            // Line-of-sight raycast still originates from camera (what you can see)
            Vector3 rayOrigin = _localPlayerCamera.transform.position;
            Vector3 rayDirection = (transform.position - rayOrigin).normalized;
            float rayDistance = Vector3.Distance(rayOrigin, transform.position);

            if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, rayDistance, lineOfSightLayers))
            {
                if (hit.collider.transform == _playerController.transform ||
                    hit.collider.transform.IsChildOf(_playerController.transform))
                {
                    return true;
                }

                return false;
            }

            return true;
        }

        void UpdateBillboarding()
        {
            if (_localPlayerCamera != null)
            {
                Vector3 directionToCamera = _localPlayerCamera.transform.position - transform.position;
                directionToCamera.y = 0;

                if (directionToCamera != Vector3.zero)
                {
                    transform.rotation = Quaternion.LookRotation(-directionToCamera);
                }
            }
        }

        void SetAlpha(float alpha)
        {
            _currentAlpha = alpha;

            if (canvasGroup != null)
            {
                canvasGroup.alpha = alpha;
            }
        }

        void OnDestroy()
        {
            if (_networkObject != null)
            {
                RemovePlayer(_playerRef);
            }
        }

        void OnDrawGizmosSelected()
        {
            if (_localPlayerCamera != null && Application.isPlaying)
            {
                Vector3 rayOrigin = _localPlayerCamera.transform.position;
                Vector3 rayEnd = transform.position;

                Gizmos.color = _hasLineOfSight ? Color.green : Color.red;
                Gizmos.DrawLine(rayOrigin, rayEnd);

                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(transform.position, 0.5f);
            }
        }
    }
}