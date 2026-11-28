// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Factories.ViewModels;
using AetherAprs.Imaging;
using AetherAprs.Localization;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Models.Objects;
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
        List<ObjectPacket> objects =
        [
            new() { Name = "SHELTER1", IsLive = true },
            new() { Name = "SHELTER2", IsLive = false }
        ];
        IObjectService objectService = Substitute.For<IObjectService>();
        objectService.GetObjectsAsync(Arg.Any<CancellationToken>()).Returns(objects);

        ObjectsViewModel vm = CreateViewModel(objectService: objectService);
        await vm.RefreshAsync();

        Assert.Equal(2, vm.Objects.Count);
        Assert.Equal("SHELTER1", vm.Objects[0].Name);
        Assert.Equal("SHELTER2", vm.Objects[1].Name);
    }

    [Fact]
    public async Task RefreshAsync_ReplacesPreviouslyLoadedObjects()
    {
        List<ObjectPacket> backing = [new() { Name = "A" }];
        IObjectService objectService = Substitute.For<IObjectService>();
        objectService.GetObjectsAsync(Arg.Any<CancellationToken>()).Returns(backing);

        ObjectsViewModel vm = CreateViewModel(objectService: objectService);
        await vm.RefreshAsync();
        backing.Clear();
        backing.Add(new ObjectPacket { Name = "B" });
        await vm.RefreshAsync();

        ObjectPacket item = Assert.Single(vm.Objects);
        Assert.Equal("B", item.Name);
    }

    [Fact]
    public async Task RefreshAsync_EmptyThenPopulated_TogglesEmptyStateFlags()
    {
        List<ObjectPacket> backing = [];
        IObjectService objectService = Substitute.For<IObjectService>();
        objectService.GetObjectsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => [.. backing]);

        ObjectsViewModel vm = CreateViewModel(objectService: objectService);

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
        List<ObjectPacket> backing = [new() { Name = "SHELTER1" }];
        IObjectService objectService = Substitute.For<IObjectService>();
        objectService.GetObjectsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => [.. backing]);

        ObjectsViewModel vm = CreateViewModel(objectService: objectService);

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
        IObjectService objectService = Substitute.For<IObjectService>();
        objectService.GetObjectsAsync(Arg.Any<CancellationToken>())
            .Returns([new() { Name = "SHELTER1" }]);

        ObjectsViewModel vm = CreateViewModel(objectService: objectService);
        List<string> raised = [];
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        await vm.RefreshAsync();

        Assert.Contains(nameof(ObjectsViewModel.HasObjects), raised);
        Assert.Contains(nameof(ObjectsViewModel.IsObjectsEmpty), raised);
    }

    [Fact]
    public void Add_NavigatesToNewEditor()
    {
        INavigationService navigation = Substitute.For<INavigationService>();
        IObjectEditorViewModelFactory factory = Substitute.For<IObjectEditorViewModelFactory>();
        ObjectEditorViewModel editor = CreateEditor(navigation);
        factory.Create().Returns(editor);

        ObjectsViewModel vm = CreateViewModel(navigationService: navigation, editorFactory: factory);

        vm.AddCommand.Execute(null);

        factory.Received(1).Create();
        navigation.Received(1).NavigateTo(editor);
    }

    [Fact]
    public void Edit_Null_DoesNothing()
    {
        INavigationService navigation = Substitute.For<INavigationService>();
        IObjectEditorViewModelFactory factory = Substitute.For<IObjectEditorViewModelFactory>();

        ObjectsViewModel vm = CreateViewModel(navigationService: navigation, editorFactory: factory);

        vm.EditCommand.Execute(null);

        factory.DidNotReceive().Create(Arg.Any<ObjectPacket?>());
        navigation.DidNotReceive().NavigateTo(Arg.Any<AetherAprs.ViewModels.ViewModelBase>());
    }

    [Fact]
    public void Edit_NavigatesToInitializedEditor()
    {
        INavigationService navigation = Substitute.For<INavigationService>();
        IObjectEditorViewModelFactory factory = Substitute.For<IObjectEditorViewModelFactory>();
        ObjectEditorViewModel editor = CreateEditor(navigation);
        ObjectPacket existing = new() { Name = "SHELTER1", IsLive = true };
        factory.Create(existing).Returns(editor);

        ObjectsViewModel vm = CreateViewModel(navigationService: navigation, editorFactory: factory);

        vm.EditCommand.Execute(existing);

        factory.Received(1).Create(existing);
        navigation.Received(1).NavigateTo(editor);
    }

    [Fact]
    public async Task Kill_WithoutConfirmation_DoesNotCallService()
    {
        IObjectService objectService = Substitute.For<IObjectService>();
        IConfirmationDialogService confirmation = Substitute.For<IConfirmationDialogService>();
        confirmation.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        ObjectsViewModel vm = CreateViewModel(objectService: objectService, confirmationDialogService: confirmation);

        await vm.KillCommand.ExecuteAsync(new ObjectPacket { Name = "SHELTER1" });

        await objectService.DidNotReceive().KillAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Kill_WithConfirmation_CallsService()
    {
        IObjectService objectService = Substitute.For<IObjectService>();
        objectService.GetObjectsAsync(Arg.Any<CancellationToken>()).Returns([]);
        IConfirmationDialogService confirmation = Substitute.For<IConfirmationDialogService>();
        confirmation.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        ObjectsViewModel vm = CreateViewModel(objectService: objectService, confirmationDialogService: confirmation);

        await vm.KillCommand.ExecuteAsync(new ObjectPacket { Name = "SHELTER1" });

        await objectService.Received(1).KillAsync("SHELTER1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Kill_WhenTheServiceRejectsTheBroadcast_ShowsTheReason()
    {
        IObjectService objectService = Substitute.For<IObjectService>();
        objectService
            .When(s => s.KillAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new BroadcastFailedException(BroadcastFailure.NoTransmitRoute));
        IConfirmationDialogService confirmation = Substitute.For<IConfirmationDialogService>();
        confirmation.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        ObjectsViewModel vm = CreateViewModel(objectService: objectService, confirmationDialogService: confirmation);
        Assert.Equal(string.Empty, vm.ErrorMessage);

        await vm.KillCommand.ExecuteAsync(new ObjectPacket { Name = "SHELTER1" });

        // A failed kill used to be logged and swallowed, leaving the operator with no
        // feedback and the object silently still live.
        Assert.Equal(Strings.Get("FailureNoTransmitRoute"), vm.ErrorMessage);
    }

    [Fact]
    public void Close_CallsNavigationGoBack()
    {
        INavigationService navigation = Substitute.For<INavigationService>();
        ObjectsViewModel vm = CreateViewModel(navigationService: navigation);

        vm.CloseCommand.Execute(null);

        navigation.Received(1).GoBack();
    }

    private static ObjectsViewModel CreateViewModel(
        IObjectService? objectService = null,
        INavigationService? navigationService = null,
        IObjectEditorViewModelFactory? editorFactory = null,
        IConfirmationDialogService? confirmationDialogService = null) => new(
            objectService ?? Substitute.For<IObjectService>(),
            navigationService ?? Substitute.For<INavigationService>(),
            editorFactory ?? Substitute.For<IObjectEditorViewModelFactory>(),
            confirmationDialogService ?? Substitute.For<IConfirmationDialogService>());

    private static ObjectEditorViewModel CreateEditor(INavigationService navigation) => new(
            Substitute.For<IObjectService>(),
            navigation,
            new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider()));

    private sealed class TestSymbolBitmapProvider : IAprsSymbolBitmapProvider
    {
        public SKBitmap GetSymbolBitmap(Symbol symbol) => new(64, 64);
        public SKBitmap GetOverlayBitmap(SymbolCode overlayChar) => new(64, 64);
        public void Dispose() { }
    }
}
