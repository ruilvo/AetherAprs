// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Services.Platform;
using AetherAprs.Services.UI;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Window;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Android;

[Activity(
    Label = "AetherAprs",
    Theme = "@style/AetherAprsTheme.NoActionBar",
    Icon = "@drawable/icon_400px",
    MainLauncher = true,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    private INavigationService? navigationService;
    private BackInvokedCallback? backInvokedCallback;
    private StopPortsBroadcastReceiver? stopPortsReceiver;
    private bool permissionsRequested = false;

    private const int NotificationPermissionRequestCode = 1000;
    private const int BluetoothPermissionRequestCode = 1100;
    private TaskCompletionSource<bool>? _notificationPermissionTcs;
    private TaskCompletionSource<bool>? _bluetoothPermissionTcs;

    /// <summary>
    /// Gets the current MainActivity instance for permission requests.
    /// </summary>
    public static MainActivity? Instance { get; private set; }

    /// <summary>
    /// Event raised when permission request results are available.
    /// </summary>
    public static event Action<int, string[], Permission[]>? OnPermissionResult;



    /// <summary>
    /// Requests notification permission and waits for user response.
    /// </summary>
    public async Task<bool> RequestNotificationPermissionAsync()
    {
        // Android 13+ only (SupportedOSPlatformVersion = 33)
        var currentStatus = CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications);
        global::Android.Util.Log.Info("AetherAprs", $"Notification permission current status: {currentStatus}");

        if (currentStatus == Permission.Granted)
        {
            global::Android.Util.Log.Info("AetherAprs", "Notification permission already granted");
            return true;
        }

        // Check if we should show rationale
        if (ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.PostNotifications))
        {
            global::Android.Util.Log.Info("AetherAprs", "Should show notification permission rationale");
        }

        // Request permission and wait for result
        global::Android.Util.Log.Info("AetherAprs", "Requesting notification permission dialog...");
        _notificationPermissionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notificationTcs = _notificationPermissionTcs;
        using var notificationTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var notificationRegistration = notificationTimeoutCts.Token.Register(
            () => notificationTcs.TrySetCanceled(notificationTimeoutCts.Token));
        
        try
        {
            RequestPermissions(new[] { global::Android.Manifest.Permission.PostNotifications }, NotificationPermissionRequestCode);
            var result = await _notificationPermissionTcs.Task.ConfigureAwait(false);
            global::Android.Util.Log.Info("AetherAprs", $"Notification permission dialog result: {result}");
            return result;
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("AetherAprs", $"Error requesting notification permission: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Requests Bluetooth permissions and waits for user response.
    /// </summary>
    public async Task<bool> RequestBluetoothPermissionAsync()
    {
        // Android 13+ only (SupportedOSPlatformVersion = 33, includes S/12 permissions)
        var bluetoothScanStatus = CheckSelfPermission(global::Android.Manifest.Permission.BluetoothScan);
        var bluetoothConnectStatus = CheckSelfPermission(global::Android.Manifest.Permission.BluetoothConnect);
        global::Android.Util.Log.Info("AetherAprs", $"Bluetooth permission current status - SCAN: {bluetoothScanStatus}, CONNECT: {bluetoothConnectStatus}");

        if (bluetoothScanStatus == Permission.Granted && bluetoothConnectStatus == Permission.Granted)
        {
            global::Android.Util.Log.Info("AetherAprs", "Bluetooth permissions already granted");
            return true;
        }

        // Check if we should show rationale
        if (ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.BluetoothScan) ||
            ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.BluetoothConnect))
        {
            global::Android.Util.Log.Info("AetherAprs", "Should show Bluetooth permission rationale");
        }

        // Request permissions and wait for result
        global::Android.Util.Log.Info("AetherAprs", "Requesting Bluetooth permission dialog...");
        _bluetoothPermissionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bluetoothTcs = _bluetoothPermissionTcs;
        using var bluetoothTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var bluetoothRegistration = bluetoothTimeoutCts.Token.Register(
            () => bluetoothTcs.TrySetCanceled(bluetoothTimeoutCts.Token));
        
        try
        {
            RequestPermissions(new[] 
            { 
                global::Android.Manifest.Permission.BluetoothScan,
                global::Android.Manifest.Permission.BluetoothConnect
            }, BluetoothPermissionRequestCode);
            var result = await _bluetoothPermissionTcs.Task.ConfigureAwait(false);
            global::Android.Util.Log.Info("AetherAprs", $"Bluetooth permission dialog result: {result}");
            return result;
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("AetherAprs", $"Error requesting Bluetooth permission: {ex.Message}");
            return false;
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        Localization.UiCulture.Apply(new AetherAprs.Android.Services.Platform.AndroidUiCultureProvider().GetUiCulture());

        // Store instance for permission requests
        Instance = this;

        // Setup the modern back handling for Android 13+
        backInvokedCallback = new BackInvokedCallback(HandleBackPressed);

        // Register broadcast receiver for stopping ports (Android 13+, SupportedOSPlatformVersion = 33)
        stopPortsReceiver = new StopPortsBroadcastReceiver();
        var filter = new IntentFilter("com.aetheraprs.STOP_ALL_PORTS");
        RegisterReceiver(stopPortsReceiver, filter, ReceiverFlags.NotExported);
    }

    protected override void OnResume()
    {
        base.OnResume();

        // Get navigation service and subscribe to exit requests
        if (navigationService == null)
        {
            var app = (App?)Avalonia.Application.Current;
            if (app != null)
            {
                navigationService = app.ServiceProvider.GetService<INavigationService>();
                navigationService?.RequestAppExit += OnRequestAppExit;
            }
        }

        // Request permissions on first resume only
        if (!permissionsRequested)
        {
            permissionsRequested = true;
            _ = RequestInitialPermissionsAsync();
        }

        // Register callback for modern back handling on Android 13+
        if (backInvokedCallback != null)
        {
            OnBackInvokedDispatcher?.RegisterOnBackInvokedCallback(0, backInvokedCallback);
        }
    }

    private async Task RequestInitialPermissionsAsync()
    {
        try
        {
            // Request notification permission
            global::Android.Util.Log.Info("AetherAprs", "Requesting notification permission from MainActivity...");
            var notificationGranted = await RequestNotificationPermissionAsync();
            global::Android.Util.Log.Info("AetherAprs", $"Notification permission result: {notificationGranted}");

            // Request Bluetooth permissions
            global::Android.Util.Log.Info("AetherAprs", "Requesting Bluetooth permissions from MainActivity...");
            var bluetoothGranted = await RequestBluetoothPermissionAsync();
            global::Android.Util.Log.Info("AetherAprs", $"Bluetooth permission result: {bluetoothGranted}");

            // Request location permissions
            global::Android.Util.Log.Info("AetherAprs", "Requesting location permissions from MainActivity...");
            var locationGranted = await RequestLocationPermissionsAsync();
            global::Android.Util.Log.Info("AetherAprs", $"Location permission result: {locationGranted}");

            // Request background location (non-fatal when denied; Android 11+
            // routes this to the app settings page)
            var backgroundLocationGranted = await RequestBackgroundLocationPermissionAsync();
            global::Android.Util.Log.Info("AetherAprs", $"Background location permission result: {backgroundLocationGranted}");

            // Signal that initial permissions are complete
            Services.Platform.AndroidPermissionService.SignalPermissionsComplete();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("AetherAprs", $"Error requesting permissions: {ex.Message}");
            // Signal completion even on error
            Services.Platform.AndroidPermissionService.SignalPermissionsComplete();
        }
    }

    /// <summary>
    /// Requests location permissions and waits for user response.
    /// </summary>
    public async Task<bool> RequestLocationPermissionsAsync()
    {
        var fineLocationStatus = CheckSelfPermission(global::Android.Manifest.Permission.AccessFineLocation);
        var coarseLocationStatus = CheckSelfPermission(global::Android.Manifest.Permission.AccessCoarseLocation);
        global::Android.Util.Log.Info("AetherAprs", $"Location permission current status - FINE: {fineLocationStatus}, COARSE: {coarseLocationStatus}");

        if (fineLocationStatus == Permission.Granted || coarseLocationStatus == Permission.Granted)
        {
            global::Android.Util.Log.Info("AetherAprs", "Location permissions already granted");
            return true;
        }

        // Check if we should show rationale
        if (ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.AccessFineLocation) ||
            ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.AccessCoarseLocation))
        {
            global::Android.Util.Log.Info("AetherAprs", "Should show location permission rationale");
        }

        // Request permissions and wait for result
        global::Android.Util.Log.Info("AetherAprs", "Requesting location permission dialog...");
        
        // Use the LocationService's existing request code to avoid conflicts
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var locationTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var locationRegistration = locationTimeoutCts.Token.Register(() =>
        {
            OnPermissionResult -= OnPermissionResultHandler;
            tcs.TrySetCanceled(locationTimeoutCts.Token);
        });
        void OnPermissionResultHandler(int requestCode, string[] permissions, Permission[] grantResults)
        {
            if (requestCode == 1001) // LocationPermissionRequestCode from LocationService
            {
                OnPermissionResult -= OnPermissionResultHandler;
                var granted = grantResults.Any(r => r == Permission.Granted);
                tcs.TrySetResult(granted);
            }
        }

        try
        {
            OnPermissionResult += OnPermissionResultHandler;
            RequestPermissions(new[] 
            { 
                global::Android.Manifest.Permission.AccessFineLocation,
                global::Android.Manifest.Permission.AccessCoarseLocation
            }, 1001); // Use LocationService's request code
            var result = await tcs.Task.ConfigureAwait(false);
            global::Android.Util.Log.Info("AetherAprs", $"Location permission dialog result: {result}");
            return result;
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("AetherAprs", $"Error requesting location permission: {ex.Message}");
            OnPermissionResult -= OnPermissionResultHandler;
            return false;
        }
    }

    /// <summary>
    /// Requests background location permission and waits for the user response.
    /// On Android 11+ the system routes this to the app settings page ("Allow all
    /// the time"); denial is non-fatal — foreground tracking keeps working.
    /// </summary>
    public async Task<bool> RequestBackgroundLocationPermissionAsync()
    {
        const int backgroundLocationRequestCode = 1002;

        // Android 10 (Q) introduced ACCESS_BACKGROUND_LOCATION; SupportedOSPlatformVersion is 33.
        if (CheckSelfPermission(global::Android.Manifest.Permission.AccessBackgroundLocation) == Permission.Granted)
        {
            global::Android.Util.Log.Info("AetherAprs", "Background location permission already granted");
            return true;
        }

        // The dialog is only meaningful once fine location is granted; the system
        // otherwise sends the user to app settings where the toggle is hidden.
        var fineLocationStatus = CheckSelfPermission(global::Android.Manifest.Permission.AccessFineLocation);
        if (fineLocationStatus != Permission.Granted)
        {
            global::Android.Util.Log.Info("AetherAprs", "Fine location not granted; skipping background location request");
            return false;
        }

        global::Android.Util.Log.Info("AetherAprs", "Requesting background location permission...");

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var backgroundTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var backgroundRegistration = backgroundTimeoutCts.Token.Register(() =>
        {
            OnPermissionResult -= OnPermissionResultHandler;
            tcs.TrySetCanceled(backgroundTimeoutCts.Token);
        });
        void OnPermissionResultHandler(int requestCode, string[] permissions, Permission[] grantResults)
        {
            if (requestCode == backgroundLocationRequestCode)
            {
                OnPermissionResult -= OnPermissionResultHandler;
                var granted = grantResults.Length > 0 && grantResults[0] == Permission.Granted;
                tcs.TrySetResult(granted);
            }
        }

        try
        {
            OnPermissionResult += OnPermissionResultHandler;
            RequestPermissions(new[] { global::Android.Manifest.Permission.AccessBackgroundLocation }, backgroundLocationRequestCode);
            var result = await tcs.Task.ConfigureAwait(false);
            global::Android.Util.Log.Info("AetherAprs", $"Background location permission result: {result}");
            return result;
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("AetherAprs", $"Error requesting background location permission: {ex.Message}");
            OnPermissionResult -= OnPermissionResultHandler;
            return false;
        }
    }

    protected override void OnPause()
    {
        // Unregister the modern back callback for Android 13+
        if (backInvokedCallback != null)
        {
            OnBackInvokedDispatcher?.UnregisterOnBackInvokedCallback(backInvokedCallback);
        }

        base.OnPause();
    }

    private void HandleBackPressed()
    {
        if (navigationService != null)
        {
            navigationService.GoBack();
        }
        else
        {
            Finish();
        }
    }

    private void OnRequestAppExit(object? sender, EventArgs e)
    {
        // Close the activity when navigation service requests exit
        Finish();
    }

    public override void OnBackPressed()
    {
        HandleBackPressed();
    }

    protected override void OnDestroy()
    {
        navigationService?.RequestAppExit -= OnRequestAppExit;

        // Unregister broadcast receiver
        if (stopPortsReceiver != null)
        {
            UnregisterReceiver(stopPortsReceiver);
            stopPortsReceiver = null;
        }

        // Clear instance reference
        if (Instance == this)
        {
            Instance = null;
        }

        base.OnDestroy();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        // Handle notification permission result
        if (requestCode == NotificationPermissionRequestCode && _notificationPermissionTcs != null)
        {
            var granted = grantResults.Length > 0 && grantResults[0] == Permission.Granted;
            _notificationPermissionTcs.TrySetResult(granted);
            _notificationPermissionTcs = null;
        }

        // Handle Bluetooth permission result
        if (requestCode == BluetoothPermissionRequestCode && _bluetoothPermissionTcs != null)
        {
            var granted = grantResults.Length > 0 && grantResults.All(r => r == Permission.Granted);
            _bluetoothPermissionTcs.TrySetResult(granted);
            _bluetoothPermissionTcs = null;
        }

        // Notify any listeners about permission results
        OnPermissionResult?.Invoke(requestCode, permissions, grantResults);
    }

    private sealed class BackInvokedCallback(Action onBackInvoked) : Java.Lang.Object, IOnBackInvokedCallback
    {
        public void OnBackInvoked()
        {
            onBackInvoked?.Invoke();
        }
    }

    private sealed class StopPortsBroadcastReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == "com.aetheraprs.STOP_ALL_PORTS")
            {
                // Get the app and stop all ports
                var app = (App?)Avalonia.Application.Current;
                if (app != null)
                {
                    var portService = app.ServiceProvider.GetService<AetherAprs.Services.Ports.IPortService>();
                    var logger = app.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILogger<MainActivity>>();
                    
                    if (portService != null)
                    {
                        // Stop all ports asynchronously
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await portService.StopAllPortsAsync();
                            }
                            catch (Exception ex)
                            {
                                logger?.LogError(ex, "Error stopping ports from notification action");
                            }
                        });
                    }
                }
            }
        }
    }
}
