using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;
using System.Collections.Generic;

namespace U3D.Networking
{
    /// <summary>
    /// Enhanced player nametag component that displays above networked players
    /// Features: Player numbering, line-of-sight visibility, proper billboarding, performance optimized
    /// </summary>
    public class U3DPlayerNametag : MonoBehaviour
    {
        [Header("Nametag Configuration")]
        [SerializeField] private float maxDisplayDistance = 30f;
        [SerializeField] private float fadeStartDistance = 20f;
        [SerializeField] private Vector3 worldOffset = new Vector3(0, 2.25f, 0);
        [SerializeField] private bool requireLineOfSight = true;

        [Header("Performance Settings")]
        [SerializeField] private float updateFrequency = 0.1f; // Update every 100ms for performance
        [SerializeField] private LayerMask lineOfSightLayers = -1; // What blocks line of sight

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
            Debug.Log("🔢 Player numbering system reset");
        }

        public static int GetPlayerNumber(PlayerRef playerRef)
        {
            if (!playerNumbers.ContainsKey(playerRef))
            {
                playerNumbers[playerRef] = nextPlayerNumber++;
                Debug.Log($"🆔 Assigned Player {playerNumbers[playerRef]} to {playerRef}");
            }
            return playerNumbers[playerRef];
        }

        public static void RemovePlayer(PlayerRef playerRef)
        {
            if (playerNumbers.ContainsKey(playerRef))
            {
                int removedNumber = playerNumbers[playerRef];
                playerNumbers.Remove(playerRef);
                Debug.Log($"🗑️ Removed Player {removedNumber} ({playerRef}) from numbering system");
            }
        }

        public void Initialize(U3DPlayerController playerController)
        {
            _playerController = playerController;
            _networkObject = playerController.GetComponent<NetworkObject>();

            if (_networkObject != null)
            {
                _playerRef = _networkObject.InputAuthority;
            }

            CreateNametagUI();
            FindLocalPlayerCamera();
            UpdatePlayerName();
            _isInitialized = true;

            Debug.Log($"✅ Nametag initialized for {GetDisplayName()}");
        }

        void CreateNametagUI()
        {
            // Create canvas if not assigned
            if (nametagCanvas == null)
            {
                var canvasObject = new GameObject("NametagCanvas");
                canvasObject.transform.SetParent(transform);
                canvasObject.transform.localPosition = Vector3.zero;

                nametagCanvas = canvasObject.AddComponent<Canvas>();
                nametagCanvas.renderMode = RenderMode.WorldSpace;
                nametagCanvas.worldCamera = null; // Will be set dynamically

                // Set canvas size and scale properly for world space
                var canvasRect = nametagCanvas.GetComponent<RectTransform>();
                canvasRect.sizeDelta = new Vector2(300, 80); // Larger for better text readability
                canvasRect.localScale = new Vector3(0.002f, 0.002f, 0.002f);

                canvasGroup = canvasObject.AddComponent<CanvasGroup>();
            }

            // UPDATED: Use TMP_DefaultControls following Complete UI Creation Methods Reference
            var tmpResources = new TMP_DefaultControls.Resources();

            // Create background panel using DefaultControls
            var uiResources = new DefaultControls.Resources();
            var panelObject = DefaultControls.CreatePanel(uiResources);
            panelObject.name = "NametagPanel";
            panelObject.transform.SetParent(nametagCanvas.transform, false);

            var nametagRect = panelObject.GetComponent<RectTransform>();
            nametagRect.anchorMin = Vector2.zero;
            nametagRect.anchorMax = Vector2.one;
            nametagRect.offsetMin = Vector2.zero;
            nametagRect.offsetMax = Vector2.zero;

            // Set panel background with subtle styling
            var panelImage = panelObject.GetComponent<Image>();
            panelImage.color = new Color(0, 0, 0, 0.6f); // Semi-transparent black
            panelImage.raycastTarget = false;

            // UPDATED: Create player name text using TMP_DefaultControls
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
            playerNameText.fontSize = 24; // Largest size from reference standards
            playerNameText.color = Color.white; // High contrast for readability
            playerNameText.alignment = TextAlignmentOptions.Center;
            playerNameText.raycastTarget = false;

            // Enable auto-sizing for better text fit
            playerNameText.enableAutoSizing = true;
            playerNameText.fontSizeMin = 20;
            playerNameText.fontSizeMax = 24;
        }

        void FindLocalPlayerCamera()
        {
            // Start a coroutine to continuously search for the local player camera
            StartCoroutine(SearchForLocalPlayerCamera());
        }

        private System.Collections.IEnumerator SearchForLocalPlayerCamera()
        {
            float maxSearchTime = 10f; // Stop searching after 10 seconds
            float searchStartTime = Time.time;

            while (_localPlayerCamera == null && (Time.time - searchStartTime) < maxSearchTime)
            {
                // Search for local player camera
                var allPlayers = FindObjectsByType<U3DPlayerController>(FindObjectsSortMode.None);
                foreach (var player in allPlayers)
                {
                    if (player.IsLocalPlayer)
                    {
                        var camera = player.GetComponentInChildren<Camera>();
                        if (camera != null && camera.enabled)
                        {
                            _localPlayerCamera = camera;
                            Debug.Log($"✅ Found local player camera: {camera.name}");
                            break;
                        }
                    }
                }

                // If still not found, try alternative methods
                if (_localPlayerCamera == null)
                {
                    // Look for active camera with MainCamera tag
                    var mainCamera = Camera.main;
                    if (mainCamera != null && mainCamera.enabled)
                    {
                        _localPlayerCamera = mainCamera;
                        Debug.Log($"✅ Using main camera as fallback: {mainCamera.name}");
                    }
                    else
                    {
                        // Look for any active camera
                        var activeCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
                        foreach (var cam in activeCameras)
                        {
                            if (cam.enabled && cam.gameObject.activeInHierarchy)
                            {
                                _localPlayerCamera = cam;
                                Debug.Log($"✅ Using first active camera: {cam.name}");
                                break;
                            }
                        }
                    }
                }

                // Exit loop if found
                if (_localPlayerCamera != null)
                    break;

                // Wait a bit before searching again
                yield return new WaitForSeconds(0.2f);
            }

            // Set canvas camera once found
            if (_localPlayerCamera != null)
            {
                if (nametagCanvas != null)
                {
                    nametagCanvas.worldCamera = _localPlayerCamera;
                }
                Debug.Log($"🎯 Nametag camera set successfully: {_localPlayerCamera.name}");
            }
            else
            {
                Debug.LogWarning("⚠️ Failed to find local player camera after 10 seconds - nametag may not billboard correctly");
            }
        }

        void UpdatePlayerName()
        {
            if (_networkObject == null || playerNameText == null) return;

            int playerNumber = GetPlayerNumber(_playerRef);
            string displayName = $"Player {playerNumber}";

            playerNameText.text = displayName;
            Debug.Log($"📛 Updated nametag to: {displayName}");
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

            // If we don't have a camera yet, keep trying to find one
            if (_localPlayerCamera == null)
            {
                // Try to find camera again (networking timing issue)
                var allPlayers = FindObjectsByType<U3DPlayerController>(FindObjectsSortMode.None);
                foreach (var player in allPlayers)
                {
                    if (player.IsLocalPlayer)
                    {
                        var camera = player.GetComponentInChildren<Camera>();
                        if (camera != null && camera.enabled)
                        {
                            _localPlayerCamera = camera;
                            if (nametagCanvas != null)
                            {
                                nametagCanvas.worldCamera = _localPlayerCamera;
                            }
                            Debug.Log($"🎯 Late-found local player camera: {camera.name}");
                            break;
                        }
                    }
                }

                // If still no camera, skip this frame
                if (_localPlayerCamera == null)
                    return;
            }

            // Performance optimization: Update at specified frequency only
            if (Time.time - _lastUpdateTime < updateFrequency)
                return;

            _lastUpdateTime = Time.time;

            // Update nametag position, visibility, and rotation
            UpdatePosition();
            UpdateVisibilityAndLineOfSight();
            UpdateBillboarding();
        }

        void UpdatePosition()
        {
            // Position nametag above player with world offset
            transform.position = _playerController.transform.position + worldOffset;
        }

        void UpdateVisibilityAndLineOfSight()
        {
            // Calculate distance to local player camera
            float distance = Vector3.Distance(transform.position, _localPlayerCamera.transform.position);

            // Check if beyond max display distance
            if (distance > maxDisplayDistance)
            {
                SetAlpha(0f);
                return;
            }

            // Check line of sight if required
            if (requireLineOfSight)
            {
                _hasLineOfSight = CheckLineOfSight();
                if (!_hasLineOfSight)
                {
                    SetAlpha(0f);
                    return;
                }
            }

            // Check if behind camera
            Vector3 directionToNametag = (transform.position - _localPlayerCamera.transform.position).normalized;
            float dotProduct = Vector3.Dot(_localPlayerCamera.transform.forward, directionToNametag);

            if (dotProduct < 0.1f) // Behind camera
            {
                SetAlpha(0f);
                return;
            }

            // Calculate fade based on distance
            float alpha = 1f;
            if (distance > fadeStartDistance)
            {
                // Fade out as distance increases beyond fade start
                float fadeRange = maxDisplayDistance - fadeStartDistance;
                float fadeProgress = (distance - fadeStartDistance) / fadeRange;
                alpha = 1f - Mathf.Clamp01(fadeProgress);
            }

            SetAlpha(alpha);
        }

        bool CheckLineOfSight()
        {
            Vector3 rayOrigin = _localPlayerCamera.transform.position;
            Vector3 rayDirection = (transform.position - rayOrigin).normalized;
            float rayDistance = Vector3.Distance(rayOrigin, transform.position);

            // Perform raycast to check for obstructions
            if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, rayDistance, lineOfSightLayers))
            {
                // Check if the hit object is the player or a child of the player
                if (hit.collider.transform == _playerController.transform ||
                    hit.collider.transform.IsChildOf(_playerController.transform))
                {
                    return true; // Hit the player, so line of sight is clear
                }

                return false; // Hit something else, line of sight is blocked
            }

            return true; // No hit, line of sight is clear
        }

        void UpdateBillboarding()
        {
            // Always face the local player camera (standard billboard behavior)
            if (_localPlayerCamera != null)
            {
                Vector3 directionToCamera = _localPlayerCamera.transform.position - transform.position;
                directionToCamera.y = 0; // Keep nametag upright

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
            // Cleanup player number when nametag is destroyed
            if (_networkObject != null)
            {
                RemovePlayer(_playerRef);
            }
        }

        // Debug method to show line of sight ray in Scene view
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