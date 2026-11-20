// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Objects;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using Geo;
using NSubstitute;
using SkiaSharp;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

public sealed class ObjectEditorViewModelTests : TestFixtureBase
{
    [Fact]
    public void Initialize_Null_SetsAddMode()
    {
        var vm = CreateViewModel();

        vm.Initialize(null);

        Assert.False(vm.IsEditing);
        Assert.Equal("Add Object", vm.Title);
        Assert.Equal(string.Empty, vm.Name);
        Assert.Equal(string.Empty, vm.LatitudeText);
        Assert.Equal(string.Empty, vm.LongitudeText);
        Assert.Null(vm.Comment);
        Assert.Equal("/", vm.SymbolPicker.TableCharacter);
        Assert.Equal("[", vm.SymbolPicker.CodeCharacter);
    }

    [Fact]
    public void Initialize_Existing_SetsEditModeAndPopulatesFields()
    {
        var existing = new ObjectPacket
        {
            Name = "SHELTER1",
            IsLive = true,
            Location = new Coordinate(38.7223, -9.1393),
            Symbol = new Symbol(SymbolTable.Alternate, SymbolCode.GreaterThanSign),
            Comment = "Shelter"
        };

        var vm = CreateViewModel();
        vm.Initialize(existing);

        Assert.True(vm.IsEditing);
        Assert.Equal("Edit Object", vm.Title);
        Assert.Equal("SHELTER1", vm.Name);
        Assert.Equal("38.722300", vm.LatitudeText);
        Assert.Equal("-9.139300", vm.LongitudeText);
        Assert.Equal("Shelter", vm.Comment);
        Assert.Equal("\\", vm.SymbolPicker.TableCharacter);
        Assert.Equal(">", vm.SymbolPicker.CodeCharacter);
    }

    [Fact]
    public async Task Save_EmptyName_SetsError()
    {
        var objectService = Substitute.For<IObjectService>();
        var vm = CreateViewModel(objectService);
        vm.Initialize(null);
        vm.Name = "   ";
        vm.LatitudeText = "38.7223";
        vm.LongitudeText = "-9.1393";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Object name is required", vm.ErrorMessage);
        await objectService.DidNotReceive().CreateOrUpdateAsync(
            Arg.Any<string>(), Arg.Any<Coordinate>(), Arg.Any<SymbolTable>(),
            Arg.Any<SymbolCode>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_NameTooLong_SetsError()
    {
        var objectService = Substitute.For<IObjectService>();
        var vm = CreateViewModel(objectService);
        vm.Initialize(null);
        vm.Name = "TOOLONGNAME";
        vm.LatitudeText = "38.7223";
        vm.LongitudeText = "-9.1393";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Object name must be 9 characters or fewer", vm.ErrorMessage);
        await objectService.DidNotReceive().CreateOrUpdateAsync(
            Arg.Any<string>(), Arg.Any<Coordinate>(), Arg.Any<SymbolTable>(),
            Arg.Any<SymbolCode>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_InvalidLatitude_SetsError()
    {
        var objectService = Substitute.For<IObjectService>();
        var vm = CreateViewModel(objectService);
        vm.Initialize(null);
        vm.Name = "SHELTER1";
        vm.LatitudeText = "abc";
        vm.LongitudeText = "-9.1393";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Enter a valid latitude", vm.ErrorMessage);
        await objectService.DidNotReceive().CreateOrUpdateAsync(
            Arg.Any<string>(), Arg.Any<Coordinate>(), Arg.Any<SymbolTable>(),
            Arg.Any<SymbolCode>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_InvalidLongitude_SetsError()
    {
        var objectService = Substitute.For<IObjectService>();
        var vm = CreateViewModel(objectService);
        vm.Initialize(null);
        vm.Name = "SHELTER1";
        vm.LatitudeText = "38.7223";
        vm.LongitudeText = "def";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Enter a valid longitude", vm.ErrorMessage);
        await objectService.DidNotReceive().CreateOrUpdateAsync(
            Arg.Any<string>(), Arg.Any<Coordinate>(), Arg.Any<SymbolTable>(),
            Arg.Any<SymbolCode>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_ValidInput_CallsCreateOrUpdateAndGoesBack()
    {
        var objectService = Substitute.For<IObjectService>();
        var navigation = Substitute.For<INavigationService>();
        var vm = CreateViewModel(objectService, navigation);
        vm.Initialize(null);
        vm.Name = "SHELTER1";
        vm.LatitudeText = "38.7223";
        vm.LongitudeText = "-9.1393";
        vm.Comment = "Shelter";

        await vm.SaveCommand.ExecuteAsync(null);

        await objectService.Received(1).CreateOrUpdateAsync(
            "SHELTER1",
            Arg.Is<Coordinate>(c => c.Latitude == 38.7223 && c.Longitude == -9.1393),
            SymbolTable.Primary,
            SymbolCode.LeftSquareBracket,
            "Shelter",
            Arg.Any<CancellationToken>());
        navigation.Received(1).GoBack();
    }

    [Fact]
    public async Task Save_KillObject_CallsKillNotCreate()
    {
        var objectService = Substitute.For<IObjectService>();
        var navigation = Substitute.For<INavigationService>();
        var vm = CreateViewModel(objectService, navigation);
        vm.Initialize(new ObjectPacket
        {
            Name = "SHELTER1",
            IsLive = true,
            Location = new Coordinate(38.7223, -9.1393),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.LeftSquareBracket)
        });
        vm.KillObject = true;

        await vm.SaveCommand.ExecuteAsync(null);

        await objectService.Received(1).KillAsync("SHELTER1", Arg.Any<CancellationToken>());
        await objectService.DidNotReceive().CreateOrUpdateAsync(
            Arg.Any<string>(), Arg.Any<Coordinate>(), Arg.Any<SymbolTable>(),
            Arg.Any<SymbolCode>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        navigation.Received(1).GoBack();
    }

    [Fact]
    public void Cancel_CallsNavigationGoBack()
    {
        var navigation = Substitute.For<INavigationService>();
        var vm = CreateViewModel(navigationService: navigation);

        vm.CancelCommand.Execute(null);

        navigation.Received(1).GoBack();
    }

    private static ObjectEditorViewModel CreateViewModel(
        IObjectService? objectService = null,
        INavigationService? navigationService = null)
    {
        return new ObjectEditorViewModel(
            objectService ?? Substitute.For<IObjectService>(),
            navigationService ?? Substitute.For<INavigationService>(),
            new AprsSymbolPickerViewModel(new TestSymbolBitmapProvider()));
    }

    private sealed class TestSymbolBitmapProvider : IAprsSymbolBitmapProvider
    {
        public SKBitmap GetSymbolBitmap(Symbol symbol) => new(64, 64);
        public SKBitmap GetOverlayBitmap(SymbolCode overlayChar) => new(64, 64);
        public void Dispose() { }
    }
}
