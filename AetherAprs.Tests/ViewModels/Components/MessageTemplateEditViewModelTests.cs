// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using AetherAprs.Configuration.Settings;
using AetherAprs.ViewModels.Components;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Components;

public sealed class MessageTemplateEditViewModelTests
{
    [Fact]
    public void Initialize_AddMode_PrefillsEmptyAndSetsIsAddMode()
    {
        var vm = CreateViewModel(null, ["Existing"]);

        Assert.True(vm.IsAddMode);
        Assert.Equal(string.Empty, vm.Name);
        Assert.Equal(string.Empty, vm.Destination);
        Assert.Equal(string.Empty, vm.Body);
    }

    [Fact]
    public void Initialize_EditMode_PrefillsFromOriginal()
    {
        var original = new MessageTemplate { Name = "SOTA", Destination = "SOTA", Body = "{summit}" };

        var vm = CreateViewModel(original, ["SOTA"]);

        Assert.False(vm.IsAddMode);
        Assert.Equal("SOTA", vm.Name);
        Assert.Equal("SOTA", vm.Destination);
        Assert.Equal("{summit}", vm.Body);
    }

    [Fact]
    public void BlankName_IsInvalid()
    {
        var vm = CreateViewModel();

        vm.Name = "   ";

        Assert.False(vm.CanSave);
        Assert.NotNull(vm.NameError);
    }

    [Fact]
    public void DuplicateName_DifferentCase_IsInvalid()
    {
        var vm = CreateViewModel(null, ["Alpha"]);

        vm.Name = "ALPHA";

        Assert.False(vm.CanSave);
        Assert.NotNull(vm.NameError);
    }

    [Fact]
    public void DuplicateName_TrimsBeforeComparing_IsInvalid()
    {
        var vm = CreateViewModel(null, ["Alpha"]);

        vm.Name = " Alpha ";

        Assert.False(vm.CanSave);
        Assert.NotNull(vm.NameError);
    }

    [Fact]
    public void NameMatchingOriginal_IsValid()
    {
        var original = new MessageTemplate { Name = "Alpha", Body = "Hello" };

        var vm = CreateViewModel(original, ["Alpha", "Beta"]);
        vm.Name = "ALPHA";

        Assert.True(vm.CanSave);
        Assert.Null(vm.NameError);
    }

    [Fact]
    public void InvalidDestination_IsInvalid()
    {
        var vm = CreateViewModel();
        vm.Name = "A";

        vm.Destination = "!!!";

        Assert.False(vm.CanSave);
        Assert.NotNull(vm.DestinationError);
    }

    [Fact]
    public void BlankDestination_IsValid()
    {
        var vm = CreateViewModel();
        vm.Name = "A";
        vm.Body = "Hello";

        vm.Destination = "   ";

        Assert.True(vm.CanSave);
        Assert.Null(vm.DestinationError);
    }

    [Fact]
    public void ValidDestination_IsValid()
    {
        var vm = CreateViewModel();
        vm.Name = "A";
        vm.Body = "Hello";

        vm.Destination = " K0PEER-1 ";

        Assert.True(vm.CanSave);
        Assert.Null(vm.DestinationError);
    }

    [Fact]
    public void Body_ExactlySixtySevenCharacters_IsValid()
    {
        var vm = CreateViewModel();
        vm.Name = "A";

        vm.Body = new string('A', 67);

        Assert.True(vm.CanSave);
        Assert.Null(vm.BodyError);
    }

    [Fact]
    public void Body_SixtyEightCharacters_IsInvalid()
    {
        var vm = CreateViewModel();
        vm.Name = "A";

        vm.Body = new string('A', 68);

        Assert.False(vm.CanSave);
        Assert.NotNull(vm.BodyError);
    }

    [Fact]
    public void CanSave_WithAllFieldsValid_IsTrue()
    {
        var vm = CreateViewModel(null, ["Other"]);

        vm.Name = "A";
        vm.Destination = "K0PEER-1";
        vm.Body = "Hello";

        Assert.True(vm.CanSave);
    }

    [Fact]
    public void BuildTemplate_TrimsValues()
    {
        var vm = CreateViewModel();
        vm.Name = " Name ";
        vm.Destination = " K0PEER-1 ";
        vm.Body = " Hello ";

        var template = vm.BuildTemplate();

        Assert.Equal("Name", template.Name);
        Assert.Equal("K0PEER-1", template.Destination);
        Assert.Equal("Hello", template.Body);
    }

    [Fact]
    public void Save_WithValidInput_SetsResultAndRaisesCloseRequested()
    {
        var vm = CreateViewModel();
        vm.Name = "A";
        vm.Body = "Hello";

        var raiseCount = 0;
        object? closeSender = null;
        vm.CloseRequested += (sender, _) =>
        {
            raiseCount++;
            closeSender = sender;
        };

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.Equal("A", vm.Result.Name);
        Assert.Equal("Hello", vm.Result.Body);
        Assert.Equal(1, raiseCount);
        Assert.Same(vm, closeSender);
    }

    [Fact]
    public void Save_WithInvalidInput_DoesNotSetResultOrRaiseCloseRequested()
    {
        var vm = CreateViewModel();

        var raiseCount = 0;
        vm.CloseRequested += (_, _) => raiseCount++;

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Result);
        Assert.Equal(0, raiseCount);
    }

    private static MessageTemplateEditViewModel CreateViewModel(
        MessageTemplate? original = null,
        IReadOnlyList<string>? existingNames = null)
    {
        var vm = new MessageTemplateEditViewModel();
        vm.Initialize(original, existingNames ?? []);
        return vm;
    }
}
