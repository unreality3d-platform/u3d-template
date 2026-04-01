using Fusion;
using System.Runtime.InteropServices;
using U3D.Networking;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// U3D Platform Integration - Handles PayPal, professional URLs, and multiplayer auto-initialization.
/// This component works automatically. No configuration needed by creators.
/// </summary>
public class FirebaseIntegration : MonoBehaviour
{
    [Header("Platform Integration")]
    [Tooltip("When enabled, multiplayer initializes automatically on scene start.")]
    [SerializeField] private bool enableMultiplayer = true;

    [Header("Editor Multiplayer Testing")]
    [Tooltip("Leave empty for isolated testing. Set a shared name for two editors to join the same room.")]
    [SerializeField] private string editorTestRoomOverride = "";

    [Header("Debug Info (Runtime Only)")]
    [SerializeField, ReadOnly] private string detectedEnvironment = "Not running";
    [SerializeField, ReadOnly] private bool multiplayerActive = false;

    private string contentId = "creator-content";
    private int maxPlayers = 10;

    private U3DFusionNetworkManager networkManager;
    private U3DPlayerSpawner playerSpawner;

    [DllImport("__Internal")]
    private static extern void UnityCheckContentAccess(string contentId);

    [DllImport("__Internal")]
    private static extern void UnityRequestPayment(string contentId, string price);

    [DllImport("__Internal")]
    private static extern System.IntPtr UnityGetCurrentURL();

    [DllImport("__Internal")]
    private static extern System.IntPtr UnityGetDeploymentInfo();

    [DllImport("__Internal")]
    private static extern void UnityReportDeploymentMetrics(string deploymentType, string loadTime);

    [DllImport("__Internal")]
    private static extern void UnityGetPhotonToken(string roomName, string contentId);

    private bool _isConnecting = false;
    private string _pendingRoomName = "";
    private string _photonAppId = "a3df46ef-b10a-4954-8526-7a9fdd553543";
    private UserInfo _currentUserInfo;
    private DeploymentInfo _deploymentInfo;
    private float _startTime;

    [System.Serializable]
    public class UserInfo
    {
        public string userId;
        public string displayName;
        public string userType;
        public bool paypalConnected;
        public string creatorUsername;
    }

    [System.Serializable]
    public class DeploymentInfo
    {
        public string url;
        public string hostname;
        public string pathname;
        public bool isProduction;
        public bool isProfessionalURL;
        public string creatorUsername;
        public string projectName;
        public string deploymentType;
    }

    void Start()
    {
        _startTime = Time.time;
        DetectDeploymentEnvironment();
        InitializeComponents();
        CheckContentAccess();

        if (enableMultiplayer)
            AutoInitializeMultiplayer();
    }

    void InitializeComponents()
    {
        if (!enableMultiplayer) return;

        if (networkManager == null)
            networkManager = FindAnyObjectByType<U3DFusionNetworkManager>();

        if (playerSpawner == null)
            playerSpawner = FindAnyObjectByType<U3DPlayerSpawner>();

        if (networkManager == null)
        {
            var networkManagerObject = new GameObject("U3D Network Manager");
            networkManager = networkManagerObject.AddComponent<U3DFusionNetworkManager>();
        }

        if (playerSpawner == null)
        {
            var spawnerObject = new GameObject("U3D Player Spawner");
            playerSpawner = spawnerObject.AddComponent<U3DPlayerSpawner>();
        }
    }

    void DetectDeploymentEnvironment()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            var deploymentInfoPtr = UnityGetDeploymentInfo();
            var deploymentInfoJson = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(deploymentInfoPtr);

            if (!string.IsNullOrEmpty(deploymentInfoJson))
            {
                _deploymentInfo = JsonUtility.FromJson<DeploymentInfo>(deploymentInfoJson);
                detectedEnvironment = _deploymentInfo.deploymentType;

                if (_deploymentInfo.isProfessionalURL)
                {
                    contentId = $"{_deploymentInfo.creatorUsername}_{_deploymentInfo.projectName}";
                }

                Invoke(nameof(ReportDeploymentMetrics), 2f);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Platform detection failed: {e.Message}");
            detectedEnvironment = "Detection failed";
        }
#else
        detectedEnvironment = "Unity Editor";
        _deploymentInfo = new DeploymentInfo
        {
            deploymentType = "editor",
            isProduction = false,
            isProfessionalURL = false
        };
#endif
    }

    void ReportDeploymentMetrics()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (_deploymentInfo != null)
        {
            var loadTime = (Time.time - _startTime) * 1000f;
            try
            {
                UnityReportDeploymentMetrics(_deploymentInfo.deploymentType, loadTime.ToString("F0"));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to report metrics: {e.Message}");
            }
        }
#endif
    }

    void AutoInitializeMultiplayer()
    {
        if (!enableMultiplayer) return;

        if (networkManager != null && networkManager.IsConnected)
            return;

        string roomName = GetAutoRoomName();

#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            _pendingRoomName = roomName;
            UnityGetPhotonToken(roomName, contentId);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Multiplayer auto-init failed: {e.Message}");
        }
#else
        _pendingRoomName = roomName;
        var mockToken = new PhotonTokenInfo
        {
            appId = _photonAppId,
            region = "auto",
            maxPlayers = maxPlayers
        };
        StartNetworkingWithToken(mockToken);
#endif
    }

    string GetAutoRoomName()
    {
        if (_deploymentInfo != null && _deploymentInfo.isProfessionalURL)
            return $"{_deploymentInfo.creatorUsername}_{_deploymentInfo.projectName}";

#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(editorTestRoomOverride))
            return $"editor_{editorTestRoomOverride}";

        return $"editor_{SystemInfo.deviceUniqueIdentifier.Substring(0, 8)}_{contentId}";
#else
        return $"room_{contentId}";
#endif
    }

    void CheckContentAccess()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            UnityCheckContentAccess(contentId);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Access check failed: {e.Message}");
        }
#endif
    }

    public void OnAccessCheckComplete(string hasAccess)
    {
    }

    public void OnPaymentComplete(string success)
    {
        if (success == "true")
            CheckContentAccess();
    }

    public void OnPhotonTokenReceived(string tokenData)
    {
        try
        {
            var tokenInfo = JsonUtility.FromJson<PhotonTokenInfo>(tokenData);
            _photonAppId = tokenInfo.appId;
            StartNetworkingWithToken(tokenInfo);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Token parsing failed: {e.Message}");
        }
    }

    public void OnUserProfileReceived(string userDataJson)
    {
        try
        {
            _currentUserInfo = JsonUtility.FromJson<UserInfo>(userDataJson);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"User profile parsing failed: {e.Message}");
        }
    }

    async void StartNetworkingWithToken(PhotonTokenInfo tokenInfo)
    {
        if (networkManager == null) return;

        _isConnecting = true;
        multiplayerActive = false;

        try
        {
            bool success = await networkManager.StartNetworking(_pendingRoomName, tokenInfo.appId);
            multiplayerActive = success;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Networking error: {e.Message}");
        }
        finally
        {
            _isConnecting = false;
        }
    }

    public bool IsProfessionalURL()
    {
        return _deploymentInfo != null && _deploymentInfo.isProfessionalURL;
    }

    public string GetCreatorUsername()
    {
        return _deploymentInfo?.creatorUsername ?? "";
    }

    public string GetProjectName()
    {
        return _deploymentInfo?.projectName ?? "";
    }

    public bool IsMultiplayerActive()
    {
        return networkManager != null && networkManager.IsConnected;
    }

    public int GetPlayerCount()
    {
        return networkManager != null ? networkManager.PlayerCount : 0;
    }

    public void SetMultiplayerEnabled(bool enabled)
    {
        enableMultiplayer = enabled;
        if (!enabled && networkManager != null && networkManager.IsConnected)
        {
            _ = networkManager.StopNetworking();
            multiplayerActive = false;
        }
    }
}

[System.Serializable]
public class PhotonTokenInfo
{
    public string appId;
    public string region;
    public int maxPlayers;
    public string userId;
    public string username;
}

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
public class ReadOnlyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        GUI.enabled = false;
        EditorGUI.PropertyField(position, property, label, true);
        GUI.enabled = true;
    }
}
#endif

public class ReadOnlyAttribute : UnityEngine.PropertyAttribute { }