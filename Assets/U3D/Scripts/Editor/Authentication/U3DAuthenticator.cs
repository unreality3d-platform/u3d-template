using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using U3D.Editor;
using UnityEngine;
using UnityEditor;

public static class U3DAuthenticator
{
    private static string _idToken;
    private static string _refreshToken;
    private static string _userEmail;
    private static string _displayName;
    private static string _creatorUsername;
    private static bool _stayLoggedIn = true; // Default to true for convenience
    private static HttpClient _sharedHttpClient;
    private static bool _networkConfigured = false;
    private static bool _credentialsLoaded = false; // Track if we've loaded credentials

    // Static credential keys - no dependencies on user data
    private const string ID_TOKEN_KEY = "U3D_IdToken";
    private const string REFRESH_TOKEN_KEY = "U3D_RefreshToken";
    private const string USER_EMAIL_KEY = "U3D_UserEmail";
    private const string DISPLAY_NAME_KEY = "U3D_DisplayName";
    private const string CREATOR_USERNAME_KEY = "U3D_CreatorUsername";
    private const string STAY_LOGGED_IN_KEY = "U3D_StayLoggedIn";

    public static bool IsLoggedIn => !string.IsNullOrEmpty(_idToken);
    public static string UserEmail => _userEmail;
    public static string DisplayName => _displayName;
    public static string CreatorUsername => _creatorUsername;
    public static class CurrentUser
    {
        public static string UserId => U3DAuthenticator.IsLoggedIn ? "user-id-placeholder" : "";
        public static string Email => U3DAuthenticator.UserEmail;
        public static string DisplayName => U3DAuthenticator.DisplayName;
        public static string CreatorUsername => U3DAuthenticator.CreatorUsername;
        public static string UserType => "creator";
    }

    public static string GetIdToken() => _idToken ?? "";

    public static bool StayLoggedIn
    {
        get => _stayLoggedIn;
        set
        {
            _stayLoggedIn = value;
            EditorPrefs.SetBool(STAY_LOGGED_IN_KEY, value);

            // If user unchecks "stay logged in", clear stored credentials
            if (!value)
            {
                ClearCredentials();
            }
        }
    }

    // Static constructor to configure networking ONCE when class is first used
    static U3DAuthenticator()
    {
        ConfigureNetworking();

        LoadCredentials();

        MigrateFromOldKeys();
    }

    private static void ConfigureNetworking()
    {
        if (_networkConfigured) return;

        try
        {
            Debug.Log("🔧 Configuring network settings for Unity Editor...");

            // Configure ServicePointManager ONCE (global .NET settings)
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;
            ServicePointManager.MaxServicePointIdleTime = 90000;

            // CRITICAL FIX: Set ConnectionLeaseTimeout to solve DNS cache issues with Cloudflare
            // This forces DNS refresh every 60 seconds to prevent stale IP addresses
            ServicePointManager.DnsRefreshTimeout = 60000; // 60 seconds

            // Alternative approach: Force connection lease timeout for all endpoints
            var allEndpoints = new[]
            {
                "https://unreality3d.web.app",
                "https://unreality3d2025.web.app",
                "https://identitytoolkit.googleapis.com",
                "https://us-central1-unreality3d.cloudfunctions.net",
                "https://us-central1-unreality3d2025.cloudfunctions.net"
            };

            foreach (var endpoint in allEndpoints)
            {
                try
                {
                    var servicePoint = ServicePointManager.FindServicePoint(new Uri(endpoint));
                    servicePoint.ConnectionLeaseTimeout = 60000; // 60 seconds
                    Debug.Log($"Set ConnectionLeaseTimeout for {endpoint}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Could not set ConnectionLeaseTimeout for {endpoint}: {ex.Message}");
                }
            }

            // Create HttpClientHandler with Unity Editor optimized settings  
            var handler = new HttpClientHandler()
            {
                UseProxy = false, // Critical: Bypass Unity Editor proxy detection
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            // Create shared HttpClient (recommended pattern for app lifetime)
            _sharedHttpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(10)
            };

            // Set headers once
            _sharedHttpClient.DefaultRequestHeaders.Add("User-Agent", "Unreality3D-Unity-SDK/1.0");
            _sharedHttpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");

            _networkConfigured = true;
            Debug.Log("✅ Network configuration completed successfully with DNS cache fix");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Network configuration failed: {ex.Message}");
        }
    }

    public static async Task<bool> TryAutoLogin()
    {
        // Ensure credentials are loaded
        if (!_credentialsLoaded)
        {
            LoadCredentials();
        }

        // Check if user wants to stay logged in
        if (!_stayLoggedIn)
        {
            Debug.Log("Auto-login disabled by user preference");
            return false;
        }

        if (string.IsNullOrEmpty(_idToken))
        {
            Debug.Log("No stored credentials found - manual login required");
            return false;
        }

        try
        {
            Debug.Log("🔄 Attempting auto-login with stored credentials...");
            bool isValid = await ValidateStoredToken();

            if (isValid)
            {
                await LoadUserProfile();
                Debug.Log("✅ Auto-login successful");
                return true;
            }
            else
            {
                Debug.Log("ℹ️ Auto-login not possible - manual login required");
                return false;
            }
        }
        catch (Exception ex)
        {
            // CRITICAL: Never throw scary errors during auto-login
            Debug.Log($"ℹ️ Auto-login failed gracefully: {ex.Message}");
            ClearCredentials();
            return false;
        }
    }

    public static async Task<bool> LoginWithEmailPassword(string email, string password)
    {
        try
        {
            await EnsureFirebaseConfiguration();

            var loginData = new
            {
                email = email,
                password = password,
                returnSecureToken = true
            };

            var json = JsonConvert.SerializeObject(loginData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _sharedHttpClient.PostAsync(
                $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={FirebaseConfigManager.CurrentConfig.apiKey}",
                content);

            var responseText = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseText);

                if (result.ContainsKey("idToken"))
                {
                    _idToken = result["idToken"].ToString();
                    _refreshToken = result.ContainsKey("refreshToken") ? result["refreshToken"].ToString() : "";
                    _userEmail = email;

                    SaveCredentials();
                    await LoadUserProfile();

                    Debug.Log("Login successful");
                    return true;
                }
            }
            else
            {
                try
                {
                    var error = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseText);
                    var errorMessage = "Login failed";

                    if (error.ContainsKey("error"))
                    {
                        var errorData = JsonConvert.DeserializeObject<Dictionary<string, object>>(error["error"].ToString());
                        if (errorData.ContainsKey("message"))
                        {
                            var message = errorData["message"].ToString();
                            errorMessage = message switch
                            {
                                "INVALID_LOGIN_CREDENTIALS" => "Invalid email or password",
                                "USER_DISABLED" => "Account has been disabled",
                                "TOO_MANY_ATTEMPTS_TRY_LATER" => "Too many failed attempts. Please try again later.",
                                _ => $"Login failed: {message}"
                            };
                        }
                    }
                    else
                    {
                        errorMessage = $"Login failed: {responseText}";
                    }

                    throw new Exception(errorMessage);
                }
                catch (JsonException)
                {
                    throw new Exception($"Login failed: {responseText}");
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Login error: {ex.Message}");
            throw;
        }
    }

    public static async Task<bool> RegisterWithEmailPassword(string email, string password)
    {
        try
        {
            await EnsureFirebaseConfiguration();

            var registerData = new
            {
                email = email,
                password = password,
                returnSecureToken = true
            };

            var registerJson = JsonConvert.SerializeObject(registerData);
            var registerContent = new StringContent(registerJson, Encoding.UTF8, "application/json");

            var response = await _sharedHttpClient.PostAsync(
                $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={FirebaseConfigManager.CurrentConfig.apiKey}",
                registerContent);

            var responseText = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseText);

                if (result.ContainsKey("idToken"))
                {
                    _idToken = result["idToken"].ToString();
                    _refreshToken = result.ContainsKey("refreshToken") ? result["refreshToken"].ToString() : "";
                    _userEmail = email;

                    SaveCredentials();
                    await LoadUserProfile();

                    Debug.Log("Registration successful");
                    return true;
                }
            }
            else
            {
                var error = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseText);
                var errorMessage = "Registration failed";

                if (error.ContainsKey("error"))
                {
                    var errorData = JsonConvert.DeserializeObject<Dictionary<string, object>>(error["error"].ToString());
                    if (errorData.ContainsKey("message"))
                    {
                        var message = errorData["message"].ToString();
                        errorMessage = message switch
                        {
                            "EMAIL_EXISTS" => "Account already exists",
                            "WEAK_PASSWORD" => "Password should be at least 6 characters",
                            "INVALID_EMAIL" => "Invalid email address",
                            _ => "Registration failed"
                        };
                    }
                }

                throw new Exception(errorMessage);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Registration error: {ex.Message}");
            throw;
        }

        return false;
    }

    public static async Task<bool> CheckUsernameAvailability(string username)
    {
        try
        {
            var result = await CallFirebaseFunction("checkUsernameAvailability", new { username = username });
            return result.ContainsKey("available") && (bool)result["available"];
        }
        catch (Exception ex)
        {
            Debug.LogError($"Username check error: {ex.Message}");
            return false;
        }
    }

    public static async Task<string[]> GetUsernameSuggestions(string username)
    {
        try
        {
            var result = await CallFirebaseFunction("checkUsernameAvailability", new { username = username });
            if (result.ContainsKey("suggestions"))
            {
                var suggestions = JsonConvert.DeserializeObject<string[]>(result["suggestions"].ToString());
                return suggestions ?? new string[0];
            }
            return new string[0];
        }
        catch (Exception ex)
        {
            Debug.LogError($"Username suggestions error: {ex.Message}");
            return new string[0];
        }
    }

    public static async Task<bool> ReserveUsername(string username)
    {
        try
        {
            var result = await CallFirebaseFunction("reserveUsername", new { username = username });

            if (result.ContainsKey("success") && (bool)result["success"])
            {
                _creatorUsername = username;
                SaveCredentials();
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Username reservation error: {ex.Message}");
            throw;
        }
    }

    public static async Task ForceProfileReload()
    {
        if (string.IsNullOrEmpty(_idToken))
        {
            Debug.LogWarning("Cannot reload profile - no authentication token");
            return;
        }

        try
        {
            Debug.Log("🔄 Force reloading user profile...");
            await LoadUserProfile();
            Debug.Log($"✅ Profile reloaded - Username: '{_creatorUsername}'");
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ Force profile reload failed: {ex.Message}");
        }
    }

    public static void Logout()
    {
        // Preserve Firebase config before clearing credentials
        var currentConfig = FirebaseConfigManager.CurrentConfig;

        ClearCredentials();

        // CRITICAL FIX: Clear authorization headers from HttpClient
        if (_sharedHttpClient != null)
        {
            _sharedHttpClient.DefaultRequestHeaders.Remove("Authorization");
        }

        // Re-establish Firebase configuration to prevent login failures
        if (currentConfig != null && !string.IsNullOrEmpty(currentConfig.apiKey))
        {
            FirebaseConfigManager.SetEnvironmentConfig("production", currentConfig);
        }

        // DON'T clear GitHub token - users want to keep their GitHub setup!
        // GitHubTokenManager.ClearToken(); // ← REMOVE THIS LINE

        Debug.Log("Logged out successfully");
    }

    private static async Task<Dictionary<string, object>> CallFirebaseFunction(string functionName, object data)
    {
        await EnsureFirebaseConfiguration();

        const int maxRetries = 3;
        const int baseDelayMs = 1000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var requestData = new { data = data };
                var json = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Always clear existing Authorization header first
                _sharedHttpClient.DefaultRequestHeaders.Remove("Authorization");

                // Only add auth header if we have a valid token
                if (!string.IsNullOrEmpty(_idToken))
                {
                    _sharedHttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_idToken}");
                }

                var functionEndpoint = FirebaseConfigManager.CurrentConfig.GetFunctionEndpoint(functionName);

                Debug.Log($"🔗 Attempt {attempt}/{maxRetries}: Calling {functionName}");
                Debug.Log($"📦 Request size: {json.Length} characters ({json.Length / (1024.0 * 1024.0):F2} MB)");

                var startTime = DateTime.Now;
                var response = await _sharedHttpClient.PostAsync(functionEndpoint, content);
                var responseText = await response.Content.ReadAsStringAsync();

                var duration = DateTime.Now - startTime;
                Debug.Log($"✅ Request completed in {duration.TotalSeconds:F1} seconds");

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseText);
                    return result.ContainsKey("result") ?
                        JsonConvert.DeserializeObject<Dictionary<string, object>>(result["result"].ToString()) :
                        result;
                }
                else
                {
                    Debug.LogWarning($"❌ HTTP Error: {response.StatusCode} - {responseText}");
                    var error = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseText);
                    var errorMessage = "Function call failed";

                    if (error.ContainsKey("error"))
                    {
                        var errorData = JsonConvert.DeserializeObject<Dictionary<string, object>>(error["error"].ToString());
                        if (errorData.ContainsKey("message"))
                        {
                            errorMessage = errorData["message"].ToString();
                        }
                    }

                    throw new Exception(errorMessage);
                }
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("An error occurred while sending the request") && attempt < maxRetries)
            {
                Debug.LogWarning($"⚠️ HttpClient corrupted, recreating on attempt {attempt}/{maxRetries}: {ex.Message}");

                // Dispose corrupted HttpClient and recreate
                RecreateHttpClient();

                var delay = (int)(baseDelayMs * Math.Pow(2, attempt - 1));
                Debug.LogWarning($"🔄 Retrying in {delay}ms with fresh HttpClient...");
                await Task.Delay(delay);
                continue;
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                var delay = (int)(baseDelayMs * Math.Pow(2, attempt - 1));
                Debug.LogWarning($"⚠️ HttpRequestException on attempt {attempt}/{maxRetries}: {ex.Message}");

                // Log inner exception details for debugging
                if (ex.InnerException != null)
                {
                    Debug.LogWarning($"Inner exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                }

                Debug.LogWarning($"🔄 Retrying in {delay}ms...");
                await Task.Delay(delay);
                continue;
            }
            catch (TaskCanceledException ex) when (attempt < maxRetries && !ex.CancellationToken.IsCancellationRequested)
            {
                var delay = (int)(baseDelayMs * Math.Pow(2, attempt - 1));
                Debug.LogWarning($"⏰ Timeout on attempt {attempt}/{maxRetries}: {ex.Message}");
                Debug.LogWarning($"🔄 Retrying in {delay}ms...");
                await Task.Delay(delay);
                continue;
            }
            catch (Exception ex) when (attempt < maxRetries && IsRetriableNetworkError(ex))
            {
                var delay = (int)(baseDelayMs * Math.Pow(2, attempt - 1));
                Debug.LogWarning($"🌐 Network error on attempt {attempt}/{maxRetries}: {ex.Message}");
                Debug.LogWarning($"🔄 Retrying in {delay}ms...");
                await Task.Delay(delay);
                continue;
            }
            catch (HttpRequestException ex)
            {
                Debug.LogError($"💥 Network request failed after {maxRetries} attempts: {ex.Message}");

                // Provide specific guidance for Unity Editor network issues
                string guidance = "This appears to be a Unity Editor network configuration issue. Try:\n" +
                                "1. Restart Unity Editor\n" +
                                "2. Check Windows Firewall settings\n" +
                                "3. Disable proxy/VPN temporarily\n" +
                                "4. Try from a different network";

                throw new Exception($"Network connection failed: {ex.Message}\n\n{guidance}");
            }
        }

        throw new Exception($"Failed to call {functionName} after {maxRetries} attempts");
    }

    private static void RecreateHttpClient()
    {
        try
        {
            Debug.Log("🔧 Recreating HttpClient due to corruption...");

            // Dispose old client
            _sharedHttpClient?.Dispose();

            // Create fresh HttpClientHandler
            var handler = new HttpClientHandler()
            {
                UseProxy = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            // Create new HttpClient
            _sharedHttpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(10)
            };

            // Set headers
            _sharedHttpClient.DefaultRequestHeaders.Add("User-Agent", "Unreality3D-Unity-SDK/1.0");
            _sharedHttpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");

            Debug.Log("✅ HttpClient recreated successfully");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to recreate HttpClient: {ex.Message}");
        }
    }

    public static void Cleanup()
    {
        _sharedHttpClient?.Dispose();
        _sharedHttpClient = null;
        _networkConfigured = false;
    }

    private static bool IsRetriableNetworkError(Exception ex)
    {
        var message = ex.Message?.ToLower() ?? "";
        return message.Contains("connection") ||
               message.Contains("timeout") ||
               message.Contains("network") ||
               message.Contains("dns") ||
               message.Contains("socket") ||
               message.Contains("established connection was aborted") ||
               message.Contains("unable to read data from the transport connection");
    }

    private static async Task EnsureFirebaseConfiguration()
    {
        if (FirebaseConfigManager.IsConfigurationComplete() &&
            FirebaseConfigManager.CurrentConfig.apiKey != "setup-required")
        {
            return;
        }

        Debug.Log("Establishing Firebase configuration...");
        await EstablishDynamicConfiguration();

        if (!FirebaseConfigManager.IsConfigurationComplete())
        {
            throw new Exception("Failed to establish Firebase configuration.");
        }
    }

    private static async Task EstablishDynamicConfiguration()
    {
        try
        {
            var configUrl = "https://unreality3d.web.app/api/config";
            var response = await _sharedHttpClient.GetAsync(configUrl);

            if (response.IsSuccessStatusCode)
            {
                var configJson = await response.Content.ReadAsStringAsync();
                var firebaseConfig = JsonConvert.DeserializeObject<Dictionary<string, object>>(configJson);

                // Create production config from response
                var productionConfig = new FirebaseEnvironmentConfig
                {
                    apiKey = firebaseConfig.ContainsKey("apiKey") ? firebaseConfig["apiKey"].ToString() : "",
                    authDomain = firebaseConfig.ContainsKey("authDomain") ? firebaseConfig["authDomain"].ToString() : "",
                    projectId = firebaseConfig.ContainsKey("projectId") ? firebaseConfig["projectId"].ToString() : "",
                    storageBucket = firebaseConfig.ContainsKey("storageBucket") ? firebaseConfig["storageBucket"].ToString() : "",
                    messagingSenderId = firebaseConfig.ContainsKey("messagingSenderId") ? firebaseConfig["messagingSenderId"].ToString() : "",
                    appId = firebaseConfig.ContainsKey("appId") ? firebaseConfig["appId"].ToString() : "",
                    measurementId = firebaseConfig.ContainsKey("measurementId") ? firebaseConfig["measurementId"].ToString() : ""
                };

                // Fetch development config from secure endpoint too
                var developmentConfig = new FirebaseEnvironmentConfig();

                try
                {
                    var devConfigUrl = "https://unreality3d2025.web.app/api/config";
                    var devResponse = await _sharedHttpClient.GetAsync(devConfigUrl);

                    if (devResponse.IsSuccessStatusCode)
                    {
                        var devConfigJson = await devResponse.Content.ReadAsStringAsync();
                        var devFirebaseConfig = JsonConvert.DeserializeObject<Dictionary<string, object>>(devConfigJson);

                        developmentConfig = new FirebaseEnvironmentConfig
                        {
                            apiKey = devFirebaseConfig.ContainsKey("apiKey") ? devFirebaseConfig["apiKey"].ToString() : "setup-required",
                            authDomain = "unreality3d2025.firebaseapp.com",
                            projectId = "unreality3d2025",
                            storageBucket = "unreality3d2025.firebasestorage.app",
                            messagingSenderId = "244081840635",
                            appId = "1:244081840635:web:71c37efb6b172a706dbb5e",
                            measurementId = "G-YXC3XB3PFL"
                        };
                        Debug.Log("Development Firebase configuration loaded securely");
                    }
                    else
                    {
                        // Fallback if dev endpoint unavailable
                        developmentConfig = new FirebaseEnvironmentConfig
                        {
                            apiKey = "setup-required", // No hardcoded keys
                            authDomain = "unreality3d2025.firebaseapp.com",
                            projectId = "unreality3d2025",
                            storageBucket = "unreality3d2025.firebasestorage.app",
                            messagingSenderId = "244081840635",
                            appId = "1:244081840635:web:71c37efb6b172a706dbb5e",
                            measurementId = "G-YXC3XB3PFL"
                        };
                    }
                }
                catch (Exception devEx)
                {
                    Debug.LogWarning($"Could not fetch development config: {devEx.Message}");
                    // Safe fallback
                    developmentConfig = new FirebaseEnvironmentConfig
                    {
                        apiKey = "setup-required", // No hardcoded keys
                        authDomain = "unreality3d2025.firebaseapp.com",
                        projectId = "unreality3d2025",
                        storageBucket = "unreality3d2025.firebasestorage.app",
                        messagingSenderId = "244081840635",
                        appId = "1:244081840635:web:71c37efb6b172a706dbb5e",
                        measurementId = "G-YXC3XB3PFL"
                    };
                }

                FirebaseConfigManager.SetEnvironmentConfig("production", productionConfig);
                FirebaseConfigManager.SetEnvironmentConfig("development", developmentConfig);

                Debug.Log("Firebase configuration established securely - no hardcoded API keys");
            }
            else
            {
                Debug.LogError($"Failed to fetch secure configuration: {response.StatusCode}");
                throw new Exception("Unable to establish Firebase configuration securely");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Secure configuration establishment failed: {ex.Message}");
            throw;
        }
    }

    private static async Task<bool> RefreshIdTokenIfNeeded()
    {
        // Don't attempt refresh if we don't have a refresh token
        if (string.IsNullOrEmpty(_refreshToken))
        {
            Debug.Log("ℹ️ No refresh token available - manual login required");
            return false;
        }

        try
        {
            Debug.Log("🔄 Attempting to refresh ID token...");

            var refreshData = new
            {
                grant_type = "refresh_token",
                refresh_token = _refreshToken
            };

            var json = JsonConvert.SerializeObject(refreshData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _sharedHttpClient.PostAsync(
                $"https://securetoken.googleapis.com/v1/token?key={FirebaseConfigManager.CurrentConfig.apiKey}",
                content);

            var responseText = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseText);

                if (result.ContainsKey("id_token"))
                {
                    _idToken = result["id_token"].ToString();

                    // Update refresh token if provided
                    if (result.ContainsKey("refresh_token"))
                    {
                        _refreshToken = result["refresh_token"].ToString();
                    }

                    SaveCredentials();
                    Debug.Log("✅ ID token refreshed successfully");
                    return true;
                }
            }
            else
            {
                Debug.Log($"⚠️ Token refresh failed: {response.StatusCode} - {responseText}");

                // If refresh token is invalid, clear credentials
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    Debug.Log("🗑️ Refresh token expired - clearing credentials");
                    ClearCredentials();
                }

                return false;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"⚠️ Token refresh error: {ex.Message}");
            return false;
        }

        return false;
    }

    private static async Task<bool> ValidateStoredToken()
    {
        try
        {
            // Don't validate if we don't have a token
            if (string.IsNullOrEmpty(_idToken))
            {
                Debug.Log("No stored token to validate");
                return false;
            }

            Debug.Log("🔍 Validating stored authentication token...");

            // First attempt with current token
            try
            {
                var result = await CallFirebaseFunction("getUserProfile", new { });

                if (result.ContainsKey("userId"))
                {
                    Debug.Log("✅ Stored token is valid");
                    return true;
                }
            }
            catch (Exception ex)
            {
                var message = ex.Message?.ToLower() ?? "";

                // If token is expired/unauthenticated, try to refresh
                if (message.Contains("unauthenticated") || message.Contains("unauthorized"))
                {
                    Debug.Log("⏰ Token appears expired - attempting refresh...");

                    bool refreshed = await RefreshIdTokenIfNeeded();
                    if (refreshed)
                    {
                        // Try validation again with refreshed token
                        try
                        {
                            var retryResult = await CallFirebaseFunction("getUserProfile", new { });
                            if (retryResult.ContainsKey("userId"))
                            {
                                Debug.Log("✅ Token refreshed and validated successfully");
                                return true;
                            }
                        }
                        catch (Exception retryEx)
                        {
                            Debug.LogWarning($"⚠️ Validation failed even after refresh: {retryEx.Message}");
                        }
                    }

                    Debug.Log("ℹ️ Token refresh failed - manual login required");
                    ClearCredentials();
                    return false;
                }

                // Handle network issues gracefully
                if (IsRetriableNetworkError(ex))
                {
                    Debug.LogWarning($"🌐 Network issue during token validation: {ex.Message}");
                    return false;
                }

                Debug.LogWarning($"⚠️ Token validation failed: {ex.Message}");
                ClearCredentials();
                return false;
            }

            Debug.Log("⚠️ Token validation returned unexpected response");
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"⚠️ Token validation error: {ex.Message}");
            ClearCredentials();
            return false;
        }
    }

    private static async Task LoadUserProfile()
    {
        try
        {
            var result = await CallFirebaseFunction("getUserProfile", new { });

            if (result.ContainsKey("creatorUsername"))
            {
                _creatorUsername = result["creatorUsername"].ToString();
            }

            if (result.ContainsKey("email"))
            {
                _userEmail = result["email"].ToString();
            }

            if (result.ContainsKey("displayName"))
            {
                _displayName = result["displayName"].ToString();
            }

            Debug.Log($"Profile loaded: {_userEmail}, Username: {_creatorUsername}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Profile load failed: {ex.Message}");
        }
    }


    private static void SaveCredentials()
    {
        if (_stayLoggedIn)
        {
            if (!string.IsNullOrEmpty(_idToken))
                EditorPrefs.SetString(ID_TOKEN_KEY, _idToken);
            if (!string.IsNullOrEmpty(_refreshToken))
                EditorPrefs.SetString(REFRESH_TOKEN_KEY, _refreshToken);
            if (!string.IsNullOrEmpty(_userEmail))
                EditorPrefs.SetString(USER_EMAIL_KEY, _userEmail);
            if (!string.IsNullOrEmpty(_displayName))
                EditorPrefs.SetString(DISPLAY_NAME_KEY, _displayName);
            if (!string.IsNullOrEmpty(_creatorUsername))
                EditorPrefs.SetString(CREATOR_USERNAME_KEY, _creatorUsername);
        }
        EditorPrefs.SetBool(STAY_LOGGED_IN_KEY, _stayLoggedIn);
    }

    private static void LoadCredentials()
    {
        if (_credentialsLoaded) return;

        _idToken = EditorPrefs.GetString(ID_TOKEN_KEY, "");
        _refreshToken = EditorPrefs.GetString(REFRESH_TOKEN_KEY, "");
        _userEmail = EditorPrefs.GetString(USER_EMAIL_KEY, "");
        _displayName = EditorPrefs.GetString(DISPLAY_NAME_KEY, "");
        _creatorUsername = EditorPrefs.GetString(CREATOR_USERNAME_KEY, "");
        _stayLoggedIn = EditorPrefs.GetBool(STAY_LOGGED_IN_KEY, true);
        _credentialsLoaded = true;

        Debug.Log($"🔑 Credentials loaded: Token={!string.IsNullOrEmpty(_idToken)}, Email={_userEmail}, Username={_creatorUsername}, StayLoggedIn={_stayLoggedIn}");
    }

    private static void MigrateFromOldKeys()
    {
        // Try to find credentials from various old key patterns
        var possiblePrefixes = new[]
        {
        $"U3D_{PlayerSettings.companyName}.{PlayerSettings.productName}_",
        $"U3D_Creator_{PlayerSettings.companyName}_"
    };

        foreach (var oldPrefix in possiblePrefixes)
        {
            if (EditorPrefs.HasKey(oldPrefix + "idToken") && string.IsNullOrEmpty(_idToken))
            {
                Debug.Log($"🔄 Migrating credentials from old keys: {oldPrefix}");

                _idToken = EditorPrefs.GetString(oldPrefix + "idToken", "");
                _refreshToken = EditorPrefs.GetString(oldPrefix + "refreshToken", "");
                _userEmail = EditorPrefs.GetString(oldPrefix + "userEmail", "");
                _displayName = EditorPrefs.GetString(oldPrefix + "displayName", "");
                _creatorUsername = EditorPrefs.GetString(oldPrefix + "creatorUsername", "");
                _stayLoggedIn = EditorPrefs.GetBool(oldPrefix + "stayLoggedIn", true);

                // Save with new simple keys
                SaveCredentials();

                // Clean up old keys
                EditorPrefs.DeleteKey(oldPrefix + "idToken");
                EditorPrefs.DeleteKey(oldPrefix + "refreshToken");
                EditorPrefs.DeleteKey(oldPrefix + "userEmail");
                EditorPrefs.DeleteKey(oldPrefix + "displayName");
                EditorPrefs.DeleteKey(oldPrefix + "creatorUsername");
                EditorPrefs.DeleteKey(oldPrefix + "paypalConnected");

                Debug.Log("✅ Migration completed successfully");
                break;
            }
        }

        // Migrate PayPal email from old email-dependent keys
        MigratePayPalEmailKeys();
    }

    private static void MigratePayPalEmailKeys()
    {
        // Check if PayPal email is already migrated
        if (EditorPrefs.HasKey("U3D_PayPalEmail"))
            return;

        // Try to find PayPal email from old email-dependent keys
        var possibleEmails = new[] { _userEmail };

        // Also try loading email from old keys if not loaded yet
        if (string.IsNullOrEmpty(_userEmail))
        {
            possibleEmails = new[]
            {
            EditorPrefs.GetString("U3D_UserEmail", ""),
            EditorPrefs.GetString($"U3D_{PlayerSettings.companyName}.{PlayerSettings.productName}_userEmail", "")
        };
        }

        foreach (var email in possibleEmails)
        {
            if (!string.IsNullOrEmpty(email))
            {
                string oldKey = $"U3D_PayPalEmail_{email}";
                if (EditorPrefs.HasKey(oldKey))
                {
                    string paypalEmail = EditorPrefs.GetString(oldKey, "");
                    if (!string.IsNullOrEmpty(paypalEmail))
                    {
                        EditorPrefs.SetString("U3D_PayPalEmail", paypalEmail);
                        EditorPrefs.DeleteKey(oldKey);
                        Debug.Log($"🔄 Migrated PayPal email from {oldKey}");
                        break;
                    }
                }
            }
        }
    }

    private static void ClearCredentials()
    {
        EditorPrefs.DeleteKey(ID_TOKEN_KEY);
        EditorPrefs.DeleteKey(REFRESH_TOKEN_KEY);
        EditorPrefs.DeleteKey(USER_EMAIL_KEY);
        EditorPrefs.DeleteKey(DISPLAY_NAME_KEY);
        EditorPrefs.DeleteKey(CREATOR_USERNAME_KEY);
        // Don't clear STAY_LOGGED_IN_KEY - preserve user preference

        _idToken = "";
        _refreshToken = "";
        _userEmail = "";
        _displayName = "";
        _creatorUsername = "";

        Debug.Log("🗑️ U3D authentication credentials cleared");
    }
}

[System.Serializable]
public class U3DConfiguration
{
    public string productionApiKey;
    public string productionAuthDomain;
    public string productionProjectId;
    public string productionStorageBucket;
    public string productionMessagingSenderId;
    public string productionAppId;

    public string developmentApiKey;
    public string developmentAuthDomain;
    public string developmentProjectId;
    public string developmentStorageBucket;
    public string developmentMessagingSenderId;
    public string developmentAppId;
}