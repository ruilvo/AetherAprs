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
        var templates = new List<MessageTemplate>
        {
            new() { Name = "SOTA spot", Destination = "SOTA", Body = "{summit}" },
            new() { Name = "Generic", Body = "Hello" }
        };
        var templateService = Substitute.For<IMessageTemplateService>();
        templateService.Templates.Returns(templates);

        var vm = CreateViewModel(templateService);
        vm.Refresh();

        Assert.Equal(2, vm.Templates.Count);
        Assert.Equal("SOTA spot", vm.Templates[0].Name);
        Assert.Equal("SOTA", vm.Templates[0].Destination);
        Assert.Equal("Generic", vm.Templates[1].Name);
    }

    [Fact]
    public void Refresh_ReplacesPreviouslyLoadedTemplates()
    {
        var backing = new List<MessageTemplate> { new() { Name = "A" } };
        var templateService = Substitute.For<IMessageTemplateService>();
        templateService.Templates.Returns(backing);

        var vm = CreateViewModel(templateService);
        vm.Refresh();
        backing.Clear();
        backing.Add(new MessageTemplate { Name = "B" });
        vm.Refresh();

        Assert.Single(vm.Templates);
        Assert.Equal("B", vm.Templates[0].Name);
    }

    [Fact]
    public async Task DeleteAsync_WithoutConfirmation_DoesNotCallService()
    {
        var templateService = Substitute.For<IMessageTemplateService>();
        var confirmationDialogService = Substitute.For<IConfirmationDialogService>();
        confirmationDialogService
            .ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);

        var vm = CreateViewModel(templateService, confirmationDialogService);

        await vm.DeleteCommand.ExecuteAsync(new MessageTemplate { Name = "A" });

        await templateService.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Empty(vm.Templates);
    }

    [Fact]
    public async Task DeleteAsync_WithConfirmation_CallsServiceAndRefreshes()
    {
        var template = new MessageTemplate { Name = "A", Body = "x" };
        var backing = new List<MessageTemplate> { template };
        var templateService = Substitute.For<IMessageTemplateService>();
        templateService.Templates.Returns(backing);
        var confirmationDialogService = Substitute.For<IConfirmationDialogService>();
        confirmationDialogService
            .ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);
        templateService
            .When(t => t.DeleteAsync(template.Name, Arg.Any<CancellationToken>()))
            .Do(_ => backing.Remove(template));

        var vm = CreateViewModel(templateService, confirmationDialogService);
        vm.Refresh();

        await vm.DeleteCommand.ExecuteAsync(template);

        await templateService.Received(1).DeleteAsync(template.Name, Arg.Any<CancellationToken>());
        Assert.Empty(vm.Templates);
    }

    [Fact]
    public async Task DeleteAsync_ShowsConfirmationDialogWithTemplateName()
    {
        var templateService = Substitute.For<IMessageTemplateService>();
        var confirmationDialogService = Substitute.For<IConfirmationDialogService>();
        confirmationDialogService
            .ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);

        var vm = CreateViewModel(templateService, confirmationDialogService);

        await vm.DeleteCommand.ExecuteAsync(new MessageTemplate { Name = "My Template" });

        await confirmationDialogService.Received(1).ShowConfirmationAsync(
            Arg.Any<string>(),
            Arg.Is<string>(message => message.Contains("My Template", StringComparison.Ordinal)));
    }

    [Fact]
    public void Add_RaisesAddRequestedWithNullTemplate()
    {
        var vm = CreateViewModel();

        MessageTemplate? received = null;
        var raiseCount = 0;
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
        var vm = CreateViewModel();

        var template = new MessageTemplate { Name = "A" };
        MessageTemplate? received = null;
        var raiseCount = 0;
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
        var navigation = Substitute.For<INavigationService>();
        var vm = CreateViewModel(navigationService: navigation);

        vm.CloseCommand.Execute(null);

        navigation.Received(1).GoBack();
    }

    [Fact]
    public void CreateEditViewModel_PassesOriginalAndNamesExcludingOriginalToFactory()
    {
        var backing = new List<MessageTemplate>
        {
            new() { Name = "Alpha", Body = "a" },
            new() { Name = "Beta", Body = "b" }
        };
        var templateService = Substitute.For<IMessageTemplateService>();
        templateService.Templates.Returns(backing);
        var factory = Substitute.For<IMessageTemplateEditViewModelFactory>();
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

        var vm = CreateViewModel(templateService, editViewModelFactory: factory);
        vm.Refresh();
        var original = backing[0];

        vm.CreateEditViewModel(original);

        Assert.Same(original, receivedOriginal);
        Assert.NotNull(receivedNames);
        Assert.DoesNotContain("Alpha", receivedNames!);
        Assert.Contains("Beta", receivedNames);
    }

    [Fact]
    public void CreateEditViewModel_AddMode_PassesAllNamesAndNullOriginal()
    {
        var backing = new List<MessageTemplate>
        {
            new() { Name = "Alpha", Body = "a" }
        };
        var templateService = Substitute.For<IMessageTemplateService>();
        templateService.Templates.Returns(backing);
        var factory = Substitute.For<IMessageTemplateEditViewModelFactory>();
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

        var vm = CreateViewModel(templateService, editViewModelFactory: factory);
        vm.Refresh();

        vm.CreateEditViewModel(null);

        Assert.Null(receivedOriginal);
        Assert.NotNull(receivedNames);
        Assert.Equal(new[] { "Alpha" }, receivedNames);
    }

    private static MessageTemplatesViewModel CreateViewModel(
        IMessageTemplateService? templateService = null,
        IConfirmationDialogService? confirmationDialogService = null,
        INavigationService? navigationService = null,
        IMessageTemplateEditViewModelFactory? editViewModelFactory = null)
    {
        return new MessageTemplatesViewModel(
            templateService ?? Substitute.For<IMessageTemplateService>(),
            confirmationDialogService ?? Substitute.For<IConfirmationDialogService>(),
            navigationService ?? Substitute.For<INavigationService>(),
            editViewModelFactory ?? Substitute.For<IMessageTemplateEditViewModelFactory>());
    }
}
