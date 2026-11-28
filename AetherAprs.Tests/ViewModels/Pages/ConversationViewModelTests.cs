// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration.Settings;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Messaging;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

public sealed class ConversationViewModelTests : TestFixtureBase
{
    [Fact]
    public void Initialize_ReflectsExistingMessages()
    {
        AprsAddress peer = AprsAddress.FromCallsign(new Callsign("K0PEER", 1));
        FakeMessageService messageService = new();
        ConversationThread thread = messageService.GetOrCreateConversation(peer);
        thread.Add(new StoredMessage
        {
            Peer = peer,
            Text = "Hello",
            Timestamp = DateTimeOffset.UtcNow,
            IsOutbound = false
        });

        ConversationViewModel vm = CreateViewModel(messageService);
        vm.Initialize(peer);

        Assert.Equal("K0PEER-1", vm.Title);
        Assert.False(vm.IsDestinationEditable);
        StoredMessage item = Assert.Single(vm.Messages);
        Assert.Equal("Hello", item.Text);
    }

    [Fact]
    public void Initialize_SubscribesToInboundMessagesWhileOpen()
    {
        AprsAddress peer = AprsAddress.FromCallsign(new Callsign("K0PEER", 1));
        FakeMessageService messageService = new();
        ConversationViewModel vm = CreateViewModel(messageService);
        vm.Initialize(peer);

        messageService.GetOrCreateConversation(peer).Add(new StoredMessage
        {
            Peer = peer,
            Text = "Inbound",
            Timestamp = DateTimeOffset.UtcNow,
            IsOutbound = false
        });

        StoredMessage item = Assert.Single(vm.Messages);
        Assert.Equal("Inbound", item.Text);
    }

    [Fact]
    public async Task SendAsync_RejectsInvalidDestination()
    {
        ConversationViewModel vm = CreateViewModel(new FakeMessageService());
        vm.InitializeNew();
        vm.DestinationAddress = "@@@";
        vm.MessageText = "Hi";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("valid destination", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateTemplateDialog_ExistingConversation_PassesPeerTemplatesAndDestination()
    {
        AprsAddress peer = new("SOTA", 1);
        IMessageTemplateService templateService = Substitute.For<IMessageTemplateService>();
        List<MessageTemplate> expectedTemplates = [new() { Name = "SOTA spot" }];
        templateService.GetTemplatesForPeer(peer).Returns(expectedTemplates);

        MessageTemplateDialogViewModel dialog = new();
        IMessageTemplateDialogViewModelFactory templateDialogFactory = Substitute.For<IMessageTemplateDialogViewModelFactory>();
        templateDialogFactory
            .Create(Arg.Any<IReadOnlyList<MessageTemplate>>(), Arg.Any<string?>())
            .Returns(dialog);

        ConversationViewModel vm = CreateViewModel(new FakeMessageService(), templateService, templateDialogFactory);
        vm.Initialize(peer);

        MessageTemplateDialogViewModel result = vm.CreateTemplateDialog();

        Assert.Same(dialog, result);
        templateDialogFactory.Received().Create(expectedTemplates, "SOTA-1");
    }

    [Fact]
    public void CreateTemplateDialog_NewConversationWithoutDestination_PassesAllTemplatesAndNullDestination()
    {
        IMessageTemplateService templateService = Substitute.For<IMessageTemplateService>();
        List<MessageTemplate> allTemplates = [new() { Name = "Generic" }];
        templateService.Templates.Returns(allTemplates);

        MessageTemplateDialogViewModel dialog = new();
        IMessageTemplateDialogViewModelFactory templateDialogFactory = Substitute.For<IMessageTemplateDialogViewModelFactory>();
        templateDialogFactory
            .Create(Arg.Any<IReadOnlyList<MessageTemplate>>(), Arg.Any<string?>())
            .Returns(dialog);

        ConversationViewModel vm = CreateViewModel(new FakeMessageService(), templateService, templateDialogFactory);
        vm.InitializeNew();

        MessageTemplateDialogViewModel result = vm.CreateTemplateDialog();

        Assert.Same(dialog, result);
        templateDialogFactory.Received().Create(allTemplates, null);
    }

    [Fact]
    public void CreateTemplateDialog_NewConversationWithTypedDestination_PassesPeerTemplatesAndNullDestination()
    {
        AprsAddress peer = new("K0PEER");
        IMessageTemplateService templateService = Substitute.For<IMessageTemplateService>();
        List<MessageTemplate> expectedTemplates = [new() { Name = "Peer spot" }];
        templateService.GetTemplatesForPeer(peer).Returns(expectedTemplates);

        MessageTemplateDialogViewModel dialog = new();
        IMessageTemplateDialogViewModelFactory templateDialogFactory = Substitute.For<IMessageTemplateDialogViewModelFactory>();
        templateDialogFactory
            .Create(Arg.Any<IReadOnlyList<MessageTemplate>>(), Arg.Any<string?>())
            .Returns(dialog);

        ConversationViewModel vm = CreateViewModel(new FakeMessageService(), templateService, templateDialogFactory);
        vm.InitializeNew();
        vm.DestinationAddress = "K0PEER";

        MessageTemplateDialogViewModel result = vm.CreateTemplateDialog();

        Assert.Same(dialog, result);
        templateDialogFactory.Received().Create(expectedTemplates, null);
    }

    [Fact]
    public void CreateTemplateDialog_NewConversationWithTypedTacticalAddress_ScopesTemplatesToIt()
    {
        AprsAddress peer = new("APRS2SOTA");
        IMessageTemplateService templateService = Substitute.For<IMessageTemplateService>();
        List<MessageTemplate> expectedTemplates = [new() { Name = "SOTA spot" }];
        templateService.GetTemplatesForPeer(peer).Returns(expectedTemplates);

        MessageTemplateDialogViewModel dialog = new();
        IMessageTemplateDialogViewModelFactory templateDialogFactory = Substitute.For<IMessageTemplateDialogViewModelFactory>();
        templateDialogFactory
            .Create(Arg.Any<IReadOnlyList<MessageTemplate>>(), Arg.Any<string?>())
            .Returns(dialog);

        ConversationViewModel vm = CreateViewModel(new FakeMessageService(), templateService, templateDialogFactory);
        vm.InitializeNew();
        vm.DestinationAddress = "APRS2SOTA";

        MessageTemplateDialogViewModel result = vm.CreateTemplateDialog();

        Assert.Same(dialog, result);
        templateService.Received(1).GetTemplatesForPeer(peer);
        templateDialogFactory.Received().Create(expectedTemplates, null);
    }

    [Fact]
    public void ApplyTemplateResult_EditableDestination_SetsMessageTextAndDestination()
    {
        ConversationViewModel vm = CreateViewModel(new FakeMessageService());
        vm.InitializeNew();

        vm.ApplyTemplateResult(new MessageTemplateDialogResult("Summit report", "W7SUM"));

        Assert.Equal("Summit report", vm.MessageText);
        Assert.Equal("W7SUM", vm.DestinationAddress);
    }

    [Fact]
    public void ApplyTemplateResult_LockedDestination_SetsOnlyMessageText()
    {
        AprsAddress peer = AprsAddress.FromCallsign(new Callsign("K0PEER", 1));
        ConversationViewModel vm = CreateViewModel(new FakeMessageService());
        vm.Initialize(peer);

        vm.ApplyTemplateResult(new MessageTemplateDialogResult("Summit report", "W7SUM"));

        Assert.Equal("Summit report", vm.MessageText);
        Assert.Equal("K0PEER-1", vm.DestinationAddress);
    }

    private static ConversationViewModel CreateViewModel(
        IMessageService messageService,
        IMessageTemplateService? templateService = null,
        IMessageTemplateDialogViewModelFactory? templateDialogFactory = null,
        IActiveConversationTracker? activeConversationTracker = null) => new(
            messageService,
            templateService ?? Substitute.For<IMessageTemplateService>(),
            templateDialogFactory ?? Substitute.For<IMessageTemplateDialogViewModelFactory>(),
            NullLogger<ConversationViewModel>.Instance,
            activeConversationTracker);

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
                    return existing;
                }
            }

            ConversationThread thread = new(peer);
            _conversations.Add(thread);
            ConversationsChanged?.Invoke(this, EventArgs.Empty);
            return thread;
        }

        public Task SendAsync(AprsAddress addressee, string text, CancellationToken cancellationToken = default)
        {
            ConversationThread thread = GetOrCreateConversation(addressee);
            thread.Add(new StoredMessage
            {
                Peer = addressee,
                Text = text,
                Timestamp = DateTimeOffset.UtcNow,
                IsOutbound = true
            });
            ConversationsChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public void InitializeAndDetach_TrackTheVisibleConversation()
    {
        FakeMessageService messageService = new();
        ActiveConversationTracker tracker = new();
        ConversationViewModel vm = CreateViewModel(messageService, activeConversationTracker: tracker);
        AprsAddress peer = AprsAddress.FromCallsign(new Callsign("K0PEER", 1));

        vm.Initialize(peer);
        Assert.Equal(peer, tracker.Peer);

        vm.Detach();
        Assert.Null(tracker.Peer);
    }

    [Fact]
    public void InitializeNew_ClearsTheVisibleConversation()
    {
        FakeMessageService messageService = new();
        ActiveConversationTracker tracker = new();
        ConversationViewModel vm = CreateViewModel(messageService, activeConversationTracker: tracker);

        vm.Initialize(AprsAddress.FromCallsign(new Callsign("K0PEER", 1)));
        vm.InitializeNew();

        Assert.Null(tracker.Peer);
    }
}
