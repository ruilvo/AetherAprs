// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using AetherAprs.Services;
using AetherAprs.ViewModels.Pages;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class ConversationViewModelTests
{
    [Fact]
    public void Initialize_ReflectsExistingMessages()
    {
        var peer = new Callsign("K0PEER", 1);
        var messageService = new FakeMessageService();
        var thread = messageService.GetOrCreateConversation(peer);
        thread.Messages.Add(new StoredMessage
        {
            Peer = peer,
            Text = "Hello",
            Timestamp = DateTimeOffset.UtcNow,
            IsOutbound = false
        });

        var vm = new ConversationViewModel(messageService, Substitute.For<INavigationService>());
        vm.Initialize(peer);

        Assert.Equal("K0PEER-1", vm.Title);
        Assert.False(vm.IsDestinationEditable);
        Assert.Single(vm.Messages);
        Assert.Equal("Hello", vm.Messages[0].Text);
    }

    [Fact]
    public void Initialize_SubscribesToInboundMessagesWhileOpen()
    {
        var peer = new Callsign("K0PEER", 1);
        var messageService = new FakeMessageService();
        var vm = new ConversationViewModel(messageService, Substitute.For<INavigationService>());
        vm.Initialize(peer);

        messageService.GetOrCreateConversation(peer).Messages.Add(new StoredMessage
        {
            Peer = peer,
            Text = "Inbound",
            Timestamp = DateTimeOffset.UtcNow,
            IsOutbound = false
        });

        Assert.Single(vm.Messages);
        Assert.Equal("Inbound", vm.Messages[0].Text);
    }

    [Fact]
    public async Task SendAsync_RejectsInvalidDestination()
    {
        var vm = new ConversationViewModel(new FakeMessageService(), Substitute.For<INavigationService>());
        vm.InitializeNew();
        vm.DestinationCallsign = "@@@";
        vm.MessageText = "Hi";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("valid destination", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeMessageService : IMessageService
    {
        private readonly ObservableCollection<ConversationThread> _conversations = new();

        public ObservableCollection<ConversationThread> Conversations => _conversations;

        public ConversationThread GetOrCreateConversation(Callsign peer)
        {
            foreach (var existing in _conversations)
            {
                if (existing.Peer.Equals(peer))
                    return existing;
            }

            var thread = new ConversationThread(peer);
            _conversations.Add(thread);
            return thread;
        }

        public Task SendAsync(Callsign addressee, string text, CancellationToken cancellationToken = default)
        {
            var thread = GetOrCreateConversation(addressee);
            thread.Messages.Add(new StoredMessage
            {
                Peer = addressee,
                Text = text,
                Timestamp = DateTimeOffset.UtcNow,
                IsOutbound = true
            });
            return Task.CompletedTask;
        }

    }
}
