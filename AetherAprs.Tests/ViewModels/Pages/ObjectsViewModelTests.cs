// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Objects;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using NSubstitute;
using SkiaSharp;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

public sealed class ObjectsViewModelTests
{
    [Fact]
    public async Task RefreshAsync_CopiesObjectsFromService()
    {
        var objects = new List<ObjectPacket>
        {
            new() { Name = "SHELTER1", IsLive = true },
            new() { Name = "SHELTER2", IsLive = false }
        };
        var objectService = Substitute.For<IObjectService>();
        objectService.GetObjectsAsync(Arg.Any<CancellationToken>()).Returns(objects);

        var vm = CreateViewModel(objectService: objectService);
        await vm.RefreshAsync();

        Assert.Equal(2, vm.Objects.Count);
        Assert.Equal("SHELTER1", vm.Objects[0].Name);
        Assert.Equal("SHELTER2", vm.Objects[1].Name);
    }

    [Fact]
    public async Task RefreshAsync_ReplacesPreviouslyLoadedObjects()
    {
        var backing = new List<ObjectPacket> { new() { Name = "A" } };
        var objectService = Substitute.For<IObjectService>();
        objectService.GetObjectsAsync(Arg.Any<CancellationToken>()).Returns(backing);

        var vm = CreateViewModel(objectService: objectService);
        await vm.RefreshAsync();
        backing.Clear();
        backing.Add(new ObjectPacket { Name = "B" });
        await vm.RefreshAsync();

        Assert.Single(vm.Objects);
        Assert.Equal("B", vm.Objects[0].Name);
    }

    [Fact]
    public async Task RefreshAsync_EmptyThenPopulated_TogglesEmptyStateFlags()
    {
        var backing = new List<ObjectPacket>();
        var objectService = Substitute.For<IObjectService>();
        objectService.GetObjectsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new List<ObjectPacket>(backing));

        var vm = CreateViewModel(objectService: objectService);

        await vm.RefreshAsync();
        Assert.False(vm.HasObjects);
        Assert.True(vm.IsObjectsEmpty);

        backing.Add(new ObjectPacket { Name = "SHELTER1" });
        await vm.RefreshAsync();
        Assert.True(vm.HasObjects);
        Assert.False(vm.IsObjectsEmpty);
    }

    [Fact]
    public async Task RefreshAsync_PopulatedThenEmpty_TogglesEmptyStateFlags()
    {
        var backing = new List<ObjectPacket> { new() { Name = "SHELTER1" } };
        var objectService = Substitute.For<IObjectService>();
        objectService.GetObjectsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new List<ObjectPacket>(backing));

        var vm = CreateViewModel(objectService: objectService);

        await vm.RefreshAsync();
        Assert.True(vm.HasObjects);
        Assert.False(vm.IsObjectsEmpty);

        backing.Clear();
        await vm.RefreshAsync();
        Assert.False(vm.HasObjects);
        Assert.True(vm.IsObjectsEmpty);
    }

    [Fact]
    public async Task RefreshAsync_PopulatesObjects_RaisesStateFlagNotifications()
    {
        var objectService = Substitute.For<IObjectService>();
        objectService.GetObjectsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ObjectPacket> { new() { Name = "SHELTER1" } });

        var vm = CreateViewModel(objectService: objectService);
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        await vm.RefreshAsync();

        Assert.Contains(nameof(ObjectsViewModel.HasObjects), raised);
        Assert.Contains(nameof(ObjectsViewModel.IsObjectsEmpty), raised);
    }

    [Fact]
    public void Add_NavigatesToNewEditor()
    {
        var navigation = Substitute.For<INavigationService>();
        var factory = Substitute.For<IObjectEditorViewModelFactory>();
        var editor = CreateEditor(navigation);
        factory.Create().Returns(editor);

        var vm = CreateViewModel(navigationService: navigation, editorFactory: factory);

        vm.AddCommand.Execute(null);

        factory.Received(1).Create();
        navigation.Received(1).NavigateTo(editor);
    }

    [Fact]
    public void Edit_Null_DoesNothing()
    {
        var navigation = Substitute.For<INavigationService>();
        var factory = Substitute.For<IObjectEditorViewModelFactory>();

        var vm = CreateViewModel(navigationService: navigation, editorFactory: factory);

        vm.EditCommand.Execute(null);

        factory.DidNotReceive().Create(Arg.Any<ObjectPacket?>());
        navigation.DidNotReceive().NavigateTo(Arg.Any<AetherAprs.ViewModels.ViewModelBase>());
    }

    [Fact]
    public void Edit_NavigatesToInitializedEditor()
    {
        var navigation = Substitute.For<INavigationService>();
        var factory = Substitute.For<IObjectEditorViewModelFactory>();
        var editor = CreateEditor(navigation);
        var existing = new ObjectPacket { Name = "SHELTER1", IsLive = true };
        factory.Create(existing).Returns(editor);

        var vm = CreateViewModel(navigationService: navigation, editorFactory: factory);

        vm.EditCommand.Execute(existing);

        factory.Received(1).Create(existing);
        navigation.Received(1).NavigateTo(editor);
    }

    [Fact]
    public async Task Kill_WithoutConfirmation_DoesNotCallService()
    {
        var objectService = Substitute.For<IObjectService>();
        var confirmation = Substitute.For<IConfirmationDialogService>();
        confirmation.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var vm = CreateViewModel(objectService: objectService, confirmationDialogService: confirmation);

        await vm.KillCommand.ExecuteAsync(new ObjectPacket { Name = "SHELTER1" });

        await objectService.DidNotReceive().KillAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Kill_WithConfirmation_CallsService()
    {
        var objectService = Substitute.For<IObjectService>();
        objectService.GetObjectsAsync(Arg.Any<CancellationToken>()).Returns(new List<ObjectPacket>());
        var confirmation = Substitute.For<IConfirmationDialogService>();
        confirmation.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var vm = CreateViewModel(objectService: objectService, confirmationDialogService: confirmation);

        await vm.KillCommand.ExecuteAsync(new ObjectPacket { Name = "SHELTER1" });

        await objectService.Received(1).KillAsync("SHELTER1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Close_CallsNavigationGoBack()
    {
        var navigation = Substitute.For<INavigationService>();
        var vm = CreateViewModel(navigationService: navigation);

        vm.CloseCommand.Execute(null);

        navigation.Received(1).GoBack();
    }

    private static ObjectsViewModel CreateViewModel(
        IObjectService? objectService = null,
        INavigationService? navigationService = null,
        IObjectEditorViewModelFactory? editorFactory = null,
        IConfirmationDialogService? confirmationDialogService = null)
    {
        return new ObjectsViewModel(
            objectService ?? Substitute.For<IObjectService>(),
            navigationService ?? Substitute.For<INavigationService>(),
            editorFactory ?? Substitute.For<IObjectEditorViewModelFactory>(),
            confirmationDialogService ?? Substitute.For<IConfirmationDialogService>());
    }

    private static ObjectEditorViewModel CreateEditor(INavigationService navigation)
    {
        return new ObjectEditorViewModel(
            Substitute.For<IObjectService>(),
            navigation,
            new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider()));
    }

    private sealed class TestSymbolBitmapProvider : IAprsSymbolBitmapProvider
    {
        public SKBitmap GetSymbolBitmap(Symbol symbol) => new(64, 64);
        public SKBitmap GetOverlayBitmap(SymbolCode overlayChar) => new(64, 64);
        public void Dispose() { }
    }
}
