// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using AetherAprs.Configuration.Settings;
using AetherAprs.ViewModels.Components;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Components;

public sealed class MessageTemplateDialogViewModelTests
{
    [Fact]
    public void Initialize_PopulatesTemplates()
    {
        var vm = new MessageTemplateDialogViewModel();
        var templates = new List<MessageTemplate>
        {
            new() { Name = "SOTA spot", Body = "SOTA {summit}" },
            new() { Name = "Generic", Body = "Hello" }
        };

        vm.Initialize(templates, null);

        Assert.Same(templates, vm.Templates);
    }

    [Fact]
    public void SelectTemplate_PopulatesFieldsInFirstOccurrenceOrder()
    {
        var vm = new MessageTemplateDialogViewModel();
        var template = new MessageTemplate { Name = "T", Body = "{b} {a} {b}" };

        vm.Initialize([template], null);
        vm.SelectedTemplate = template;

        Assert.Equal(2, vm.Fields.Count);
        Assert.Equal("b", vm.Fields[0].Name);
        Assert.Equal("a", vm.Fields[1].Name);
        Assert.Equal(string.Empty, vm.Fields[0].Value);
    }

    [Fact]
    public void SelectTemplate_WithoutCallerDestination_PrefillsDestinationFromTemplate()
    {
        var vm = new MessageTemplateDialogViewModel();
        var template = new MessageTemplate { Name = "T", Destination = "W7SUM", Body = "Hello" };

        vm.Initialize([template], null);
        vm.SelectedTemplate = template;

        Assert.Equal("W7SUM", vm.Destination);
    }

    [Fact]
    public void SelectTemplate_WithCallerDestination_KeepsCallerDestination()
    {
        var vm = new MessageTemplateDialogViewModel();
        var template = new MessageTemplate { Name = "T", Destination = "W7SUM", Body = "Hello" };

        vm.Initialize([template], "K0PEER-1");
        vm.SelectedTemplate = template;

        Assert.Equal("K0PEER-1", vm.Destination);
    }

    [Fact]
    public void FieldEdit_UpdatesPreviewWithTrimmedCaseInsensitiveValues()
    {
        var vm = new MessageTemplateDialogViewModel();
        var template = new MessageTemplate { Name = "T", Body = "Summit {Summit} on {FREQ}" };

        vm.Initialize([template], null);
        vm.SelectedTemplate = template;
        vm.Fields[0].Value = " W7/ON-001 ";
        vm.Fields[1].Value = "432.100";

        Assert.Equal("Summit W7/ON-001 on 432.100", vm.Preview);
        Assert.True(vm.CanConfirm);
    }

    [Fact]
    public void FieldEdit_MissingOrBlankValue_BecomesEmptyString()
    {
        var vm = new MessageTemplateDialogViewModel();
        var template = new MessageTemplate { Name = "T", Body = "QTH {qth} de {call}" };

        vm.Initialize([template], null);
        vm.SelectedTemplate = template;
        vm.Fields[0].Value = "  ";

        Assert.Equal("QTH  de ", vm.Preview);
    }

    [Fact]
    public void CanConfirm_ExactlySixtySevenCharacters_IsTrue()
    {
        var vm = new MessageTemplateDialogViewModel();
        var template = new MessageTemplate { Name = "T", Body = new string('A', 60) + "{v}" };

        vm.Initialize([template], null);
        vm.SelectedTemplate = template;
        vm.Fields[0].Value = new string('B', 7);

        Assert.Equal(67, vm.Preview.Length);
        Assert.True(vm.CanConfirm);
    }

    [Fact]
    public void CanConfirm_SixtyEightCharacters_IsFalseAndPreviewEmpty()
    {
        var vm = new MessageTemplateDialogViewModel();
        var template = new MessageTemplate { Name = "T", Body = new string('A', 60) + "{v}" };

        vm.Initialize([template], null);
        vm.SelectedTemplate = template;
        vm.Fields[0].Value = new string('B', 8);

        Assert.False(vm.CanConfirm);
        Assert.Equal(string.Empty, vm.Preview);
    }

    [Fact]
    public void NoSelection_CanConfirmIsFalse()
    {
        var vm = new MessageTemplateDialogViewModel();
        var template = new MessageTemplate { Name = "T", Body = "Hello" };

        vm.Initialize([template], null);

        Assert.Null(vm.SelectedTemplate);
        Assert.False(vm.CanConfirm);
        Assert.Equal(string.Empty, vm.Preview);
    }

    [Fact]
    public void Confirm_SetsResultAndRaisesCloseRequested()
    {
        var vm = new MessageTemplateDialogViewModel();
        var template = new MessageTemplate { Name = "T", Body = "Summit {summit}" };
        vm.Initialize([template], null);
        vm.SelectedTemplate = template;
        vm.Fields[0].Value = " W7/ON-001 ";
        vm.Destination = "K0PEER-1";

        var closeCount = 0;
        object? closeSender = null;
        vm.CloseRequested += (sender, _) =>
        {
            closeCount++;
            closeSender = sender;
        };

        vm.ConfirmCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.Equal("Summit W7/ON-001", vm.Result.Text);
        Assert.Equal("K0PEER-1", vm.Result.Destination);
        Assert.Equal(1, closeCount);
        Assert.Same(vm, closeSender);
    }

    [Fact]
    public void Confirm_WithoutSelection_DoesNotSetResultOrRaiseCloseRequested()
    {
        var vm = new MessageTemplateDialogViewModel();
        vm.Initialize([new MessageTemplate { Name = "T", Body = "Hello" }], null);

        var closeCount = 0;
        vm.CloseRequested += (_, _) => closeCount++;

        vm.ConfirmCommand.Execute(null);

        Assert.Null(vm.Result);
        Assert.Equal(0, closeCount);
    }

    [Fact]
    public void Confirm_WithOverLimitMessage_DoesNotSetResultOrRaiseCloseRequested()
    {
        var vm = new MessageTemplateDialogViewModel();
        var template = new MessageTemplate { Name = "T", Body = new string('A', 60) + "{v}" };
        vm.Initialize([template], null);
        vm.SelectedTemplate = template;
        vm.Fields[0].Value = new string('B', 8);

        var closeCount = 0;
        vm.CloseRequested += (_, _) => closeCount++;

        vm.ConfirmCommand.Execute(null);

        Assert.Null(vm.Result);
        Assert.Equal(0, closeCount);
    }

    [Fact]
    public void ChangingSelection_UnsubscribesFromOldFieldNotifications()
    {
        var vm = new MessageTemplateDialogViewModel();
        var first = new MessageTemplate { Name = "First", Body = "A {x}" };
        var second = new MessageTemplate { Name = "Second", Body = "B {y}" };

        vm.Initialize([first, second], null);
        vm.SelectedTemplate = first;
        var staleField = vm.Fields[0];
        vm.SelectedTemplate = second;

        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        staleField.Value = "trigger";

        Assert.DoesNotContain(nameof(vm.Preview), raised);
        Assert.DoesNotContain(nameof(vm.CanConfirm), raised);
    }
}
