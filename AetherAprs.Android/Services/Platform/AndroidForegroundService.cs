// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Localization;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Microsoft.Extensions.Logging;
using System;
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
[Service(ForegroundServiceType = ForegroundService.TypeConnectedDevice | ForegroundService.TypeLocation)]
public class AetherAprsForegroundService : Service
{
    private const int NotificationId = 1001;
    private const int TimeoutNotificationId = 1002;
    private const string ChannelId = "aetheraprs_foreground";
    private const string AlertChannelId = "aetheraprs_alerts";
    private const string ActionStart = "START_FOREGROUND";
    private const string ActionStop = "STOP_FOREGROUND";

    /// <summary>
    /// Action of the notification "Stop" button. Handled by the service itself so
    /// it works even when no activity (and therefore no runtime receiver) exists.
    /// </summary>
    private const string ActionStopAll = "STOP_ALL_PORTS";
    internal const string StateAction = "com.aetheraprs.FOREGROUND_SERVICE_STATE";
    internal const string StateExtra = "state";
    internal const string StateStarted = "started";
    internal const string StateStopped = "stopped";
    internal const string StateFailed = "failed";

    /// <summary>Intent extra carrying the <see cref="KeepAliveServiceTypes"/> bitmask.</summary>
    internal const string RequiredTypesExtra = "requiredTypes";
    internal const int DefaultRequiredTypes = (int)KeepAliveServiceTypes.ConnectedDevice;
    private const string WakeLockTag = "AetherAprs:ForegroundService";

    private PowerManager.WakeLock? _wakeLock;

    public override IBinder? OnBind(Intent? intent)
    {
        return null;
    }

    public override void OnCreate()
    {
        base.OnCreate();
        AndroidLifecycleTelemetry.State.ForegroundStartRequested();

        // Hold a partial wake lock so the CPU stays awake while the foreground
        // service is running (e.g. GPS polling, packet processing). Released in
        // OnDestroy.
        var powerManager = GetSystemService(PowerService) as PowerManager;
        _wakeLock = powerManager?.NewWakeLock(WakeLockFlags.Partial, WakeLockTag);
        _wakeLock?.Acquire();
    }

    public override void OnDestroy()
    {
        if (_wakeLock?.IsHeld == true)
        {
            _wakeLock.Release();
        }

        _wakeLock = null;
        AndroidLifecycleTelemetry.State.ForegroundStopped();
        PublishState(StateStopped);
        base.OnDestroy();
    }

    /// <summary>
    /// Degrades gracefully when a time-limited foreground service type reaches its
    /// platform limit. Android 15+ requires an override here; without one the app
    /// is killed. The service stops the ports, releases the keep-alive reasons and
    /// posts a user-visible explanation instead of crashing.
    /// </summary>
    public override void OnTimeout(int startId, ForegroundService fgsType)
    {
        global::Android.Util.Log.Warn(
            "AetherAprs",
            $"Foreground service type {fgsType} reached its platform time limit; stopping cleanly.");

        ShowTimeoutNotification();

        // Give up foreground status immediately to satisfy the platform grace
        // period, then finish the orderly teardown in the still-running process.
        StopForeground(StopForegroundFlags.Remove);

        _ = StopAllFromNotificationAsync();
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var action = intent?.Action;

        if (action == ActionStop)
        {
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
            PublishState(StateStopped);
            return StartCommandResult.NotSticky;
        }

        if (action == ActionStopAll)
        {
            // Explicit delivery from the notification's "Stop" action. This runs
            // entirely inside the service, so it works when the UI is gone.
            _ = StopAllFromNotificationAsync();
            return StartCommandResult.NotSticky;
        }

        CreateNotificationChannel();

        var notificationIntent = new Intent(this, typeof(MainActivity));
        var pendingIntent = PendingIntent.GetActivity(
            this, 
            0, 
            notificationIntent, 
            PendingIntentFlags.Immutable);

        // Explicit service delivery: no runtime activity receiver is required.
        var stopAllIntent = new Intent(this, typeof(AetherAprsForegroundService));
        stopAllIntent.SetAction(ActionStopAll);
        var stopPendingIntent = PendingIntent.GetService(
            this, 
            1, 
            stopAllIntent, 
            PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(this, ChannelId);
        builder.SetContentTitle(Strings.Get("ForegroundServiceNotificationTitle"));
        builder.SetContentText(Strings.Get("ForegroundServiceNotificationText"));
        builder.SetSmallIcon(Resource.Drawable.icon_400px);
        builder.SetContentIntent(pendingIntent);
        builder.AddAction(Resource.Drawable.icon_400px, Strings.Get("Stop"), stopPendingIntent);
        builder.SetOngoing(true);
        var notification = builder.Build();

        if (notification != null)
        {
            var requiredTypes = ToAndroidServiceType(GetRequiredTypes(intent));

            try
            {
                StartForeground(NotificationId, notification, requiredTypes);
            }
            catch (Java.Lang.SecurityException ex)
            {
                global::Android.Util.Log.Warn("AetherAprs", $"Foreground service unavailable: {ex.Message}");
                AndroidLifecycleTelemetry.State.ForegroundRejected();
                PublishState(StateFailed);
                StopSelf();
                return StartCommandResult.NotSticky;
            }
            catch (ForegroundServiceStartNotAllowedException ex)
            {
                global::Android.Util.Log.Warn("AetherAprs", $"Foreground service unavailable: {ex.Message}");
                AndroidLifecycleTelemetry.State.ForegroundRejected();
                PublishState(StateFailed);
                StopSelf();
                return StartCommandResult.NotSticky;
            }

            AndroidLifecycleTelemetry.State.ForegroundStarted();
            PublishState(StateStarted);
        }

        return StartCommandResult.Sticky;
    }

    /// <summary>
    /// Stops every port and releases all keep-alive reasons, then stops the
    /// service. Shared by the notification action and the timeout handler; every
    /// step is best-effort and the service is always stopped afterwards.
    /// </summary>
    private async Task StopAllFromNotificationAsync()
    {
        // Release location first so the shared foreground service stays held by
        // the ports while they shut down; both releases are idempotent.
        await ReleaseKeepAliveAsync(KeepAliveReason.LocationTracking).ConfigureAwait(false);

        try
        {
            var portService = AetherAprs.App.GetService<IPortService>();
            await portService.StopAllPortsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error(
                "AetherAprs",
                $"Failed to stop ports from the foreground service notification: {ex}");
        }
        finally
        {
            await ReleaseKeepAliveAsync(KeepAliveReason.Ports).ConfigureAwait(false);
            PublishState(StateStopped);
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
        }
    }

    private static async Task ReleaseKeepAliveAsync(KeepAliveReason reason)
    {
        try
        {
            var keepAlive = AetherAprs.App.GetService<IKeepAliveService>();
            await keepAlive.ReleaseAsync(reason).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn(
                "AetherAprs",
                $"Failed to release keep-alive reason {reason}: {ex.Message}");
        }
    }

    private static KeepAliveServiceTypes GetRequiredTypes(Intent? intent)
    {
        var rawTypes = intent?.GetIntExtra(RequiredTypesExtra, DefaultRequiredTypes)
            ?? DefaultRequiredTypes;

        return (KeepAliveServiceTypes)rawTypes;
    }

    private static ForegroundService ToAndroidServiceType(KeepAliveServiceTypes requiredTypes)
    {
        var androidTypes = ForegroundService.TypeNone;

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

        var notificationIntent = new Intent(this, typeof(MainActivity));
        var pendingIntent = PendingIntent.GetActivity(
            this,
            0,
            notificationIntent,
            PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(this, AlertChannelId);
        builder.SetContentTitle(Strings.Get("ForegroundServiceTimeoutTitle"));
        builder.SetContentText(Strings.Get("ForegroundServiceTimeoutText"));
        builder.SetSmallIcon(Resource.Drawable.icon_400px);
        builder.SetContentIntent(pendingIntent);
        builder.SetAutoCancel(true);

        var notification = builder.Build();
        if (notification == null)
        {
            return;
        }

        // The AndroidX compatibility helper is annotated nullable in the .NET
        // bindings; use the platform manager the channel setup already uses.
        var notificationManager = GetSystemService(NotificationService) as NotificationManager;
        notificationManager?.Notify(TimeoutNotificationId, notification);
    }

    private void PublishState(string state)
    {
        var intent = new Intent(StateAction);
        intent.SetPackage(PackageName);
        intent.PutExtra(StateExtra, state);
        SendBroadcast(intent);
    }

    private void CreateNotificationChannel()
    {
        // Android 13+ only (SupportedOSPlatformVersion = 33, >= API 26/O)
        var channel = new NotificationChannel(
            ChannelId,
            Strings.Get("ForegroundServiceNotificationChannelName"),
            NotificationImportance.Low)
        {
            Description = Strings.Get("ForegroundServiceNotificationChannelDescription")
        };

        var notificationManager = GetSystemService(NotificationService) as NotificationManager;
        notificationManager?.CreateNotificationChannel(channel);
    }

    private void CreateAlertChannel()
    {
        // Android 13+ only (SupportedOSPlatformVersion = 33, >= API 26/O)
        var channel = new NotificationChannel(
            AlertChannelId,
            Strings.Get("ForegroundServiceTimeoutChannelName"),
            NotificationImportance.Default)
        {
            Description = Strings.Get("ForegroundServiceTimeoutChannelDescription")
        };

        var notificationManager = GetSystemService(NotificationService) as NotificationManager;
        notificationManager?.CreateNotificationChannel(channel);
    }
}

/// <summary>
/// Android implementation of IForegroundService.
/// </summary>
public sealed class AndroidForegroundService : AetherAprs.Services.Platform.IForegroundService, IDisposable
{
    private const string ActionStop = "STOP_FOREGROUND";

    private readonly Context _context;
    private readonly ILogger<AndroidForegroundService>? _logger;
    private readonly ForegroundServiceStateReceiver _stateReceiver;
    private volatile bool _isRunning;
    private volatile bool _startRequested;

    public AndroidForegroundService(ILogger<AndroidForegroundService>? logger = null)
    {
        _context = Application.Context;
        _logger = logger;
        _stateReceiver = new ForegroundServiceStateReceiver(this);
        var filter = new IntentFilter(AetherAprsForegroundService.StateAction);
        _context.RegisterReceiver(_stateReceiver, filter, ReceiverFlags.NotExported);
    }

    public bool IsRunning => _isRunning;

    public Task StartAsync(KeepAliveServiceTypes requiredTypes)
    {
        var types = requiredTypes == KeepAliveServiceTypes.None
            ? KeepAliveServiceTypes.ConnectedDevice
            : requiredTypes;

        var intent = new Intent(_context, typeof(AetherAprsForegroundService));
        intent.PutExtra(AetherAprsForegroundService.RequiredTypesExtra, (int)types);

        try
        {
            if (_isRunning)
            {
                // Already foreground: refresh the declared service types (for
                // example when location tracking starts after the ports did).
                _context.StartService(intent);
            }
            else
            {
                AndroidLifecycleTelemetry.State.ForegroundStartRequested();
                _startRequested = true;
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
            // is in the background (e.g. BLE reconnect path); do not abort the caller.
            _logger?.LogWarning(ex, "Failed to start foreground service");
            AndroidLifecycleTelemetry.State.ForegroundRejected();
            _startRequested = false;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!_isRunning && !_startRequested)
        {
            return Task.CompletedTask;
        }

        var intent = new Intent(_context, typeof(AetherAprsForegroundService));
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
        }
        else if (state == AetherAprsForegroundService.StateStarted)
        {
            AndroidLifecycleTelemetry.State.ForegroundStarted();
        }
        else if (state == AetherAprsForegroundService.StateStopped)
        {
            AndroidLifecycleTelemetry.State.ForegroundStopped();
        }
    }

    private sealed class ForegroundServiceStateReceiver : BroadcastReceiver
    {
        private readonly AndroidForegroundService _owner;

        public ForegroundServiceStateReceiver(AndroidForegroundService owner)
        {
            _owner = owner;
        }

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
