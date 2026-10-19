// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using System;
using System.Threading.Tasks;
using AetherAprs.Localization;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Android foreground service to keep the app active while receiving/transmitting packets.
/// </summary>
[Service(ForegroundServiceType = ForegroundService.TypeDataSync)]
public class AetherAprsForegroundService : Service
{
    private const int NotificationId = 1001;
    private const string ChannelId = "aetheraprs_foreground";
    private const string ActionStart = "START_FOREGROUND";
    private const string ActionStop = "STOP_FOREGROUND";

    public override IBinder? OnBind(Intent? intent)
    {
        return null;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionStop)
        {
            // Broadcast intent to notify the app to stop all ports
            var stopPortsIntent = new Intent("com.aetheraprs.STOP_ALL_PORTS");
            SendBroadcast(stopPortsIntent);

            // Android 13+ only (SupportedOSPlatformVersion = 33, >= API 24/N)
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        CreateNotificationChannel();

        var notificationIntent = new Intent(this, typeof(MainActivity));
        var pendingIntent = PendingIntent.GetActivity(
            this, 
            0, 
            notificationIntent, 
            PendingIntentFlags.Immutable);

        var stopIntent = new Intent(this, typeof(AetherAprsForegroundService));
        stopIntent.SetAction(ActionStop);
        var stopPendingIntent = PendingIntent.GetService(
            this, 
            0, 
            stopIntent, 
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
            StartForeground(NotificationId, notification);
        }

        return StartCommandResult.Sticky;
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
}

/// <summary>
/// Android implementation of IForegroundService.
/// </summary>
public sealed class AndroidForegroundService : AetherAprs.Services.Platform.IForegroundService
{
    private const string ActionStop = "STOP_FOREGROUND";

    private readonly Context _context;
    private readonly ILogger<AndroidForegroundService>? _logger;
    private volatile bool _isRunning;

    public AndroidForegroundService(ILogger<AndroidForegroundService>? logger = null)
    {
        _context = Application.Context;
        _logger = logger;
    }

    public bool IsRunning => _isRunning;

    public Task StartAsync()
    {
        if (_isRunning)
        {
            return Task.CompletedTask;
        }

        var intent = new Intent(_context, typeof(AetherAprsForegroundService));

        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                _context.StartForegroundService(intent);
            }
            else
            {
                _context.StartService(intent);
            }
        }
        catch (Exception ex)
        {
            // API 31+ throws ForegroundServiceStartNotAllowedException when the app
            // is in the background (e.g. BLE reconnect path); do not abort the caller.
            _logger?.LogWarning(ex, "Failed to start foreground service");
            return Task.CompletedTask;
        }

        _isRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!_isRunning)
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
        return Task.CompletedTask;
    }
}
