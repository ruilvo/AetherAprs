// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Localization;
using AetherAprs.Services.Platform;

using Android.App;
using Android.Content;

using AndroidX.Core.App;

using Microsoft.Extensions.Logging;

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

    private const string ChannelId = NotificationSpecs.MessageChannelId;
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
        Intent contentIntent = new(_context, typeof(MainActivity));
        contentIntent.SetAction(Intent.ActionMain);
        contentIntent.AddCategory(Intent.CategoryLauncher);
        contentIntent.PutExtra(ConversationCallsignExtra, callsign);
        contentIntent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);

        PendingIntent? pendingIntent = PendingIntent.GetActivity(
            _context,
            StableNotificationId(conversationId),
            contentIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        NotificationContentSpec spec = NotificationSpecs.Message(callsign, text);
        NotificationCompat.Builder builder = new(_context, spec.ChannelId);
        builder.SetSmallIcon(AndroidNotificationIcons.Resolve(spec.IconResourceName));
        builder.SetContentTitle(spec.Title);
        builder.SetContentText(spec.Text);
        builder.SetStyle(new NotificationCompat.BigTextStyle().BigText(spec.Text));
        builder.SetContentIntent(pendingIntent);
        builder.SetAutoCancel(spec.AutoCancel);
        builder.SetPriority(NotificationCompat.PriorityHigh);

        Notification? notification = builder.Build();
        if (notification == null)
        {
            return Task.CompletedTask;
        }

        if (_context.GetSystemService(Context.NotificationService) is not NotificationManager notificationManager)
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
        NotificationChannel channel = new(
            ChannelId,
            Strings.Get(ChannelNameKey),
            NotificationImportance.High)
        {
            Description = Strings.Get(ChannelDescriptionKey)
        };

        NotificationManager? notificationManager = _context.GetSystemService(Context.NotificationService) as NotificationManager;
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
            uint hash = 2166136261u;
            foreach (char c in conversationId)
            {
                hash ^= c;
                hash *= 16777619u;
            }

            return (int)hash;
        }
    }
}
