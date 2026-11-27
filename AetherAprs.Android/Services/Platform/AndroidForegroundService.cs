// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Localization;
using AetherAprs.Services.Lifecycle;
using AetherAprs.Services.Platform;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

// Android's Service base class exposes a ForegroundServiceType property that
// shadows the core enum name inside derived classes; use an alias for the contract type.
using KeepAliveServiceTypes = AetherAprs.Services.Platform.ForegroundServiceType;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Android foreground service to keep the app active while receiving/transmitting packets
/// and while location tracking is enabled.
/// </summary>
/// <remarks>
/// The service declares <c>connectedDevice</c> (a long-lived radio/socket link) and
/// <c>location</c> (while tracking). It deliberately does not declare
/// <c>dataSync</c>: on Android 15+ that type is capped at six hours per 24 hours,
/// which would silently stop continuous operation.
/// </remarks>
[Service(
    ForegroundServiceType = ForegroundService.TypeConnectedDevice | ForegroundService.TypeLocation,
    // Started only by this application; declaring the export keeps the generated
    // manifest honest instead of relying on what the generator defaults to.
    Exported = false)]
public class AetherAprsForegroundService : Service
{
    private const int NotificationId = 1001;
    private const int TimeoutNotificationId = 1002;
    private const string ChannelId = NotificationSpecs.ForegroundChannelId;
    private const string AlertChannelId = NotificationSpecs.AlertChannelId;
    private const string ActionStart = "START_FOREGROUND";
    private const string ActionStop = "STOP_FOREGROUND";

    // Android creates the service itself, so it cannot take an injected logger;
    // ApplicationLog resolves the container's category once Avalonia is up.
    private static readonly ILogger Log = ApplicationLog.For<AetherAprsForegroundService>();

    /// <summary>
    /// Action of the notification "Stop" button. Handled by the service itself so
    /// it works even when no activity (and therefore no runtime receiver) exists.
    /// </summary>
    private const string ActionStopAll = NotificationSpecs.StopAllActionId;
    internal const string StateAction = "com.aetheraprs.FOREGROUND_SERVICE_STATE";
    internal const string StateExtra = "state";
    internal const string StateStarted = "started";
    internal const string StateStopped = "stopped";
    internal const string StateFailed = "failed";

    /// <summary>Intent extra carrying the <see cref="KeepAliveServiceTypes"/> bitmask.</summary>
    internal const string RequiredTypesExtra = "requiredTypes";
    internal const int DefaultRequiredTypes = (int)KeepAliveServiceTypes.ConnectedDevice;

    // The only types this service advertises in the foreground notification. Bits
    // outside this set are dropped rather than passed through, so an intent from a
    // stale or foreign caller cannot widen the service type.
    private const KeepAliveServiceTypes SupportedTypes =
        KeepAliveServiceTypes.ConnectedDevice | KeepAliveServiceTypes.Location;
    private const string WakeLockTag = "AetherAprs:ForegroundService";

    private PowerManager.WakeLock? _wakeLock;
    private bool _batteryOptimizationReported;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        AndroidLifecycleTelemetry.State.ForegroundStartRequested();

        // Create the lock here but do not hold it yet: the lease is armed by the
        // start command that brings work in, and it is bounded (see WakeLeasePolicy),
        // so a service nobody asks to work cannot keep the CPU awake.
        PowerManager? powerManager = GetSystemService(PowerService) as PowerManager;
        _wakeLock = powerManager?.NewWakeLock(WakeLockFlags.Partial, WakeLockTag);

        RecheckBatteryOptimization();
    }

    public override void OnDestroy()
    {
        ApplyWakeLease(isSessionActive: false);

        _wakeLock = null;
        AndroidLifecycleTelemetry.State.ForegroundStopped();
        PublishState(StateStopped);
        base.OnDestroy();
    }

    /// <summary>
    /// Arms or releases the bounded partial wake lock for the current session.
    /// </summary>
    /// <remarks>
    /// The platform counts acquisitions, so the lease is only armed when the lock is
    /// not already held and only released when it is; re-arming a held lock would
    /// leave a reference that the matching single release cannot clear.
    /// </remarks>
    private void ApplyWakeLease(bool isSessionActive)
    {
        if (_wakeLock is not { } wakeLock)
        {
            return;
        }

        switch (WakeLeasePolicy.Decide(isSessionActive, wakeLock.IsHeld))
        {
            case WakeLeaseDecision.Arm:
                wakeLock.Acquire(WakeLeasePolicy.LeaseMilliseconds);
                break;

            case WakeLeaseDecision.Release:
                wakeLock.Release();
                break;
        }
    }

    /// <summary>
    /// Releases everything a rejected foreground start was holding.
    /// </summary>
    /// <remarks>
    /// The lease was armed before the platform call and the declared types belong to
    /// a foreground state that never began, so waiting for <c>OnDestroy</c> would keep
    /// both for as long as the process lingers. Nothing owns them after a rejection.
    /// </remarks>
    private void AbandonRejectedStart()
    {
        ApplyWakeLease(isSessionActive: false);
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
    }

    /// <summary>
    /// Re-reads the battery-optimization exemption when the service starts work.
    /// </summary>
    /// <remarks>
    /// A foreground service is not exempt from Doze: without the exemption the
    /// platform may defer network and location work and eventually stop the
    /// session. The state can change while the application runs (a system update,
    /// the operator toggling it), so it is rechecked on each start instead of being
    /// remembered, and reported once per service instance to keep the log bounded.
    /// </remarks>
    private void RecheckBatteryOptimization()
    {
        if (_batteryOptimizationReported)
        {
            return;
        }

        if (GetSystemService(PowerService) is not PowerManager powerManager)
        {
            return;
        }

        if (powerManager.IsIgnoringBatteryOptimizations(PackageName))
        {
            _batteryOptimizationReported = true;
            return;
        }

        _batteryOptimizationReported = true;
        Log.LogWarning(
            "Battery optimization is enabled for this application; the system may defer or stop background work.");
    }

    /// <summary>
    /// Degrades gracefully when a time-limited foreground service type reaches its
    /// platform limit. Android 15+ requires an override here; without one the app
    /// is killed. The service stops the ports, releases the keep-alive reasons and
    /// posts a user-visible explanation instead of crashing.
    /// </summary>
    public override void OnTimeout(int startId, ForegroundService fgsType)
    {
        Log.LogWarning(
            "Foreground service type {ServiceType} reached its platform time limit; stopping cleanly.",
            fgsType);

        ShowTimeoutNotification();

        // Give up foreground status immediately to satisfy the platform grace
        // period, then finish the orderly teardown in the still-running process.
        StopForeground(StopForegroundFlags.Remove);

        // The platform limit ends the background session, not the application:
        // stop the session through the shared stop path but keep the UI usable.
        _ = StopApplicationAsync(closeApplication: false);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        string? action = intent?.Action;

        if (action == ActionStop)
        {
            ApplyWakeLease(isSessionActive: false);
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
            PublishState(StateStopped);
            return StartCommandResult.NotSticky;
        }

        if (action == ActionStopAll)
        {
            // Explicit delivery from the notification's stop action. This runs
            // entirely inside the service, so it works when the UI is gone, and it
            // closes the application instead of leaving a stopped session behind.
            _ = StopApplicationAsync(closeApplication: true);
            return StartCommandResult.NotSticky;
        }

        // Work is being requested, so take a bounded lease for it and recheck the
        // premise the background session depends on.
        ApplyWakeLease(isSessionActive: true);
        RecheckBatteryOptimization();

        CreateNotificationChannel();

        Intent notificationIntent = new(this, typeof(MainActivity));
        PendingIntent? pendingIntent = PendingIntent.GetActivity(
            this,
            0,
            notificationIntent,
            PendingIntentFlags.Immutable);

        NotificationContentSpec spec = NotificationSpecs.Foreground(
            Strings.Get("ForegroundServiceNotificationTitle"),
            Strings.Get("ForegroundServiceNotificationText"),
            Strings.Get("ForegroundServiceCloseAction"));

        // Explicit service delivery: no runtime activity receiver is required.
        Intent stopAllIntent = new(this, typeof(AetherAprsForegroundService));
        stopAllIntent.SetAction(ActionStopAll);
        PendingIntent? stopPendingIntent = PendingIntent.GetService(
            this,
            1,
            stopAllIntent,
            PendingIntentFlags.Immutable);

        int iconResource = AndroidNotificationIcons.Resolve(spec.IconResourceName);
        NotificationCompat.Builder builder = new(this, spec.ChannelId);
        builder.SetContentTitle(spec.Title);
        builder.SetContentText(spec.Text);
        builder.SetSmallIcon(iconResource);
        builder.SetContentIntent(pendingIntent);
        foreach (NotificationActionSpec notificationAction in spec.Actions)
        {
            builder.AddAction(iconResource, notificationAction.Label, stopPendingIntent);
        }

        builder.SetOngoing(true);
        Notification? notification = builder.Build();

        if (notification != null)
        {
            ForegroundService requiredTypes = ToAndroidServiceType(GetRequiredTypes(intent));

            try
            {
                StartForeground(NotificationId, notification, requiredTypes);
            }
            catch (Java.Lang.SecurityException ex)
            {
                Log.LogWarning(ex, "Foreground service unavailable");
                AndroidLifecycleTelemetry.State.ForegroundRejected();
                PublishState(StateFailed);
                AbandonRejectedStart();
                return StartCommandResult.NotSticky;
            }
            catch (ForegroundServiceStartNotAllowedException ex)
            {
                Log.LogWarning(ex, "Foreground service unavailable");
                AndroidLifecycleTelemetry.State.ForegroundRejected();
                PublishState(StateFailed);
                AbandonRejectedStart();
                return StartCommandResult.NotSticky;
            }

            AndroidLifecycleTelemetry.State.ForegroundStarted();
            PublishState(StateStarted);
        }

        return StartCommandResult.Sticky;
    }

    /// <summary>
    /// Stops the application session through the shared stop path, then gives up
    /// foreground status. Shared by the notification action and the timeout
    /// handler; every step is best-effort and the service is always stopped
    /// afterwards.
    /// </summary>
    /// <param name="closeApplication">
    /// Whether the operator asked to close the application. The notification
    /// action does; a platform timeout only ends the background session.
    /// </param>
    private async Task StopApplicationAsync(bool closeApplication)
    {
        IApplicationSessionControl? sessionControl = ResolveSessionControl();
        if (sessionControl is null)
        {
            // The container is not available, so there is nothing to disarm. Give
            // up foreground status anyway; the platform can then reclaim the
            // process instead of leaving a stale notification.
            Log.LogWarning(
                "Application services are unavailable; stopping the foreground service only.");
        }
        else
        {
            try
            {
                ApplicationSessionStopResult result = await sessionControl.StopAsync().ConfigureAwait(false);
                foreach (ApplicationSessionStopFailure failure in result.Failures)
                {
                    Log.LogWarning(
                        "Application stop step {Step} failed: {Message}",
                        failure.Step,
                        failure.Message);
                }
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Failed to stop the application session from the foreground service");
            }
        }

        PublishState(StateStopped);
        ApplyWakeLease(isSessionActive: false);
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();

        if (closeApplication)
        {
            CloseActivities();
        }
    }

    private static IApplicationSessionControl? ResolveSessionControl() =>
        Avalonia.Application.Current is AetherAprs.App app
            ? app.ServiceProvider.GetService(typeof(IApplicationSessionControl)) as IApplicationSessionControl
            : null;

    /// <summary>
    /// Finishes every activity so a notification-triggered stop really closes the
    /// application instead of leaving an empty window behind. Posted to the main
    /// looper because the stop completes on a worker thread. The process itself is
    /// reclaimed by the platform once no component is left.
    /// </summary>
    private static void CloseActivities()
    {
        MainActivity? activity = MainActivity.Instance;
        if (activity is null)
        {
            return;
        }

        Looper? mainLooper = Looper.MainLooper;
        if (mainLooper is null)
        {
            return;
        }

        Handler handler = new(mainLooper);
        handler.Post(() =>
        {
            try
            {
                activity.FinishAffinity();
            }
            catch (Exception ex)
            {
                Log.LogWarning(ex, "Failed to close the activity after an application stop");
            }
        });
    }

    private static KeepAliveServiceTypes GetRequiredTypes(Intent? intent)
    {
        int rawTypes = intent?.GetIntExtra(RequiredTypesExtra, DefaultRequiredTypes)
            ?? DefaultRequiredTypes;

        return (KeepAliveServiceTypes)rawTypes & SupportedTypes;
    }

    private static ForegroundService ToAndroidServiceType(KeepAliveServiceTypes requiredTypes)
    {
        ForegroundService androidTypes = ForegroundService.TypeNone;

        if ((requiredTypes & KeepAliveServiceTypes.ConnectedDevice) != 0)
        {
            androidTypes |= ForegroundService.TypeConnectedDevice;
        }

        if ((requiredTypes & KeepAliveServiceTypes.Location) != 0)
        {
            androidTypes |= ForegroundService.TypeLocation;
        }

        return androidTypes == ForegroundService.TypeNone
            ? ForegroundService.TypeConnectedDevice
            : androidTypes;
    }

    private void ShowTimeoutNotification()
    {
        CreateNotificationChannel();
        CreateAlertChannel();

        Intent notificationIntent = new(this, typeof(MainActivity));
        PendingIntent? pendingIntent = PendingIntent.GetActivity(
            this,
            0,
            notificationIntent,
            PendingIntentFlags.Immutable);

        NotificationContentSpec spec = NotificationSpecs.ForegroundTimeout(
            Strings.Get("ForegroundServiceTimeoutTitle"),
            Strings.Get("ForegroundServiceTimeoutText"));

        NotificationCompat.Builder builder = new(this, spec.ChannelId);
        builder.SetContentTitle(spec.Title);
        builder.SetContentText(spec.Text);
        builder.SetSmallIcon(AndroidNotificationIcons.Resolve(spec.IconResourceName));
        builder.SetContentIntent(pendingIntent);
        builder.SetAutoCancel(spec.AutoCancel);

        Notification? notification = builder.Build();
        if (notification == null)
        {
            return;
        }

        // The AndroidX compatibility helper is annotated nullable in the .NET
        // bindings; use the platform manager the channel setup already uses.
        NotificationManager? notificationManager = GetSystemService(NotificationService) as NotificationManager;
        notificationManager?.Notify(TimeoutNotificationId, notification);
    }

    private void PublishState(string state)
    {
        Intent intent = new(StateAction);
        intent.SetPackage(PackageName);
        intent.PutExtra(StateExtra, state);
        SendBroadcast(intent);
    }

    private void CreateNotificationChannel()
    {
        // Android 13+ only (SupportedOSPlatformVersion = 33, >= API 26/O)
        NotificationChannel channel = new(
            ChannelId,
            Strings.Get("ForegroundServiceNotificationChannelName"),
            NotificationImportance.Low)
        {
            Description = Strings.Get("ForegroundServiceNotificationChannelDescription")
        };

        NotificationManager? notificationManager = GetSystemService(NotificationService) as NotificationManager;
        notificationManager?.CreateNotificationChannel(channel);
    }

    private void CreateAlertChannel()
    {
        // Android 13+ only (SupportedOSPlatformVersion = 33, >= API 26/O)
        NotificationChannel channel = new(
            AlertChannelId,
            Strings.Get("ForegroundServiceTimeoutChannelName"),
            NotificationImportance.Default)
        {
            Description = Strings.Get("ForegroundServiceTimeoutChannelDescription")
        };

        NotificationManager? notificationManager = GetSystemService(NotificationService) as NotificationManager;
        notificationManager?.CreateNotificationChannel(channel);
    }
}

/// <summary>
/// Android implementation of IForegroundService.
/// </summary>
public sealed class AndroidForegroundService : AetherAprs.Services.Platform.IForegroundService, IDisposable
{
    private const string ActionStop = "STOP_FOREGROUND";
    private const int StartConfirmationTimeoutSeconds = 5;

    private readonly Context _context;
    private readonly ILogger<AndroidForegroundService>? _logger;
    private readonly ForegroundServiceStateReceiver _stateReceiver;
    private readonly Lock _pendingStartLock = new();
    private volatile bool _isRunning;
    private volatile bool _startRequested;
    private TaskCompletionSource<ForegroundServiceStartResult>? _pendingStart;

    public AndroidForegroundService(ILogger<AndroidForegroundService>? logger = null)
    {
        _context = Application.Context;
        _logger = logger;
        _stateReceiver = new ForegroundServiceStateReceiver(this);
        IntentFilter filter = new(AetherAprsForegroundService.StateAction);
        _context.RegisterReceiver(_stateReceiver, filter, ReceiverFlags.NotExported);
    }

    public bool IsRunning => _isRunning;

    public async Task<ForegroundServiceStartResult> StartAsync(KeepAliveServiceTypes requiredTypes)
    {
        KeepAliveServiceTypes types = requiredTypes == KeepAliveServiceTypes.None
            ? KeepAliveServiceTypes.ConnectedDevice
            : requiredTypes;

        bool wasRunning = _isRunning;

        Intent intent = new(_context, typeof(AetherAprsForegroundService));
        intent.PutExtra(AetherAprsForegroundService.RequiredTypesExtra, (int)types);

        TaskCompletionSource<ForegroundServiceStartResult> confirmation;
        lock (_pendingStartLock)
        {
            // Concurrent requests share one answer: the service reports a single
            // state for the types it currently declares.
            confirmation = _pendingStart ??=
                new TaskCompletionSource<ForegroundServiceStartResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            if (!wasRunning)
            {
                _startRequested = true;
            }
        }

        try
        {
            if (wasRunning)
            {
                // Already foreground: refresh the declared service types (for
                // example when location tracking starts after the ports did).
                // The service republishes its state, so the refresh is confirmed
                // the same way a first start is.
                _context.StartService(intent);
            }
            else
            {
                AndroidLifecycleTelemetry.State.ForegroundStartRequested();
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    _context.StartForegroundService(intent);
                }
                else
                {
                    _context.StartService(intent);
                }
            }
        }
        catch (Exception ex)
        {
            // API 31+ throws ForegroundServiceStartNotAllowedException when the app
            // is in the background (e.g. BLE reconnect path). The caller degrades
            // instead of losing the keep-alive reason.
            _logger?.LogWarning(ex, "Failed to start foreground service");
            AndroidLifecycleTelemetry.State.ForegroundRejected();
            lock (_pendingStartLock)
            {
                _startRequested = false;
            }

            ForegroundServiceStartResult rejected = ForegroundServiceStartResult.Rejected(
                $"The platform refused the foreground service request: {ex.Message}");
            CompletePendingStart(rejected);
            return rejected;
        }

        try
        {
            return await confirmation.Task
                .WaitAsync(TimeSpan.FromSeconds(StartConfirmationTimeoutSeconds))
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            ForegroundServiceStartResult rejected = ForegroundServiceStartResult.Rejected(
                $"The foreground service did not confirm startup within {StartConfirmationTimeoutSeconds} seconds.");
            _logger?.LogWarning("{Reason}", rejected.FailureReason);
            AndroidLifecycleTelemetry.State.ForegroundRejected();
            CompletePendingStart(rejected);
            return rejected;
        }
    }

    /// <summary>
    /// Resolves the request that is waiting for the service state broadcast, so
    /// startup is never assumed from the fact that the intent was delivered.
    /// </summary>
    private void CompletePendingStart(ForegroundServiceStartResult result)
    {
        TaskCompletionSource<ForegroundServiceStartResult>? pending;
        lock (_pendingStartLock)
        {
            pending = _pendingStart;
            _pendingStart = null;
        }

        pending?.TrySetResult(result);
    }

    public Task StopAsync()
    {
        if (!_isRunning && !_startRequested)
        {
            return Task.CompletedTask;
        }

        Intent intent = new(_context, typeof(AetherAprsForegroundService));
        intent.SetAction(ActionStop);

        try
        {
            _context.StartService(intent);
        }
        catch (Exception ex)
        {
            // The OS may have already stopped the service (e.g. app in background
            // or service killed) while _isRunning was still true; nothing left to stop.
            _logger?.LogWarning(ex, "Failed to deliver stop intent to foreground service");
        }

        _isRunning = false;
        _startRequested = false;
        AndroidLifecycleTelemetry.State.ForegroundStopped();
        return Task.CompletedTask;
    }

    private void UpdateState(string? state)
    {
        _isRunning = state == AetherAprsForegroundService.StateStarted;
        _startRequested = state is not AetherAprsForegroundService.StateStarted
            and not AetherAprsForegroundService.StateFailed;

        if (state == AetherAprsForegroundService.StateFailed)
        {
            AndroidLifecycleTelemetry.State.ForegroundRejected();
            _logger?.LogWarning("Foreground service failed before notification publication");
            CompletePendingStart(ForegroundServiceStartResult.Rejected(
                "The platform rejected the foreground service notification."));
        }
        else if (state == AetherAprsForegroundService.StateStarted)
        {
            AndroidLifecycleTelemetry.State.ForegroundStarted();
            CompletePendingStart(ForegroundServiceStartResult.Started());
        }
        else if (state == AetherAprsForegroundService.StateStopped)
        {
            AndroidLifecycleTelemetry.State.ForegroundStopped();
        }
    }

    private sealed class ForegroundServiceStateReceiver(AndroidForegroundService owner) : BroadcastReceiver
    {
        private readonly AndroidForegroundService _owner = owner;

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == AetherAprsForegroundService.StateAction)
            {
                _owner.UpdateState(intent.GetStringExtra(AetherAprsForegroundService.StateExtra));
            }
        }
    }

    public void Dispose()
    {
        _context.UnregisterReceiver(_stateReceiver);
        _stateReceiver.Dispose();
    }
}
