// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Services.Lifecycle;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.Platform;
using AetherAprs.Services.UI;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Models.Aprs;
using AetherAprs.Android.Services.Platform;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using DialogHostAvalonia;
using Android.Window;
using AndroidX.Core.View;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Android;

// Layout-only configuration changes are absorbed without re-creating the
// activity: the Avalonia view forwards them to its platform (insets, screens)
// and re-lays out from the new metrics. Locale and font-scale changes stay
// unhandled on purpose so the activity is re-created and the UI culture plus
// resources are re-applied from the new configuration.
[Activity(
    Label = "AetherAprs",
    Theme = "@style/AetherAprsTheme.Splash",
    Icon = "@drawable/icon_400px",
    MainLauncher = true,
    // Declared instead of inferred: the launcher filter makes this activity
    // reachable from other applications, so the export has to be a reviewed
    // decision and every extra it reads is untrusted input.
    Exported = true,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.ScreenLayout
        | ConfigChanges.UiMode
        | ConfigChanges.LayoutDirection
        | ConfigChanges.KeyboardHidden
        | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity
{
    // The initial permission burst is intentionally process-scoped: Android
    // re-creates the activity for configuration changes that are not listed
    // above, and running the burst again would orphan the first instance's
    // pending waiter. A fresh process starts unset and asks the user again.
    private static readonly ProcessLifetimeFlag _initialPermissionPromptRequested = new();

    // Android constructs the activity, so it cannot take an injected logger;
    // ApplicationLog resolves the container's category once Avalonia is up.
    private static readonly ILogger _logger = ApplicationLog.For<MainActivity>();

    private INavigationService? navigationService;
    private BackInvokedCallback? backInvokedCallback;
    private bool _backCallbackRegistered;
    private bool _destroyed;
    private bool _initialPermissionBurstStarted;
    private AprsAddress? pendingConversationPeer;

    // The queue owns the waiters, so a prompt answered after this activity was
    // re-created or destroyed cannot settle a request the new activity started.
    private ActivityPromptHost? _promptHost;
    private PermissionRequestQueue? _permissionRequests;

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
    public async Task<PermissionPromptResult> RequestNotificationPermissionAsync()
    {
        if (_destroyed)
        {
            return PermissionPromptResult.Abandoned;
        }

        // Android 13+ only (SupportedOSPlatformVersion = 33)
        var currentStatus = CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications);
        _logger.LogDebug("Notification permission status: {Status}", currentStatus);

        if (currentStatus == Permission.Granted)
        {
            _logger.LogDebug("Notification permission already granted");
            return PermissionPromptResult.Granted;
        }

        // Check if we should show rationale
        if (ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.PostNotifications))
        {
            _logger.LogDebug("Notification permission rationale is suggested");
        }

        _logger.LogDebug("Requesting the notification permission");
        return await PromptForAsync(PermissionKind.Notifications);
    }

    /// <summary>
    /// Requests Bluetooth permissions and waits for user response.
    /// </summary>
    public async Task<PermissionPromptResult> RequestBluetoothPermissionAsync()
    {
        if (_destroyed)
        {
            return PermissionPromptResult.Abandoned;
        }

        // Android 13+ only (SupportedOSPlatformVersion = 33, includes S/12 permissions)
        var bluetoothScanStatus = CheckSelfPermission(global::Android.Manifest.Permission.BluetoothScan);
        var bluetoothConnectStatus = CheckSelfPermission(global::Android.Manifest.Permission.BluetoothConnect);
        _logger.LogDebug(
            "Bluetooth permission status: {ScanStatus}, {ConnectStatus}",
            bluetoothScanStatus,
            bluetoothConnectStatus);

        if (bluetoothScanStatus == Permission.Granted && bluetoothConnectStatus == Permission.Granted)
        {
            _logger.LogDebug("Bluetooth permissions already granted");
            return PermissionPromptResult.Granted;
        }

        // Check if we should show rationale
        if (ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.BluetoothScan) ||
            ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.BluetoothConnect))
        {
            _logger.LogDebug("Bluetooth permission rationale is suggested");
        }

        _logger.LogDebug("Requesting the Bluetooth permissions");
        return await PromptForAsync(PermissionKind.Bluetooth);
    }

    /// <summary>
    /// Requests microphone (audio recording) permission and waits for user response.
    /// </summary>
    public async Task<PermissionPromptResult> RequestAudioPermissionAsync()
    {
        if (_destroyed)
        {
            return PermissionPromptResult.Abandoned;
        }

        var currentStatus = CheckSelfPermission(global::Android.Manifest.Permission.RecordAudio);
        _logger.LogDebug("Audio permission status: {Status}", currentStatus);

        if (currentStatus == Permission.Granted)
        {
            _logger.LogDebug("Audio permission already granted");
            return PermissionPromptResult.Granted;
        }

        _logger.LogDebug("Requesting the audio permission");
        return await PromptForAsync(PermissionKind.Audio);
    }

    private async Task<PermissionPromptResult> PromptForAsync(PermissionKind kind)
    {
        var queue = _permissionRequests;
        if (queue is null)
        {
            return PermissionPromptResult.Abandoned;
        }

        // A dialog the operator walks away from must not hold the startup burst
        // forever; an abandoned request is not a denial.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var result = await queue.RequestAsync(kind, timeout.Token);
            _logger.LogDebug("The {Permission} prompt finished with {Outcome}", kind, result);
            return result;
        }
        catch (System.OperationCanceledException)
        {
            _logger.LogDebug("Waiting for the {Permission} prompt timed out", kind);
            return PermissionPromptResult.Abandoned;
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);
        base.OnCreate(savedInstanceState);
        AndroidLifecycleTelemetry.State.ProcessStarted();
        AndroidLifecycleTelemetry.State.ActivityCreated();
        ConfigureSystemBars();

        Localization.UiCulture.Apply(new AetherAprs.Android.Services.Platform.AndroidUiCultureProvider().GetUiCulture());

        // Store instance for permission requests
        Instance = this;

        // One queue per activity: the request ids it hands out are the request
        // codes Android echoes back, so a result can only settle the prompt that
        // produced it, and tearing the activity down abandons the waiters.
        _promptHost = new ActivityPromptHost(this);
        _permissionRequests = new PermissionRequestQueue(
            _promptHost,
            ApplicationLog.For<PermissionRequestQueue>());

        // Setup the modern back handling for Android 13+
        backInvokedCallback = new BackInvokedCallback(HandleBackPressed);
    }

    protected override void OnResume()
    {
        base.OnResume();
        AndroidLifecycleTelemetry.State.ActivityResumed();
        if (Avalonia.Application.Current is { } themeApplication)
        {
            themeApplication.ActualThemeVariantChanged += OnActualThemeVariantChanged;
        }
        ConfigureSystemBars();

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

        // Request permissions on the first resume of this process only. The flag
        // is process-scoped (see the field comment) so an activity re-created for
        // a configuration change does not re-run the burst.
        if (_initialPermissionPromptRequested.TrySet())
        {
            // Marked before starting: the burst can complete synchronously when
            // every permission is already granted.
            _initialPermissionBurstStarted = true;
            _ = RequestInitialPermissionsAsync();
        }

        if (navigationService != null)
        {
            navigationService.CurrentViewModelChanged += OnCurrentViewModelChanged;
        }
        UpdateBackCallback();

        // Process a notification deep-link (cold start or resume with intent).
        ProcessPendingConversation();
    }

    private void ConfigureSystemBars()
    {
        var window = Window;
        if (window is null)
        {
            return;
        }
        var isLightTheme = (Avalonia.Application.Current?.ActualThemeVariant
            ?? Avalonia.Styling.ThemeVariant.Light) == Avalonia.Styling.ThemeVariant.Light;
        var controller = WindowCompat.GetInsetsController(window, window.DecorView);
        if (controller != null)
        {
            controller.AppearanceLightStatusBars = isLightTheme;
            controller.AppearanceLightNavigationBars = isLightTheme;
        }
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        ConfigureSystemBars();
    }

    private void OnCurrentViewModelChanged(object? sender, AetherAprs.ViewModels.ViewModelBase? viewModel)
    {
        UpdateBackCallback();
    }

    private void UpdateBackCallback()
    {
        if (_destroyed || backInvokedCallback is null || OnBackInvokedDispatcher is null)
        {
            return;
        }

        var shouldRegister = navigationService?.CanGoBack == true;
        if (shouldRegister && !_backCallbackRegistered)
        {
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(0, backInvokedCallback);
            _backCallbackRegistered = true;
        }
        else if (!shouldRegister && _backCallbackRegistered)
        {
            OnBackInvokedDispatcher.UnregisterOnBackInvokedCallback(backInvokedCallback);
            _backCallbackRegistered = false;
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);

        // Tapping a message notification reuses the activity (SingleTop|ClearTop)
        // and delivers the new intent here rather than relaunching.
        if (intent != null)
        {
            Intent = intent;
            pendingConversationPeer = ReadConversationPeer(intent);
        }
    }

    /// <summary>
    /// Reads the conversation peer from an intent this activity received.
    /// </summary>
    /// <remarks>
    /// This activity is exported, so the intent may come from any application. The
    /// extra is only honoured on the notification's launcher intent and only when it
    /// is a callsign; a missing, oversized or malformed value selects no
    /// conversation and the caller stays where it was.
    /// </remarks>
    private static AprsAddress? ReadConversationPeer(Intent? intent)
    {
        if (intent is null
            || intent.Action != Intent.ActionMain
            || !intent.HasCategory(Intent.CategoryLauncher))
        {
            return null;
        }

        var extra = intent.GetStringExtra(AndroidNotificationService.ConversationCallsignExtra);
        if (extra is null)
        {
            return null;
        }

        if (!ConversationDeepLink.TryParsePeer(extra, out var peer))
        {
            _logger.LogDebug("Ignored a conversation intent whose callsign extra was not a callsign");
            return null;
        }

        return peer;
    }

    private void ProcessPendingConversation()
    {
        // Prefer the extra from the current intent, then any OnNewIntent value.
        var peer = ReadConversationPeer(Intent) ?? pendingConversationPeer;
        pendingConversationPeer = null;

        // Clear the extra so a subsequent plain resume does not re-navigate.
        Intent?.RemoveExtra(AndroidNotificationService.ConversationCallsignExtra);

        if (peer is not { } target)
        {
            return;
        }

        var app = (App?)Avalonia.Application.Current;
        if (app == null)
        {
            return;
        }

        var factory = app.ServiceProvider.GetService<IConversationViewModelFactory>();
        var conversation = factory?.Create(target);
        if (conversation != null)
        {
            navigationService?.NavigateTo(conversation);
        }
    }

    private async Task RequestInitialPermissionsAsync()
    {
        try
        {
            _logger.LogDebug("Requesting the initial permission burst");

            var notificationResult = await RequestNotificationPermissionAsync();
            var bluetoothResult = await RequestBluetoothPermissionAsync();
            var locationGranted = await RequestLocationPermissionsAsync();
            var audioResult = await RequestAudioPermissionAsync();

            // Request background location (non-fatal when denied; Android 11+
            // routes this to the app settings page)
            var backgroundLocationGranted = await RequestBackgroundLocationPermissionAsync();

            // One bounded summary instead of a line per step: the individual
            // methods already report their own status at debug level.
            _logger.LogDebug(
                "Initial permission burst finished: notifications {Notifications}, Bluetooth {Bluetooth}, location {Location}, audio {Audio}, background location {BackgroundLocation}",
                notificationResult,
                bluetoothResult,
                locationGranted,
                audioResult,
                backgroundLocationGranted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Requesting the initial permissions failed");
        }
        finally
        {
            // Signal completion on every path, including a burst aborted by the
            // activity being destroyed, so the waiting core service does not sit
            // until its own timeout.
            Services.Platform.AndroidPermissionService.SignalPermissionsComplete();
        }
    }

    /// <summary>
    /// Requests location permissions and waits for user response.
    /// </summary>
    public async Task<bool> RequestLocationPermissionsAsync()
    {
        if (_destroyed)
        {
            return false;
        }

        var fineLocationStatus = CheckSelfPermission(global::Android.Manifest.Permission.AccessFineLocation);
        var coarseLocationStatus = CheckSelfPermission(global::Android.Manifest.Permission.AccessCoarseLocation);
        _logger.LogDebug(
            "Location permission status: {FineStatus}, {CoarseStatus}",
            fineLocationStatus,
            coarseLocationStatus);

        if (fineLocationStatus == Permission.Granted || coarseLocationStatus == Permission.Granted)
        {
            _logger.LogDebug("Location permissions already granted");
            return true;
        }

        // Check if we should show rationale
        if (ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.AccessFineLocation) ||
            ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.AccessCoarseLocation))
        {
            _logger.LogDebug("Location permission rationale is suggested");
        }

        // Request permissions and wait for result
        _logger.LogDebug("Requesting the location permissions");
        
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
            _logger.LogDebug("Location permission result: {Granted}", result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Requesting the location permissions failed");
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
        if (_destroyed)
        {
            return false;
        }

        const int backgroundLocationRequestCode = 1002;

        // Android 10 (Q) introduced ACCESS_BACKGROUND_LOCATION; SupportedOSPlatformVersion is 33.
        if (CheckSelfPermission(global::Android.Manifest.Permission.AccessBackgroundLocation) == Permission.Granted)
        {
            _logger.LogDebug("Background location permission already granted");
            return true;
        }

        // The dialog is only meaningful once fine location is granted; the system
        // otherwise sends the user to app settings where the toggle is hidden.
        var fineLocationStatus = CheckSelfPermission(global::Android.Manifest.Permission.AccessFineLocation);
        if (fineLocationStatus != Permission.Granted)
        {
            _logger.LogDebug("Fine location not granted; skipping the background location request");
            return false;
        }

        _logger.LogDebug("Requesting the background location permission");

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
            _logger.LogDebug("Background location permission result: {Granted}", result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Requesting the background location permission failed");
            OnPermissionResult -= OnPermissionResultHandler;
            return false;
        }
    }

    protected override void OnPause()
    {
        AndroidLifecycleTelemetry.State.ActivityPaused();
        if (Avalonia.Application.Current is { } app)
        {
            app.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        }
        if (navigationService != null)
        {
            navigationService.CurrentViewModelChanged -= OnCurrentViewModelChanged;
        }
        // Unregister (but keep) the modern back callback while paused; OnResume
        // registers it again through UpdateBackCallback.
        UnregisterBackCallback();

        base.OnPause();
    }

    private void HandleBackPressed()
    {
        if (_destroyed)
        {
            return;
        }

        if (DialogHost.GetDialogSession("MainDialogHost") != null)
        {
            DialogHost.Close("MainDialogHost", false);
            return;
        }

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
        // Exit is the same operator action as the notification's stop: stop the
        // background session and then close the activity. Without the shared stop,
        // an exit would leave the foreground service running behind a closed window.
        _ = CloseApplicationAsync();
    }

    private async Task CloseApplicationAsync()
    {
        try
        {
            var app = (App?)Avalonia.Application.Current;
            if (app?.ServiceProvider.GetService(typeof(IApplicationSessionControl))
                is IApplicationSessionControl sessionControl)
            {
                var result = await sessionControl.StopAsync();
                foreach (var failure in result.Failures)
                {
                    _logger.LogWarning(
                        "Application stop step {Step} failed: {Message}",
                        failure.Step,
                        failure.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop the application session on exit");
        }
        finally
        {
            if (!_destroyed)
            {
                FinishAffinity();
            }
        }
    }

    public override void OnBackPressed()
    {
        HandleBackPressed();
    }

    protected override void OnDestroy()
    {
        AndroidLifecycleTelemetry.State.ActivityDestroyed();
        _destroyed = true;

        // OnDestroy can run without OnPause (for example when the activity never
        // resumed), so every subscription and JNI peer is released here as well.
        // Every step below is safe to run after the matching OnPause work.
        if (Avalonia.Application.Current is { } app)
        {
            app.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        }
        if (navigationService != null)
        {
            navigationService.CurrentViewModelChanged -= OnCurrentViewModelChanged;
            navigationService.RequestAppExit -= OnRequestAppExit;
            navigationService = null;
        }

        DisposeBackCallback();
        CompletePendingPermissionRequests();

        if (_initialPermissionBurstStarted)
        {
            // The burst cannot continue on this destroyed instance and the process
            // flag prevents a re-created activity from running it again, so tell
            // the waiting core service now instead of letting it time out.
            Services.Platform.AndroidPermissionService.SignalPermissionsComplete();
        }

        // Clear instance reference
        if (Instance == this)
        {
            Instance = null;
        }

        base.OnDestroy();
    }

    private void UnregisterBackCallback()
    {
        if (!_backCallbackRegistered)
        {
            return;
        }

        // Clear the flag first so a throwing platform call cannot leave the
        // lifecycle believing the callback is still registered.
        _backCallbackRegistered = false;
        var callback = backInvokedCallback;
        if (callback == null)
        {
            return;
        }

        try
        {
            OnBackInvokedDispatcher?.UnregisterOnBackInvokedCallback(callback);
        }
        catch (Exception ex)
        {
            // The dispatcher may already be tearing down; disposal below must
            // still release the JNI peer.
            _logger.LogDebug(ex, "Failed to unregister the back callback");
        }
    }

    private void DisposeBackCallback()
    {
        try
        {
            UnregisterBackCallback();
        }
        finally
        {
            // BackInvokedCallback wraps a Java peer; leaking it leaks one JNI
            // global reference per activity re-creation.
            backInvokedCallback?.Dispose();
            backInvokedCallback = null;
        }
    }

    private void CompletePendingPermissionRequests()
    {
        // A destroyed activity never receives OnRequestPermissionsResult, so the
        // waiter is abandoned now rather than left to its timeout. Abandoned is
        // not a denial: nothing is recorded about the operator's choice.
        _permissionRequests?.Dispose();
        _permissionRequests = null;
        _promptHost = null;
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        // The request code is the queue's request id, so the kind the prompt was
        // shown for comes from the host rather than from a fixed code per kind.
        if (_promptHost is { } host && host.TryTakeKind(requestCode, out var kind))
        {
            var granted = grantResults.Length > 0 &&
                (kind == PermissionKind.Bluetooth
                    ? grantResults.All(result => result == Permission.Granted)
                    : grantResults[0] == Permission.Granted);

            AndroidLifecycleTelemetry.State.PermissionResult(ToAndroidPermission(kind), granted);
            _permissionRequests?.Complete(requestCode, granted);
        }

        // Notify any listeners about permission results
        OnPermissionResult?.Invoke(requestCode, permissions, grantResults);

        if (requestCode == 1001)
        {
            AndroidLifecycleTelemetry.State.PermissionResult(
                AetherAprs.Services.Platform.AndroidPermission.Location,
                grantResults.Any(result => result == Permission.Granted));
        }
        else if (requestCode == 1002)
        {
            AndroidLifecycleTelemetry.State.PermissionResult(
                AetherAprs.Services.Platform.AndroidPermission.BackgroundLocation,
                grantResults.Length > 0 && grantResults[0] == Permission.Granted);
        }
    }

    private static AetherAprs.Services.Platform.AndroidPermission ToAndroidPermission(PermissionKind kind) => kind switch
    {
        PermissionKind.Notifications => AetherAprs.Services.Platform.AndroidPermission.Notifications,
        PermissionKind.Bluetooth => AetherAprs.Services.Platform.AndroidPermission.Bluetooth,
        PermissionKind.Audio => AetherAprs.Services.Platform.AndroidPermission.Audio,
        PermissionKind.Location => AetherAprs.Services.Platform.AndroidPermission.Location,
        _ => AetherAprs.Services.Platform.AndroidPermission.BackgroundLocation,
    };

    private sealed class ActivityPromptHost(MainActivity activity) : IPermissionPromptHost
    {
        private readonly Dictionary<int, PermissionKind> _kinds = [];

        public void Show(PermissionKind kind, int requestId)
        {
            var permissions = kind switch
            {
                PermissionKind.Notifications => new[] { global::Android.Manifest.Permission.PostNotifications },
                PermissionKind.Bluetooth => new[]
                {
                    global::Android.Manifest.Permission.BluetoothScan,
                    global::Android.Manifest.Permission.BluetoothConnect,
                },
                PermissionKind.Audio => new[] { global::Android.Manifest.Permission.RecordAudio },
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This permission has no prompt."),
            };

            _kinds[requestId] = kind;
            activity.RequestPermissions(permissions, requestId);
        }

        public bool TryTakeKind(int requestId, out PermissionKind kind) => _kinds.Remove(requestId, out kind);
    }

    private sealed class BackInvokedCallback(Action onBackInvoked) : Java.Lang.Object, IOnBackInvokedCallback
    {
        public void OnBackInvoked()
        {
            onBackInvoked?.Invoke();
        }
    }
}
