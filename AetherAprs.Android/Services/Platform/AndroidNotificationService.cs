// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using AetherAprs.Localization;
using AetherAprs.Services.Platform;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Android implementation of <see cref="INotificationService"/>. Posts a message
/// notification on a dedicated channel; tapping it deep-links to the conversation
/// by launching <see cref="MainActivity"/> with the peer callsign as an extra.
/// </summary>
public sealed class AndroidNotificationService : INotificationService
{
    /// <summary>Intent extra carrying the conversation peer callsign.</summary>
    public const string ConversationCallsignExtra = "aetheraprs.conversation_callsign";

    private const string ChannelId = "aetheraprs_messages";
    private const string ChannelNameKey = "MessageNotificationChannelName";
    private const string ChannelDescriptionKey = "MessageNotificationChannelDescription";

    private readonly Context _context;
    private readonly ILogger<AndroidNotificationService>? _logger;

    public AndroidNotificationService(ILogger<AndroidNotificationService>? logger = null)
    {
        _context = Application.Context;
        _logger = logger;
        CreateNotificationChannel();
    }

    public Task ShowMessageNotificationAsync(
        string callsign,
        string text,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Deep-link to the conversation: launch MainActivity (SingleTop|ClearTop so
        // it is reused rather than stacked) with the peer callsign extra.
        var contentIntent = new Intent(_context, typeof(MainActivity));
        contentIntent.SetAction(Intent.ActionMain);
        contentIntent.AddCategory(Intent.CategoryLauncher);
        contentIntent.PutExtra(ConversationCallsignExtra, callsign);
        contentIntent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);

        var pendingIntent = PendingIntent.GetActivity(
            _context,
            StableNotificationId(conversationId),
            contentIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var builder = new NotificationCompat.Builder(_context, ChannelId);
        builder.SetSmallIcon(Resource.Drawable.icon_400px);
        builder.SetContentTitle(callsign);
        builder.SetContentText(text);
        builder.SetStyle(new NotificationCompat.BigTextStyle().BigText(text));
        builder.SetContentIntent(pendingIntent);
        builder.SetAutoCancel(true);
        builder.SetPriority(NotificationCompat.PriorityHigh);

        var notification = builder.Build();
        if (notification == null)
        {
            return Task.CompletedTask;
        }

        var notificationManager = _context.GetSystemService(Context.NotificationService) as NotificationManager;
        if (notificationManager == null)
        {
            _logger?.LogWarning("NotificationManager unavailable; message notification for {Callsign} dropped", callsign);
            return Task.CompletedTask;
        }

        // POST_NOTIFICATIONS is requested at startup (MainActivity); if the user
        // denied it, this call is a harmless no-op on Android 13+.
        notificationManager.Notify(StableNotificationId(conversationId), notification);
        AndroidLifecycleTelemetry.State.NotificationPosted();
        return Task.CompletedTask;
    }

    private void CreateNotificationChannel()
    {
        var channel = new NotificationChannel(
            ChannelId,
            Strings.Get(ChannelNameKey),
            NotificationImportance.High)
        {
            Description = Strings.Get(ChannelDescriptionKey)
        };

        var notificationManager = _context.GetSystemService(Context.NotificationService) as NotificationManager;
        notificationManager?.CreateNotificationChannel(channel);
    }

    /// <summary>
    /// Deterministic per-conversation notification id (FNV-1a of the conversation
    /// id) so notifications for the same conversation replace each other and the
    /// content intent request code matches.
    /// </summary>
    private static int StableNotificationId(string conversationId)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in conversationId)
            {
                hash ^= c;
                hash *= 16777619u;
            }

            return (int)hash;
        }
    }
}
