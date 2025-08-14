using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;

/// <summary>
/// Universal UI input manager for any interactive UI component
/// Automatically detects and manages input focus for all UI types
/// </summary>
public class U3DUIInputManager : MonoBehaviour, IUIInputHandler
{
    [Header("UI Component Detection")]
    public string componentName = "UI Component";
    [SerializeField] private bool autoDetectInputFields = true;
    [SerializeField] private bool autoDetectButtons = true;
    [SerializeField] private bool autoDetectScrollViews = true;
    [SerializeField] private bool autoDetectDropdowns = true;

    [Header("Input Behavior")]
    public int inputPriority = 0;
    public bool blockAllInput = false;
    [SerializeField] private bool onlyWhenVisible = true;

    [Header("Manual UI References")]
    [SerializeField] private Selectable[] manualSelectables;
    [SerializeField] private TMP_InputField[] manualInputFields;
    [SerializeField] private ScrollRect[] manualScrollRects;

    // Auto-detected components
    private Selectable[] _autoSelectables;
    private TMP_InputField[] _autoInputFields;
    private ScrollRect[] _autoScrollRects;
    private TMP_Dropdown[] _autoDropdowns;

    private U3D.Networking.U3DFusionNetworkManager _networkManager;
    private bool _registeredWithNetwork = false;
    private bool _wasVisible = false;

    void Start()
    {
        AutoDetectUIComponents();
        RegisterWithNetworkManager();
    }

    void Update()
    {
        // Handle visibility changes for dynamic UI
        bool isCurrentlyVisible = IsUIVisible();
        if (isCurrentlyVisible != _wasVisible)
        {
            _wasVisible = isCurrentlyVisible;

            if (isCurrentlyVisible)
            {
                RegisterWithNetworkManager();
            }
            else
            {
                UnregisterFromNetworkManager();
            }
        }
    }

    void AutoDetectUIComponents()
    {
        if (autoDetectInputFields)
        {
            _autoInputFields = GetComponentsInChildren<TMP_InputField>(true);
        }

        if (autoDetectButtons)
        {
            _autoSelectables = GetComponentsInChildren<Selectable>(true);
        }

        if (autoDetectScrollViews)
        {
            _autoScrollRects = GetComponentsInChildren<ScrollRect>(true);
        }

        if (autoDetectDropdowns)
        {
            _autoDropdowns = GetComponentsInChildren<TMP_Dropdown>(true);
        }

        Debug.Log($"🔍 Auto-detected UI components in {componentName}: " +
                 $"InputFields={_autoInputFields?.Length ?? 0}, " +
                 $"Buttons={_autoSelectables?.Length ?? 0}, " +
                 $"ScrollViews={_autoScrollRects?.Length ?? 0}, " +
                 $"Dropdowns={_autoDropdowns?.Length ?? 0}");
    }

    private void RegisterWithNetworkManager()
    {
        if (_registeredWithNetwork) return;

        _networkManager = U3D.Networking.U3DFusionNetworkManager.Instance;
        if (_networkManager != null)
        {
            _networkManager.RegisterUIInputHandler(this);
            _registeredWithNetwork = true;
            Debug.Log($"🎮 {componentName} registered with network input system");
        }
    }

    private void UnregisterFromNetworkManager()
    {
        if (!_registeredWithNetwork) return;

        if (_networkManager != null)
        {
            _networkManager.UnregisterUIInputHandler(this);
            _registeredWithNetwork = false;
            Debug.Log($"🎮 {componentName} unregistered from network input system");
        }
    }

    private bool IsUIVisible()
    {
        if (!onlyWhenVisible) return true;

        // Check if the root object is active and visible
        if (!gameObject.activeInHierarchy) return false;

        // Safely check Canvas component (might not exist)
        var canvas = GetComponent<Canvas>();
        if (canvas != null && !canvas.enabled) return false;

        // Safely check CanvasGroup component (might not exist)  
        var canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup != null && canvasGroup.alpha <= 0) return false;

        // Also check parent Canvas components up the hierarchy
        var parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas != null && !parentCanvas.enabled) return false;

        var parentCanvasGroup = GetComponentInParent<CanvasGroup>();
        if (parentCanvasGroup != null && parentCanvasGroup.alpha <= 0) return false;

        return true;
    }

    // IUIInputHandler implementation
    public bool IsUIFocused()
    {
        if (onlyWhenVisible && !IsUIVisible()) return false;

        // Check manual references first
        if (IsAnyComponentFocused(manualInputFields)) return true;
        if (IsAnyComponentFocused(manualSelectables)) return true;
        if (IsAnyComponentFocused(manualScrollRects)) return true;

        // Check auto-detected components
        if (IsAnyComponentFocused(_autoInputFields)) return true;
        if (IsAnyComponentFocused(_autoSelectables)) return true;
        if (IsAnyComponentFocused(_autoScrollRects)) return true;
        if (IsAnyComponentFocused(_autoDropdowns)) return true;

        return false;
    }

    private bool IsAnyComponentFocused<T>(T[] components) where T : Component
    {
        if (components == null) return false;

        foreach (var component in components)
        {
            if (component == null || !component.gameObject.activeInHierarchy) continue;

            switch (component)
            {
                case TMP_InputField inputField:
                    if (inputField.isFocused) return true;
                    break;

                case TMP_Dropdown dropdown: // CHECK DROPDOWN FIRST (before Selectable)
                    if (dropdown.IsExpanded) return true;
                    break;

                case Selectable selectable: // Check other selectables after dropdown
                    if (UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject == selectable.gameObject)
                        return true;
                    break;

                case ScrollRect scrollRect:
                    if (scrollRect.velocity.magnitude > 0.1f) return true;
                    break;
            }
        }

        return false;
    }

    public string GetHandlerName() => componentName;
    public int GetInputPriority() => inputPriority;
    public bool ShouldBlockAllInput() => blockAllInput;

    void OnDestroy()
    {
        UnregisterFromNetworkManager();
    }

    // Editor utilities
    [ContextMenu("Refresh Auto-Detection")]
    public void RefreshAutoDetection()
    {
        AutoDetectUIComponents();
    }

    [ContextMenu("Test Focus Detection")]
    public void TestFocusDetection()
    {
        bool isFocused = IsUIFocused();
        Debug.Log($"🧪 {componentName} focus test: {(isFocused ? "FOCUSED" : "not focused")}");
    }
}