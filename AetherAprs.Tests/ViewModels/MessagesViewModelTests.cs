// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Factories;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using AetherAprs.Services;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class MessagesViewModelTests : TestFixtureBase
{
    [Fact]
    public void Conversations_ExposesMessageServiceCollection()
    {
        var messageService = new FakeMessageService();
        var peer = new Callsign("K0PEER", 1);
        messageService.GetOrCreateConversation(peer);

        var vm = CreateViewModel(messageService, Substitute.For<INavigationService>());

        Assert.Same(messageService.Conversations, vm.Conversations);
        Assert.Single(vm.Conversations);
        Assert.Equal(peer, vm.Conversations[0].Peer);
    }

    [Fact]
    public void NewMessage_NavigatesToInitializedConversation()
    {
        var navigation = Substitute.For<INavigationService>();
        ViewModelBase? navigated = null;
        navigation.When(n => n.NavigateTo(Arg.Any<ViewModelBase>()))
            .Do(ci => navigated = ci.Arg<ViewModelBase>());

        var vm = CreateViewModel(new FakeMessageService(), navigation);
        vm.NewMessageCommand.Execute(null);

        var conversation = Assert.IsType<ConversationViewModel>(navigated);
        Assert.True(conversation.IsDestinationEditable);
        Assert.Equal("New Message", conversation.Title);
    }

    [Fact]
    public void OpenConversation_Null_DoesNothing()
    {
        var navigation = Substitute.For<INavigationService>();
        var vm = CreateViewModel(new FakeMessageService(), navigation);

        vm.OpenConversationCommand.Execute(null);

        navigation.DidNotReceive().NavigateTo(Arg.Any<ViewModelBase>());
    }

    [Fact]
    public void OpenConversation_NavigatesWithPeerInitialized()
    {
        var peer = new Callsign("K0PEER", 1);
        var thread = new ConversationThread(peer);
        var navigation = Substitute.For<INavigationService>();
        ViewModelBase? navigated = null;
        navigation.When(n => n.NavigateTo(Arg.Any<ViewModelBase>()))
            .Do(ci => navigated = ci.Arg<ViewModelBase>());

        var vm = CreateViewModel(new FakeMessageService(), navigation);
        vm.OpenConversationCommand.Execute(thread);

        var conversation = Assert.IsType<ConversationViewModel>(navigated);
        Assert.False(conversation.IsDestinationEditable);
        Assert.Equal("K0PEER-1", conversation.Title);
        Assert.Equal("K0PEER-1", conversation.DestinationCallsign);
    }

    private static MessagesViewModel CreateViewModel(
        IMessageService messageService,
        INavigationService navigation)
    {
        var services = new ServiceCollection();
        services.AddSingleton(messageService);
        services.AddSingleton(navigation);
        services.AddLogging();
        services.AddTransient<ConversationViewModel>();
        services.AddSingleton<IConversationViewModelFactory, ConversationViewModelFactory>();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IConversationViewModelFactory>();
        return new MessagesViewModel(messageService, navigation, factory);
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

        public Task SendAsync(Callsign addressee, string text, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
