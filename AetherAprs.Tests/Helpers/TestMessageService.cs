// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

using AetherAprs.Factories.Packets;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Services.UI;

using Microsoft.Extensions.Logging;

namespace AetherAprs.Tests.Helpers;

/// <summary>
/// Builds a <see cref="MessageService"/> for tests. When the test does not care
/// about durable outbound state, an in-memory outbox stands in for the SQLite
/// store with the same contract.
/// </summary>
internal static class TestMessageService
{
    public static MessageService Create(
        IPortService portService,
        IPacketStorageService packetStorageService,
        IConfigurationService configurationService,
        IAprsPortSettingsResolver portSettingsResolver,
        IPacketFactory packetFactory,
        ILogger<MessageService> logger,
        IUiDispatcher uiDispatcher,
        IMessageCommands? messageCommands = null,
        IMessageQueries? messageQueries = null,
        IPacketQueryService? packetQueryService = null,
        INotificationService? notificationService = null,
        TimeProvider? timeProvider = null,
        IActiveConversationTracker? activeConversationTracker = null)
    {
        FakeMessageOutbox shared = messageCommands as FakeMessageOutbox
            ?? messageQueries as FakeMessageOutbox
            ?? new FakeMessageOutbox(timeProvider);

        return new MessageService(
            portService,
            packetStorageService,
            configurationService,
            portSettingsResolver,
            packetFactory,
            logger,
            uiDispatcher,
            messageCommands ?? shared,
            messageQueries ?? shared,
            packetQueryService,
            notificationService,
            timeProvider,
            activeConversationTracker);
    }
}
