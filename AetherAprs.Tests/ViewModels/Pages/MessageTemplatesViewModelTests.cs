// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration.Settings;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels.Pages;

using NSubstitute;

using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

public sealed class MessageTemplatesViewModelTests
{
    [Fact]
    public void Refresh_CopiesTemplatesFromService()
    {
        List<MessageTemplate> templates =
        [
            new() { Name = "SOTA spot", Destination = "SOTA", Body = "{summit}" },
            new() { Name = "Generic", Body = "Hello" }
        ];
        IMessageTemplateService templateService = Substitute.For<IMessageTemplateService>();
        templateService.Templates.Returns(templates);

        MessageTemplatesViewModel vm = CreateViewModel(templateService);
        vm.Refresh();

        Assert.Equal(2, vm.Templates.Count);
        Assert.Equal("SOTA spot", vm.Templates[0].Name);
        Assert.Equal("SOTA", vm.Templates[0].Destination);
        Assert.Equal("Generic", vm.Templates[1].Name);
    }

    [Fact]
    public void Refresh_ReplacesPreviouslyLoadedTemplates()
    {
        List<MessageTemplate> backing = [new() { Name = "A" }];
        IMessageTemplateService templateService = Substitute.For<IMessageTemplateService>();
        templateService.Templates.Returns(backing);

        MessageTemplatesViewModel vm = CreateViewModel(templateService);
        vm.Refresh();
        backing.Clear();
        backing.Add(new MessageTemplate { Name = "B" });
        vm.Refresh();

        MessageTemplate item = Assert.Single(vm.Templates);
        Assert.Equal("B", item.Name);
    }

    [Fact]
    public void Refresh_EmptyThenPopulated_TogglesEmptyStateFlags()
    {
        List<MessageTemplate> backing = [];
        IMessageTemplateService templateService = Substitute.For<IMessageTemplateService>();
        templateService.Templates.Returns(_ => backing);

        MessageTemplatesViewModel vm = CreateViewModel(templateService);

        vm.Refresh();
        Assert.False(vm.HasTemplates);
        Assert.True(vm.IsTemplatesEmpty);

        backing.Add(new MessageTemplate { Name = "A" });
        vm.Refresh();
        Assert.True(vm.HasTemplates);
        Assert.False(vm.IsTemplatesEmpty);
    }

    [Fact]
    public void Refresh_PopulatedThenEmpty_TogglesEmptyStateFlags()
    {
        List<MessageTemplate> backing = [new() { Name = "A" }];
        IMessageTemplateService templateService = Substitute.For<IMessageTemplateService>();
        templateService.Templates.Returns(_ => backing);

        MessageTemplatesViewModel vm = CreateViewModel(templateService);

        vm.Refresh();
        Assert.True(vm.HasTemplates);
        Assert.False(vm.IsTemplatesEmpty);

        backing.Clear();
        vm.Refresh();
        Assert.False(vm.HasTemplates);
        Assert.True(vm.IsTemplatesEmpty);
    }

    [Fact]
    public void Refresh_RaisesStateFlagNotifications()
    {
        IMessageTemplateService templateService = Substitute.For<IMessageTemplateService>();
        templateService.Templates.Returns([new() { Name = "A" }]);

        MessageTemplatesViewModel vm = CreateViewModel(templateService);
        List<string> raised = [];
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        vm.Refresh();

        Assert.Contains(nameof(MessageTemplatesViewModel.HasTemplates), raised);
        Assert.Contains(nameof(MessageTemplatesViewModel.IsTemplatesEmpty), raised);
    }

    [Fact]
    public async Task DeleteAsync_WithoutConfirmation_DoesNotCallService()
    {
        IMessageTemplateService templateService = Substitute.For<IMessageTemplateService>();
        IConfirmationDialogService confirmationDialogService = Substitute.For<IConfirmationDialogService>();
        confirmationDialogService
            .ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);

        MessageTemplatesViewModel vm = CreateViewModel(templateService, confirmationDialogService);

        await vm.DeleteCommand.ExecuteAsync(new MessageTemplate { Name = "A" });

        await templateService.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Empty(vm.Templates);
    }

    [Fact]
    public async Task DeleteAsync_WithConfirmation_CallsServiceAndRefreshes()
    {
        MessageTemplate template = new() { Name = "A", Body = "x" };
        List<MessageTemplate> backing = [template];
        IMessageTemplateService templateService = Substitute.For<IMessageTemplateService>();
        templateService.Templates.Returns(backing);
        IConfirmationDialogService confirmationDialogService = Substitute.For<IConfirmationDialogService>();
        confirmationDialogService
            .ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);
        templateService
            .When(t => t.DeleteAsync(template.Name, Arg.Any<CancellationToken>()))
            .Do(_ => backing.Remove(template));

        MessageTemplatesViewModel vm = CreateViewModel(templateService, confirmationDialogService);
        vm.Refresh();

        await vm.DeleteCommand.ExecuteAsync(template);

        await templateService.Received(1).DeleteAsync(template.Name, Arg.Any<CancellationToken>());
        Assert.Empty(vm.Templates);
    }

    [Fact]
    public async Task DeleteAsync_ShowsConfirmationDialogWithTemplateName()
    {
        IMessageTemplateService templateService = Substitute.For<IMessageTemplateService>();
        IConfirmationDialogService confirmationDialogService = Substitute.For<IConfirmationDialogService>();
        confirmationDialogService
            .ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);

        MessageTemplatesViewModel vm = CreateViewModel(templateService, confirmationDialogService);

        await vm.DeleteCommand.ExecuteAsync(new MessageTemplate { Name = "My Template" });

        await confirmationDialogService.Received(1).ShowConfirmationAsync(
            Arg.Any<string>(),
            Arg.Is<string>(message => message.Contains("My Template", StringComparison.Ordinal)));
    }

    [Fact]
    public void Add_RaisesAddRequestedWithNullTemplate()
    {
        MessageTemplatesViewModel vm = CreateViewModel();

        MessageTemplate? received = null;
        int raiseCount = 0;
        vm.AddRequested += (_, template) =>
        {
            raiseCount++;
            received = template;
        };

        vm.AddCommand.Execute(null);

        Assert.Equal(1, raiseCount);
        Assert.Null(received);
    }

    [Fact]
    public void Edit_RaisesEditRequestedWithTemplate()
    {
        MessageTemplatesViewModel vm = CreateViewModel();

        MessageTemplate template = new() { Name = "A" };
        MessageTemplate? received = null;
        int raiseCount = 0;
        vm.EditRequested += (_, requested) =>
        {
            raiseCount++;
            received = requested;
        };

        vm.EditCommand.Execute(template);

        Assert.Equal(1, raiseCount);
        Assert.Same(template, received);
    }

    [Fact]
    public void Close_CallsNavigationGoBack()
    {
        INavigationService navigation = Substitute.For<INavigationService>();
        MessageTemplatesViewModel vm = CreateViewModel(navigationService: navigation);

        vm.CloseCommand.Execute(null);

        navigation.Received(1).GoBack();
    }

    [Fact]
    public void CreateEditViewModel_PassesOriginalAndNamesExcludingOriginalToFactory()
    {
        List<MessageTemplate> backing =
        [
            new() { Name = "Alpha", Body = "a" },
            new() { Name = "Beta", Body = "b" }
        ];
        IMessageTemplateService templateService = Substitute.For<IMessageTemplateService>();
        templateService.Templates.Returns(backing);
        IMessageTemplateEditViewModelFactory factory = Substitute.For<IMessageTemplateEditViewModelFactory>();
        MessageTemplate? receivedOriginal = null;
        IReadOnlyList<string>? receivedNames = null;
        factory
            .Create(Arg.Any<MessageTemplate?>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(callInfo =>
            {
                receivedOriginal = callInfo.ArgAt<MessageTemplate?>(0);
                receivedNames = callInfo.ArgAt<IReadOnlyList<string>>(1);
                return new AetherAprs.ViewModels.Components.MessageTemplateEditViewModel();
            });

        MessageTemplatesViewModel vm = CreateViewModel(templateService, editViewModelFactory: factory);
        vm.Refresh();
        MessageTemplate original = backing[0];

        vm.CreateEditViewModel(original);

        Assert.Same(original, receivedOriginal);
        Assert.NotNull(receivedNames);
        Assert.DoesNotContain("Alpha", receivedNames!);
        Assert.Contains("Beta", receivedNames);
    }

    [Fact]
    public void CreateEditViewModel_AddMode_PassesAllNamesAndNullOriginal()
    {
        List<MessageTemplate> backing =
        [
            new() { Name = "Alpha", Body = "a" }
        ];
        IMessageTemplateService templateService = Substitute.For<IMessageTemplateService>();
        templateService.Templates.Returns(backing);
        IMessageTemplateEditViewModelFactory factory = Substitute.For<IMessageTemplateEditViewModelFactory>();
        MessageTemplate? receivedOriginal = null;
        IReadOnlyList<string>? receivedNames = null;
        factory
            .Create(Arg.Any<MessageTemplate?>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(callInfo =>
            {
                receivedOriginal = callInfo.ArgAt<MessageTemplate?>(0);
                receivedNames = callInfo.ArgAt<IReadOnlyList<string>>(1);
                return new AetherAprs.ViewModels.Components.MessageTemplateEditViewModel();
            });

        MessageTemplatesViewModel vm = CreateViewModel(templateService, editViewModelFactory: factory);
        vm.Refresh();

        vm.CreateEditViewModel(null);

        Assert.Null(receivedOriginal);
        Assert.NotNull(receivedNames);
        Assert.Equal(["Alpha"], receivedNames);
    }

    private static MessageTemplatesViewModel CreateViewModel(
        IMessageTemplateService? templateService = null,
        IConfirmationDialogService? confirmationDialogService = null,
        INavigationService? navigationService = null,
        IMessageTemplateEditViewModelFactory? editViewModelFactory = null) => new(
            templateService ?? Substitute.For<IMessageTemplateService>(),
            confirmationDialogService ?? Substitute.For<IConfirmationDialogService>(),
            navigationService ?? Substitute.For<INavigationService>(),
            editViewModelFactory ?? Substitute.For<IMessageTemplateEditViewModelFactory>());
}
