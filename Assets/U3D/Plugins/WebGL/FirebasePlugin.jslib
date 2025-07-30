mergeInto(LibraryManager.library, {

    // ========== DIRECT PAYPAL ORDERS V2 API INTEGRATION ==========
    // No Firebase Functions required - Unity SDK → PayPal Orders v2 API direct

    // MAIN: Direct PayPal dual transaction - bypasses Firebase entirely
    UnityStartDirectPayPalTransaction: function (gameObjectNamePtr, itemNamePtr, itemDescriptionPtr, pricePtr, creatorEmailPtr, transactionIdPtr) {
        var gameObjectName = UTF8ToString(gameObjectNamePtr);
        var itemName = UTF8ToString(itemNamePtr);
        var itemDescription = UTF8ToString(itemDescriptionPtr);
        var price = parseFloat(UTF8ToString(pricePtr));
        var creatorEmail = UTF8ToString(creatorEmailPtr);
        var transactionId = UTF8ToString(transactionIdPtr);

        console.log('🚀 DIRECT PayPal dual transaction initiated');
        console.log('💳 GameObject for callbacks:', gameObjectName);
        console.log('💰 Item:', itemName, '-', itemDescription);
        console.log('💵 Total amount:', '$' + price.toFixed(2));
        console.log('📧 Creator email:', creatorEmail);
        console.log('🆔 Transaction ID:', transactionId);

        // Store the GameObject name for SendMessage callbacks
        window.currentPayPalGameObject = gameObjectName;

        // Calculate dual transaction amounts
        var creatorAmount = Math.round(price * 0.95 * 100) / 100; // 95% to creator
        var platformAmount = Math.round(price * 0.05 * 100) / 100; // 5% to platform

        console.log('💰 Creator receives:', '$' + creatorAmount.toFixed(2), '(95%)');
        console.log('💰 Platform receives:', '$' + platformAmount.toFixed(2), '(5%)');

        // Check if direct PayPal function is available
        if (typeof window.StartDirectPayPalDualTransaction === 'function') {
            window.StartDirectPayPalDualTransaction({
                itemName: itemName,
                itemDescription: itemDescription,
                totalAmount: price,
                creatorAmount: creatorAmount,
                platformAmount: platformAmount,
                creatorEmail: creatorEmail,
                platformEmail: 'laurie@unreality3d.com',
                transactionId: transactionId,
                gameObjectName: gameObjectName
            });
        } else {
            console.warn('❌ Direct PayPal function not available');
            console.warn('🔍 Make sure PayPal SDK is loaded and direct integration is configured');
            
            // Send failure back to Unity
            if (typeof window.unityInstance !== 'undefined' && window.unityInstance && window.currentPayPalGameObject) {
                window.unityInstance.SendMessage(window.currentPayPalGameObject, 'OnPaymentComplete', 'false');
            }
        }
    },

    // Test direct PayPal connection
    UnityTestDirectPayPalConnection: function (gameObjectNamePtr) {
        var gameObjectName = UTF8ToString(gameObjectNamePtr);
        
        console.log('🧪 Testing direct PayPal connection for GameObject:', gameObjectName);
        
        // Store the GameObject name
        window.currentPayPalGameObject = gameObjectName;

        // Check if PayPal SDK is available
        if (typeof paypal !== 'undefined' && paypal.Buttons) {
            console.log('✅ PayPal SDK is available');
            
            // Test connection
            if (typeof window.TestDirectPayPalConnection === 'function') {
                window.TestDirectPayPalConnection(gameObjectName);
            } else {
                console.log('✅ PayPal SDK available, direct integration ready');
                // Send success back to Unity
                if (typeof window.unityInstance !== 'undefined' && window.unityInstance) {
                    window.unityInstance.SendMessage(gameObjectName, 'OnConnectionTestComplete', 'true');
                }
            }
        } else {
            console.log('❌ PayPal SDK not available');
            // Send failure back to Unity
            if (typeof window.unityInstance !== 'undefined' && window.unityInstance) {
                window.unityInstance.SendMessage(gameObjectName, 'OnConnectionTestComplete', 'false');
            }
        }
    },

    // ========== EXISTING PAYPAL FUNCTIONS (LEGACY - FIREBASE DEPENDENT) ==========
    
    UnityCallTestFunction: function () {
        if (typeof window.UnityCallTestFunction === 'function') {
            window.UnityCallTestFunction();
        } else {
            console.warn('UnityCallTestFunction not available in browser context');
        }
    },

    UnityCheckContentAccess: function (contentIdPtr) {
        var contentId = UTF8ToString(contentIdPtr);
        if (typeof window.UnityCheckContentAccess === 'function') {
            window.UnityCheckContentAccess(contentId);
        } else {
            console.warn('UnityCheckContentAccess not available in browser context');
        }
    },

    UnityRequestPayment: function (contentIdPtr, pricePtr) {
        var contentId = UTF8ToString(contentIdPtr);
        var price = UTF8ToString(pricePtr);
        if (typeof window.UnityRequestPayment === 'function') {
            window.UnityRequestPayment(contentId, price);
        } else {
            console.warn('UnityRequestPayment not available in browser context');
        }
    },

    // ========== PROFESSIONAL URL DETECTION ==========

    UnityGetCurrentURL: function () {
        var currentUrl = window.location.href;
        var bufferSize = lengthBytesUTF8(currentUrl) + 1;
        var buffer = _malloc(bufferSize);
        stringToUTF8(currentUrl, buffer, bufferSize);
        return buffer;
    },

    UnityGetDeploymentInfo: function () {
        var deploymentInfo = {
            url: window.location.href,
            hostname: window.location.hostname,
            pathname: window.location.pathname,
            isProduction: false,
            isProfessionalURL: false,
            creatorUsername: '',
            projectName: '',
            deploymentType: 'unknown'
        };

        var hostname = window.location.hostname.toLowerCase();
        var pathname = window.location.pathname;

        if (hostname.endsWith('.unreality3d.com') && hostname !== 'unreality3d.com') {
            deploymentInfo.isProfessionalURL = true;
            deploymentInfo.isProduction = true;
            deploymentInfo.deploymentType = 'professional';
            
            var subdomain = hostname.replace('.unreality3d.com', '');
            deploymentInfo.creatorUsername = subdomain;
            
            var pathParts = pathname.split('/').filter(part => part.length > 0);
            if (pathParts.length > 0) {
                deploymentInfo.projectName = pathParts[0];
            }
            
            console.log('Professional URL detected:', deploymentInfo.creatorUsername + '.unreality3d.com/' + deploymentInfo.projectName);
            
        } else if (hostname.includes('unreality3d.web.app') || hostname.includes('unreality3d.firebaseapp.com')) {
            deploymentInfo.isProduction = hostname.includes('unreality3d.web.app');
            deploymentInfo.deploymentType = deploymentInfo.isProduction ? 'firebase-production' : 'firebase-development';
            
            console.log('Firebase hosting detected:', deploymentInfo.deploymentType);
            
        } else if (hostname.includes('unreality3d2025.web.app') || hostname.includes('unreality3d2025.firebaseapp.com')) {
            deploymentInfo.isProduction = false;
            deploymentInfo.deploymentType = 'firebase-development';
            
            console.log('Development environment detected');
            
        } else if (hostname === 'localhost' || hostname.startsWith('192.168.') || hostname.startsWith('127.0.0.1')) {
            deploymentInfo.deploymentType = 'local';
            console.log('Local development detected');
            
        } else {
            deploymentInfo.deploymentType = 'unknown';
            console.log('Unknown deployment type for hostname:', hostname);
        }

        var jsonString = JSON.stringify(deploymentInfo);
        var bufferSize = lengthBytesUTF8(jsonString) + 1;
        var buffer = _malloc(bufferSize);
        stringToUTF8(jsonString, buffer, bufferSize);
        return buffer;
    },

    UnityReportDeploymentMetrics: function (deploymentTypePtr, loadTimePtr) {
        var deploymentType = UTF8ToString(deploymentTypePtr);
        var loadTime = UTF8ToString(loadTimePtr);
        
        console.log('Unity deployment metrics:', {
            type: deploymentType,
            loadTime: loadTime + 'ms',
            timestamp: new Date().toISOString()
        });
        
        if (typeof window.UnityReportAnalyticsEvent === 'function') {
            window.UnityReportAnalyticsEvent('deployment_metrics', JSON.stringify({
                deploymentType: deploymentType,
                loadTime: loadTime,
                url: window.location.href,
                timestamp: new Date().toISOString()
            }));
        }
    },

    // ========== PHOTON FUSION MULTIPLAYER FUNCTIONS ==========

    UnityGetPhotonToken: function (roomNamePtr, contentIdPtr) {
        var roomName = UTF8ToString(roomNamePtr);
        var contentId = UTF8ToString(contentIdPtr);
        
        console.log('Unity requested Photon token for room:', roomName, 'content:', contentId);
        
        if (typeof window.UnityGetPhotonToken === 'function') {
            window.UnityGetPhotonToken(roomName, contentId);
        } else {
            console.warn('UnityGetPhotonToken not available in browser context');
            if (typeof window.unityInstance !== 'undefined' && window.unityInstance) {
                window.unityInstance.SendMessage('FirebaseIntegration', 'OnPhotonTokenReceived', 
                    JSON.stringify({ error: 'Multiplayer functions not available' }));
            }
        }
    },

    UnityCreateMultiplayerSession: function (contentIdPtr, sessionNamePtr, maxPlayersPtr) {
        var contentId = UTF8ToString(contentIdPtr);
        var sessionName = UTF8ToString(sessionNamePtr);
        var maxPlayers = UTF8ToString(maxPlayersPtr);
        
        console.log('Unity creating multiplayer session:', sessionName, 'for content:', contentId, 'max players:', maxPlayers);
        
        if (typeof window.UnityCreateMultiplayerSession === 'function') {
            window.UnityCreateMultiplayerSession(contentId, sessionName, maxPlayers);
        } else {
            console.warn('UnityCreateMultiplayerSession not available in browser context');
            if (typeof window.unityInstance !== 'undefined' && window.unityInstance) {
                window.unityInstance.SendMessage('FirebaseIntegration', 'OnSessionCreated', 
                    JSON.stringify({ error: 'Session creation not available' }));
            }
        }
    },

    UnityJoinMultiplayerSession: function (roomNamePtr) {
        var roomName = UTF8ToString(roomNamePtr);
        
        console.log('Unity joining multiplayer session:', roomName);
        
        if (typeof window.UnityJoinMultiplayerSession === 'function') {
            window.UnityJoinMultiplayerSession(roomName);
        } else {
            console.warn('UnityJoinMultiplayerSession not available in browser context');
            if (typeof window.unityInstance !== 'undefined' && window.unityInstance) {
                window.unityInstance.SendMessage('FirebaseIntegration', 'OnSessionJoinResponse', 
                    JSON.stringify({ error: 'Session join not available' }));
            }
        }
    },

    // ========== USER PROFILE FUNCTIONS ==========

    UnityGetUserProfile: function () {
        console.log('Unity requesting user profile');
        
        if (typeof window.UnityGetUserProfile === 'function') {
            window.UnityGetUserProfile();
        } else {
            console.warn('UnityGetUserProfile not available in browser context');
            if (typeof window.unityInstance !== 'undefined' && window.unityInstance) {
                var defaultProfile = {
                    userId: 'guest',
                    displayName: 'Visitor',
                    userType: 'visitor',
                    paypalConnected: false,
                    creatorUsername: ''
                };
                window.unityInstance.SendMessage('FirebaseIntegration', 'OnUserProfileReceived', 
                    JSON.stringify(defaultProfile));
            }
        }
    },

    UnityUpdateUserProfile: function (displayNamePtr, userTypePtr, paypalConnectedPtr) {
        var displayName = UTF8ToString(displayNamePtr);
        var userType = UTF8ToString(userTypePtr);
        var paypalConnected = UTF8ToString(paypalConnectedPtr) === 'true';
        
        console.log('Unity updating user profile:', displayName, userType, paypalConnected);
        
        if (typeof window.UnityUpdateUserProfile === 'function') {
            window.UnityUpdateUserProfile(displayName, userType, paypalConnected);
        } else {
            console.warn('UnityUpdateUserProfile not available in browser context');
        }
    },

    // ========== NETWORKING STATUS FUNCTIONS ==========

    UnityReportNetworkStatus: function (statusPtr, playerCountPtr) {
        var status = UTF8ToString(statusPtr);
        var playerCount = UTF8ToString(playerCountPtr);
        
        console.log('Unity network status update:', status, 'players:', playerCount);
        
        if (typeof window.UnityReportNetworkStatus === 'function') {
            window.UnityReportNetworkStatus(status, playerCount);
        }
    },

    UnityReportPlayerJoined: function (playerNamePtr, userTypePtr) {
        var playerName = UTF8ToString(playerNamePtr);
        var userType = UTF8ToString(userTypePtr);
        
        console.log('Unity player joined:', playerName, 'type:', userType);
        
        if (typeof window.UnityReportPlayerJoined === 'function') {
            window.UnityReportPlayerJoined(playerName, userType);
        }
    },

    UnityReportPlayerLeft: function (playerNamePtr) {
        var playerName = UTF8ToString(playerNamePtr);
        
        console.log('Unity player left:', playerName);
        
        if (typeof window.UnityReportPlayerLeft === 'function') {
            window.UnityReportPlayerLeft(playerName);
        }
    },

    // ========== ANALYTICS AND TELEMETRY ==========

    UnityReportAnalyticsEvent: function (eventNamePtr, eventDataPtr) {
        var eventName = UTF8ToString(eventNamePtr);
        var eventData = UTF8ToString(eventDataPtr);
        
        console.log('Unity analytics event:', eventName, eventData);
        
        if (typeof window.UnityReportAnalyticsEvent === 'function') {
            window.UnityReportAnalyticsEvent(eventName, eventData);
        }
    },

    UnityReportPerformanceMetrics: function (fpsPtr, memoryUsagePtr, networkLatencyPtr) {
        var fps = UTF8ToString(fpsPtr);
        var memoryUsage = UTF8ToString(memoryUsagePtr);
        var networkLatency = UTF8ToString(networkLatencyPtr);
        
        if (typeof window.UnityReportPerformanceMetrics === 'function') {
            window.UnityReportPerformanceMetrics(fps, memoryUsage, networkLatency);
        }
    },

    // ========== BROWSER INTEGRATION FUNCTIONS ==========

    UnityRequestFullscreen: function () {
        console.log('Unity requesting fullscreen');
        
        if (typeof window.UnityRequestFullscreen === 'function') {
            window.UnityRequestFullscreen();
        } else {
            if (document.documentElement.requestFullscreen) {
                document.documentElement.requestFullscreen();
            } else if (document.documentElement.webkitRequestFullscreen) {
                document.documentElement.webkitRequestFullscreen();
            } else if (document.documentElement.msRequestFullscreen) {
                document.documentElement.msRequestFullscreen();
            }
        }
    },

    UnityExitFullscreen: function () {
        console.log('Unity exiting fullscreen');
        
        if (typeof window.UnityExitFullscreen === 'function') {
            window.UnityExitFullscreen();
        } else {
            if (document.exitFullscreen) {
                document.exitFullscreen();
            } else if (document.webkitExitFullscreen) {
                document.webkitExitFullscreen();
            } else if (document.msExitFullscreen) {
                document.msExitFullscreen();
            }
        }
    },

    UnityGetBrowserInfo: function () {
        var browserInfo = {
            userAgent: navigator.userAgent,
            platform: navigator.platform,
            language: navigator.language,
            cookieEnabled: navigator.cookieEnabled,
            onLine: navigator.onLine,
            screenWidth: screen.width,
            screenHeight: screen.height,
            windowWidth: window.innerWidth,
            windowHeight: window.innerHeight,
            pixelRatio: window.devicePixelRatio || 1
        };
        
        console.log('Unity browser info:', browserInfo);
        
        if (typeof window.unityInstance !== 'undefined' && window.unityInstance) {
            window.unityInstance.SendMessage('FirebaseIntegration', 'OnBrowserInfoReceived', 
                JSON.stringify(browserInfo));
        }
    },

    // ========== ERROR HANDLING AND DEBUGGING ==========

    UnityReportError: function (errorMessagePtr, stackTracePtr) {
        var errorMessage = UTF8ToString(errorMessagePtr);
        var stackTrace = UTF8ToString(stackTracePtr);
        
        console.error('Unity error reported:', errorMessage);
        console.error('Stack trace:', stackTrace);
        
        if (typeof window.UnityReportError === 'function') {
            window.UnityReportError(errorMessage, stackTrace);
        }
        
        if (typeof window.UnityReportAnalyticsEvent === 'function') {
            window.UnityReportAnalyticsEvent('unity_error', JSON.stringify({
                message: errorMessage,
                stackTrace: stackTrace,
                timestamp: new Date().toISOString()
            }));
        }
    },

    UnityLog: function (levelPtr, messagePtr) {
        var level = UTF8ToString(levelPtr);
        var message = UTF8ToString(messagePtr);
        
        switch (level.toLowerCase()) {
            case 'error':
                console.error('[Unity]', message);
                break;
            case 'warning':
                console.warn('[Unity]', message);
                break;
            case 'info':
                console.info('[Unity]', message);
                break;
            default:
                console.log('[Unity]', message);
                break;
        }
        
        if (typeof window.UnityLog === 'function') {
            window.UnityLog(level, message);
        }
    },

    // ========== UTILITY FUNCTIONS ==========

    UnityGetTimestamp: function () {
        return Date.now();
    },

    UnityGetRandomGUID: function () {
        var guid = 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
            var r = Math.random() * 16 | 0;
            var v = c == 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
        
        var bufferSize = lengthBytesUTF8(guid) + 1;
        var buffer = _malloc(bufferSize);
        stringToUTF8(guid, buffer, bufferSize);
        return buffer;
    },

    UnitySetLocalStorage: function (keyPtr, valuePtr) {
        var key = UTF8ToString(keyPtr);
        var value = UTF8ToString(valuePtr);
        
        try {
            localStorage.setItem('U3D_' + key, value);
            return 1;
        } catch (e) {
            console.warn('Failed to set localStorage:', e);
            return 0;
        }
    },

    UnityGetLocalStorage: function (keyPtr) {
        var key = UTF8ToString(keyPtr);
        
        try {
            var value = localStorage.getItem('U3D_' + key) || '';
            var bufferSize = lengthBytesUTF8(value) + 1;
            var buffer = _malloc(bufferSize);
            stringToUTF8(value, buffer, bufferSize);
            return buffer;
        } catch (e) {
            console.warn('Failed to get localStorage:', e);
            var bufferSize = 1;
            var buffer = _malloc(bufferSize);
            stringToUTF8('', buffer, bufferSize);
            return buffer;
        }
    },

    UnityRemoveLocalStorage: function (keyPtr) {
        var key = UTF8ToString(keyPtr);
        
        try {
            localStorage.removeItem('U3D_' + key);
            return 1;
        } catch (e) {
            console.warn('Failed to remove localStorage:', e);
            return 0;
        }
    },

    // ========== LEGACY DUAL TRANSACTION FUNCTIONS (FIREBASE DEPENDENT) ==========
    // These are kept for backward compatibility but marked as deprecated

    currentPayPalGameObject: '',

    // DEPRECATED: Firebase Functions-based dual transaction
    UnityStartDualTransactionWithGameObject: function (gameObjectNamePtr, itemNamePtr, itemDescriptionPtr, pricePtr, transactionIdPtr) {
        console.warn('⚠️ UnityStartDualTransactionWithGameObject is DEPRECATED');
        console.warn('⚠️ This method requires Firebase Functions authentication');
        console.warn('⚠️ Use UnityStartDirectPayPalTransaction for direct PayPal integration');
        
        var gameObjectName = UTF8ToString(gameObjectNamePtr);
        var itemName = UTF8ToString(itemNamePtr);
        var itemDescription = UTF8ToString(itemDescriptionPtr);
        var price = UTF8ToString(pricePtr);
        var transactionId = UTF8ToString(transactionIdPtr);
        
        window.currentPayPalGameObject = gameObjectName;
        
        console.log('Unity dual transaction (DEPRECATED Firebase method):', {
            itemName: itemName,
            itemDescription: itemDescription,
            price: price,
            transactionId: transactionId
        });
        
        if (typeof window.UnityStartDualTransaction === 'function') {
            window.UnityStartDualTransaction(itemName, itemDescription, price, transactionId);
        } else {
            console.warn('Firebase-based UnityStartDualTransaction not available');
            if (typeof window.unityInstance !== 'undefined' && window.unityInstance && window.currentPayPalGameObject) {
                window.unityInstance.SendMessage(window.currentPayPalGameObject, 'OnPaymentComplete', 'false');
            }
        }
    },

    // DEPRECATED: Firebase Functions-based authentication check
    UnityCheckAuthenticationStatusWithGameObject: function (gameObjectNamePtr) {
        console.warn('⚠️ UnityCheckAuthenticationStatusWithGameObject is DEPRECATED');
        console.warn('⚠️ Direct PayPal integration does not require authentication');
        
        var gameObjectName = UTF8ToString(gameObjectNamePtr);
        
        console.log('Unity authentication check (DEPRECATED) for GameObject:', gameObjectName);
        
        window.currentPayPalGameObject = gameObjectName;
        
        if (typeof window.UnityCheckAuthenticationStatus === 'function') {
            window.UnityCheckAuthenticationStatus();
        } else {
            console.log('No auth required for direct PayPal - returning success');
            if (typeof window.unityInstance !== 'undefined' && window.unityInstance && window.currentPayPalGameObject) {
                window.unityInstance.SendMessage(window.currentPayPalGameObject, 'OnAuthenticationChecked', 'true');
            }
        }
    },

    // ULTRA-DEPRECATED: Original methods without GameObject name support
    UnityStartDualTransaction: function (itemNamePtr, itemDescriptionPtr, pricePtr, transactionIdPtr) {
        console.warn('⚠️ UnityStartDualTransaction (without GameObject name) is ULTRA-DEPRECATED');
        console.warn('⚠️ Will cause "SendMessage: object not found!" errors');
        console.warn('⚠️ Use UnityStartDirectPayPalTransaction instead');
        
        var itemName = UTF8ToString(itemNamePtr);
        var itemDescription = UTF8ToString(itemDescriptionPtr);
        var price = UTF8ToString(pricePtr);
        var transactionId = UTF8ToString(transactionIdPtr);
        
        console.log('Unity dual transaction (ULTRA-DEPRECATED method):', {
            itemName: itemName,
            itemDescription: itemDescription,
            price: price,
            transactionId: transactionId
        });
        
        if (typeof window.UnityStartDualTransaction === 'function') {
            window.UnityStartDualTransaction(itemName, itemDescription, price, transactionId);
        } else {
            console.warn('Firebase-based UnityStartDualTransaction not available');
            if (typeof window.unityInstance !== 'undefined' && window.unityInstance) {
                window.unityInstance.SendMessage('PayPalDualTransaction', 'OnPaymentComplete', 'false');
            }
        }
    },

    UnityCheckAuthenticationStatus: function () {
        console.warn('⚠️ UnityCheckAuthenticationStatus (without GameObject name) is ULTRA-DEPRECATED');
        console.warn('⚠️ Will cause "SendMessage: object not found!" errors');
        
        console.log('Unity authentication check (ULTRA-DEPRECATED method)');
        
        if (typeof window.UnityCheckAuthenticationStatus === 'function') {
            window.UnityCheckAuthenticationStatus();
        } else {
            console.log('No auth required for direct PayPal - returning success');
            if (typeof window.unityInstance !== 'undefined' && window.unityInstance) {
                window.unityInstance.SendMessage('PayPalDualTransaction', 'OnAuthenticationChecked', 'true');
            }
        }
    }
});