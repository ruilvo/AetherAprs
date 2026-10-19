// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration.Settings;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Messaging;
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

        var vm = CreateViewModel(messageService);
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
        var vm = CreateViewModel(messageService);
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
        var vm = CreateViewModel(new FakeMessageService());
        vm.InitializeNew();
        vm.DestinationCallsign = "@@@";
        vm.MessageText = "Hi";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("valid destination", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateTemplateDialog_ExistingConversation_PassesPeerTemplatesAndDestination()
    {
        var peer = new Callsign("SOTA", 1);
        var templateService = Substitute.For<IMessageTemplateService>();
        var expectedTemplates = new List<MessageTemplate> { new() { Name = "SOTA spot" } };
        templateService.GetTemplatesForPeer(peer).Returns(expectedTemplates);

        var dialog = new MessageTemplateDialogViewModel();
        var templateDialogFactory = Substitute.For<IMessageTemplateDialogViewModelFactory>();
        templateDialogFactory
            .Create(Arg.Any<IReadOnlyList<MessageTemplate>>(), Arg.Any<string?>())
            .Returns(dialog);

        var vm = CreateViewModel(new FakeMessageService(), templateService, templateDialogFactory);
        vm.Initialize(peer);

        var result = vm.CreateTemplateDialog();

        Assert.Same(dialog, result);
        templateDialogFactory.Received().Create(expectedTemplates, "SOTA-1");
    }

    [Fact]
    public void CreateTemplateDialog_NewConversationWithoutDestination_PassesAllTemplatesAndNullDestination()
    {
        var templateService = Substitute.For<IMessageTemplateService>();
        var allTemplates = new List<MessageTemplate> { new() { Name = "Generic" } };
        templateService.Templates.Returns(allTemplates);

        var dialog = new MessageTemplateDialogViewModel();
        var templateDialogFactory = Substitute.For<IMessageTemplateDialogViewModelFactory>();
        templateDialogFactory
            .Create(Arg.Any<IReadOnlyList<MessageTemplate>>(), Arg.Any<string?>())
            .Returns(dialog);

        var vm = CreateViewModel(new FakeMessageService(), templateService, templateDialogFactory);
        vm.InitializeNew();

        var result = vm.CreateTemplateDialog();

        Assert.Same(dialog, result);
        templateDialogFactory.Received().Create(allTemplates, null);
    }

    [Fact]
    public void CreateTemplateDialog_NewConversationWithTypedDestination_PassesPeerTemplatesAndNullDestination()
    {
        var peer = new Callsign("K0PEER");
        var templateService = Substitute.For<IMessageTemplateService>();
        var expectedTemplates = new List<MessageTemplate> { new() { Name = "Peer spot" } };
        templateService.GetTemplatesForPeer(peer).Returns(expectedTemplates);

        var dialog = new MessageTemplateDialogViewModel();
        var templateDialogFactory = Substitute.For<IMessageTemplateDialogViewModelFactory>();
        templateDialogFactory
            .Create(Arg.Any<IReadOnlyList<MessageTemplate>>(), Arg.Any<string?>())
            .Returns(dialog);

        var vm = CreateViewModel(new FakeMessageService(), templateService, templateDialogFactory);
        vm.InitializeNew();
        vm.DestinationCallsign = "K0PEER";

        var result = vm.CreateTemplateDialog();

        Assert.Same(dialog, result);
        templateDialogFactory.Received().Create(expectedTemplates, null);
    }

    [Fact]
    public void ApplyTemplateResult_EditableDestination_SetsMessageTextAndDestination()
    {
        var vm = CreateViewModel(new FakeMessageService());
        vm.InitializeNew();

        vm.ApplyTemplateResult(new MessageTemplateDialogResult("Summit report", "W7SUM"));

        Assert.Equal("Summit report", vm.MessageText);
        Assert.Equal("W7SUM", vm.DestinationCallsign);
    }

    [Fact]
    public void ApplyTemplateResult_LockedDestination_SetsOnlyMessageText()
    {
        var peer = new Callsign("K0PEER", 1);
        var vm = CreateViewModel(new FakeMessageService());
        vm.Initialize(peer);

        vm.ApplyTemplateResult(new MessageTemplateDialogResult("Summit report", "W7SUM"));

        Assert.Equal("Summit report", vm.MessageText);
        Assert.Equal("K0PEER-1", vm.DestinationCallsign);
    }

    private static ConversationViewModel CreateViewModel(
        IMessageService messageService,
        IMessageTemplateService? templateService = null,
        IMessageTemplateDialogViewModelFactory? templateDialogFactory = null)
    {
        return new ConversationViewModel(
            messageService,
            templateService ?? Substitute.For<IMessageTemplateService>(),
            templateDialogFactory ?? Substitute.For<IMessageTemplateDialogViewModelFactory>(),
            NullLogger<ConversationViewModel>.Instance);
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

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
