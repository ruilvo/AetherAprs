// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Factories.ViewModels;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Pages;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

public sealed class MessagesViewModelTests : TestFixtureBase
{
    [Fact]
    public void Conversations_MirrorTheServiceSnapshotWithoutAliasingIt()
    {
        FakeMessageService messageService = new();
        AprsAddress peer = AprsAddress.FromCallsign(new Callsign("K0PEER", 1));
        messageService.GetOrCreateConversation(peer);

        using MessagesViewModel vm = CreateViewModel(messageService, Substitute.For<INavigationService>());

        Assert.NotSame(messageService.Conversations, vm.Conversations);
        ConversationThread item = Assert.Single(vm.Conversations);
        Assert.Same(Assert.Single(messageService.Conversations), item);
        Assert.Equal(peer, item.Peer);
    }

    [Fact]
    public void ServiceReorder_MovesTheExistingItemInsteadOfReplacingIt()
    {
        FakeMessageService messageService = new();
        AprsAddress first = AprsAddress.FromCallsign(new Callsign("K0AAA", 1));
        AprsAddress second = AprsAddress.FromCallsign(new Callsign("K0BBB", 2));
        ConversationThread firstThread = messageService.GetOrCreateConversation(first);
        ConversationThread secondThread = messageService.GetOrCreateConversation(second);

        using MessagesViewModel vm = CreateViewModel(messageService, Substitute.For<INavigationService>());
        Assert.Equal(2, vm.Conversations.Count);
        Assert.Same(secondThread, vm.Conversations[0]);
        Assert.Same(firstThread, vm.Conversations[1]);

        // Touching the older conversation moves it to the front of the snapshot.
        messageService.GetOrCreateConversation(first);

        Assert.Same(firstThread, vm.Conversations[0]);
        Assert.Same(secondThread, vm.Conversations[1]);
    }

    [Fact]
    public void ConversationChangesUpdateEmptyState()
    {
        FakeMessageService service = new();
        using MessagesViewModel vm = CreateViewModel(service, Substitute.For<INavigationService>());
        int changes = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MessagesViewModel.HasConversations))
            {
                changes++;
            }
        };

        Assert.False(vm.HasConversations);
        service.GetOrCreateConversation(AprsAddress.FromCallsign(new Callsign("K0PEER", 1)));
        Assert.True(vm.HasConversations);
        service.ClearConversations();
        Assert.Empty(vm.Conversations);
        Assert.False(vm.HasConversations);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void NewMessage_NavigatesToInitializedConversation()
    {
        INavigationService navigation = Substitute.For<INavigationService>();
        ViewModelBase? navigated = null;
        navigation.When(n => n.NavigateTo(Arg.Any<ViewModelBase>()))
            .Do(ci => navigated = ci.Arg<ViewModelBase>());

        MessagesViewModel vm = CreateViewModel(new FakeMessageService(), navigation);
        vm.NewMessageCommand.Execute(null);

        ConversationViewModel conversation = Assert.IsType<ConversationViewModel>(navigated);
        Assert.True(conversation.IsDestinationEditable);
        Assert.Equal("New Message", conversation.Title);
    }

    [Fact]
    public void OpenConversation_Null_DoesNothing()
    {
        INavigationService navigation = Substitute.For<INavigationService>();
        MessagesViewModel vm = CreateViewModel(new FakeMessageService(), navigation);

        vm.OpenConversationCommand.Execute(null);

        navigation.DidNotReceive().NavigateTo(Arg.Any<ViewModelBase>());
    }

    [Fact]
    public void OpenConversation_NavigatesWithPeerInitialized()
    {
        AprsAddress peer = AprsAddress.FromCallsign(new Callsign("K0PEER", 1));
        ConversationThread thread = new(peer);
        INavigationService navigation = Substitute.For<INavigationService>();
        ViewModelBase? navigated = null;
        navigation.When(n => n.NavigateTo(Arg.Any<ViewModelBase>()))
            .Do(ci => navigated = ci.Arg<ViewModelBase>());

        MessagesViewModel vm = CreateViewModel(new FakeMessageService(), navigation);
        vm.OpenConversationCommand.Execute(thread);

        ConversationViewModel conversation = Assert.IsType<ConversationViewModel>(navigated);
        Assert.False(conversation.IsDestinationEditable);
        Assert.Equal("K0PEER-1", conversation.Title);
        Assert.Equal("K0PEER-1", conversation.DestinationAddress);
    }

    private static MessagesViewModel CreateViewModel(
        IMessageService messageService,
        INavigationService navigation)
    {
        ServiceCollection services = new();
        services.AddSingleton(messageService);
        services.AddSingleton(navigation);
        services.AddLogging();
        services.AddSingleton(Substitute.For<IMessageTemplateService>());
        services.AddSingleton(Substitute.For<IMessageTemplateDialogViewModelFactory>());
        services.AddTransient<ConversationViewModel>();
        services.AddSingleton<IConversationViewModelFactory, ConversationViewModelFactory>();
        ServiceProvider provider = services.BuildServiceProvider();
        IConversationViewModelFactory factory = provider.GetRequiredService<IConversationViewModelFactory>();
        BulletinsViewModel bulletinsViewModel = new(
            Substitute.For<IBulletinService>(),
            navigation);
        return new MessagesViewModel(messageService, navigation, factory, bulletinsViewModel);
    }

    private sealed class FakeMessageService : IMessageService
    {
        private readonly List<ConversationThread> _conversations = [];

        public IReadOnlyList<ConversationThread> Conversations => _conversations;

        public event EventHandler? ConversationsChanged;

        public ConversationThread GetOrCreateConversation(AprsAddress peer)
        {
            foreach (ConversationThread existing in _conversations)
            {
                if (existing.Peer.Equals(peer))
                {
                    int index = _conversations.IndexOf(existing);
                    if (index > 0)
                    {
                        _conversations.RemoveAt(index);
                        _conversations.Insert(0, existing);
                        ConversationsChanged?.Invoke(this, EventArgs.Empty);
                    }

                    return existing;
                }
            }

            ConversationThread thread = new(peer);
            _conversations.Insert(0, thread);
            ConversationsChanged?.Invoke(this, EventArgs.Empty);
            return thread;
        }

        public void ClearConversations()
        {
            _conversations.Clear();
            ConversationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task SendAsync(AprsAddress addressee, string text, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
