// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services;

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

            if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
            {
                StopForeground(StopForegroundFlags.Remove);
            }
            else
            {
#pragma warning disable CA1422
                StopForeground(true);
#pragma warning restore CA1422
            }
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

        var notification = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("AetherAprs Active")
            .SetContentText("Receiving and transmitting APRS packets")
            .SetSmallIcon(Resource.Drawable.icon_400px)
            .SetContentIntent(pendingIntent)
            .AddAction(Resource.Drawable.icon_400px, "Stop", stopPendingIntent)
            .SetOngoing(true)
            .Build();

        if (notification != null)
        {
            StartForeground(NotificationId, notification);
        }

        return StartCommandResult.Sticky;
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(
                ChannelId,
                "AetherAprs Foreground Service",
                NotificationImportance.Low)
            {
                Description = "Keeps AetherAprs active while receiving and transmitting packets"
            };

            var notificationManager = GetSystemService(NotificationService) as NotificationManager;
            notificationManager?.CreateNotificationChannel(channel);
        }
    }
}

/// <summary>
/// Android implementation of IForegroundService.
/// </summary>
public sealed class AndroidForegroundService : AetherAprs.Services.IForegroundService
{
    private readonly Context _context;
    private bool _isRunning;

    public AndroidForegroundService()
    {
        _context = Application.Context;
    }

    public bool IsRunning => _isRunning;

    public Task StartAsync()
    {
        if (_isRunning)
        {
            return Task.CompletedTask;
        }

        var intent = new Intent(_context, typeof(AetherAprsForegroundService));
        
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            _context.StartForegroundService(intent);
        }
        else
        {
            _context.StartService(intent);
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
        intent.SetAction("STOP_FOREGROUND");
        _context.StartService(intent);

        _isRunning = false;
        return Task.CompletedTask;
    }
}
