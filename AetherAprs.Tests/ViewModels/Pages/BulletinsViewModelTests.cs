// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels.Pages;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

public sealed class BulletinsViewModelTests : TestFixtureBase
{
    [Fact]
    public async Task RefreshAsync_CopiesBulletinsFromService()
    {
        List<BulletinPacket> bulletins =
        [
            new() { Name = "BLN0SPCL", Text = "Special event", Source = new Callsign("N0CALL") },
            new() { Name = "BLN0WX", Text = "Weather", Source = new Callsign("K0PEER") }
        ];
        IBulletinService bulletinService = Substitute.For<IBulletinService>();
        bulletinService.GetBulletinsAsync(Arg.Any<CancellationToken>()).Returns(bulletins);

        BulletinsViewModel vm = CreateViewModel(bulletinService: bulletinService);
        await vm.RefreshAsync();

        Assert.Equal(2, vm.Bulletins.Count);
        Assert.Equal("BLN0SPCL", vm.Bulletins[0].Name);
        Assert.Equal("BLN0WX", vm.Bulletins[1].Name);
    }

    [Fact]
    public async Task Send_EmptyName_SetsError()
    {
        IBulletinService bulletinService = Substitute.For<IBulletinService>();
        BulletinsViewModel vm = CreateViewModel(bulletinService: bulletinService);
        vm.Name = "   ";
        vm.Text = "Hello";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal("Bulletin name is required", vm.ErrorMessage);
        await bulletinService.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_NameNotStartingWithBln_SetsError()
    {
        IBulletinService bulletinService = Substitute.For<IBulletinService>();
        BulletinsViewModel vm = CreateViewModel(bulletinService: bulletinService);
        vm.Name = "SPCL";
        vm.Text = "Hello";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal("Bulletin name must start with 'BLN'", vm.ErrorMessage);
        await bulletinService.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_NameTooLong_SetsError()
    {
        IBulletinService bulletinService = Substitute.For<IBulletinService>();
        BulletinsViewModel vm = CreateViewModel(bulletinService: bulletinService);
        vm.Name = "BLNTOOLONG";
        vm.Text = "Hello";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal("Bulletin name must be 9 characters or fewer", vm.ErrorMessage);
        await bulletinService.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_EmptyText_SetsError()
    {
        IBulletinService bulletinService = Substitute.For<IBulletinService>();
        BulletinsViewModel vm = CreateViewModel(bulletinService: bulletinService);
        vm.Name = "BLN0SPCL";
        vm.Text = "  ";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal("Bulletin text is required", vm.ErrorMessage);
        await bulletinService.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_TextTooLong_SetsError()
    {
        IBulletinService bulletinService = Substitute.For<IBulletinService>();
        BulletinsViewModel vm = CreateViewModel(bulletinService: bulletinService);
        vm.Name = "BLN0SPCL";
        vm.Text = new string('x', 68);

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal("Bulletin text must be 67 characters or fewer", vm.ErrorMessage);
        await bulletinService.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_EmptyThenPopulated_TogglesEmptyStateFlags()
    {
        List<BulletinPacket> backing = [];
        IBulletinService bulletinService = Substitute.For<IBulletinService>();
        bulletinService.GetBulletinsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => [.. backing]);

        BulletinsViewModel vm = CreateViewModel(bulletinService: bulletinService);

        await vm.RefreshAsync();
        Assert.False(vm.HasBulletins);
        Assert.True(vm.IsBulletinsEmpty);

        backing.Add(new BulletinPacket { Name = "BLN0SPCL", Text = "Special event" });
        await vm.RefreshAsync();
        Assert.True(vm.HasBulletins);
        Assert.False(vm.IsBulletinsEmpty);
    }

    [Fact]
    public async Task Send_ValidInput_CallsServiceAndClearsText()
    {
        IBulletinService bulletinService = Substitute.For<IBulletinService>();
        bulletinService.GetBulletinsAsync(Arg.Any<CancellationToken>()).Returns([]);
        BulletinsViewModel vm = CreateViewModel(bulletinService: bulletinService);
        vm.Name = "BLN0SPCL";
        vm.Text = "Special event";

        await vm.SendCommand.ExecuteAsync(null);

        await bulletinService.Received(1).SendAsync(
            "BLN0SPCL", "Special event", Arg.Any<CancellationToken>());
        Assert.Equal(string.Empty, vm.Text);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Send_ServiceThrows_SetsErrorMessage()
    {
        IBulletinService bulletinService = Substitute.For<IBulletinService>();
        bulletinService
            .When(s => s.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new System.InvalidOperationException("No TX ports"));
        BulletinsViewModel vm = CreateViewModel(bulletinService: bulletinService);
        vm.Name = "BLN0SPCL";
        vm.Text = "Special event";

        await vm.SendCommand.ExecuteAsync(null);

        // The view model must not echo the provider text; it shows the localized
        // category the operator can act on.
        Assert.Equal(AetherAprs.Localization.Strings.Get("FailureUnavailable"), vm.ErrorMessage);
        Assert.DoesNotContain("No TX ports", vm.ErrorMessage);
    }

    [Fact]
    public async Task Send_NoTransmitRoute_NamesTheMissingRoute()
    {
        IBulletinService bulletinService = Substitute.For<IBulletinService>();
        bulletinService
            .When(s => s.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new AetherAprs.Models.Objects.BroadcastFailedException(
                AetherAprs.Models.Objects.BroadcastFailure.NoTransmitRoute));
        BulletinsViewModel vm = CreateViewModel(bulletinService: bulletinService);
        vm.Name = "BLN0SPCL";
        vm.Text = "Special event";

        await vm.SendCommand.ExecuteAsync(null);

        // A typed reason is more specific than the generic category, so the operator
        // is told exactly which configuration is missing.
        Assert.Equal(AetherAprs.Localization.Strings.Get("FailureNoTransmitRoute"), vm.ErrorMessage);
    }

    [Fact]
    public void Close_CallsNavigationGoBack()
    {
        INavigationService navigation = Substitute.For<INavigationService>();
        BulletinsViewModel vm = CreateViewModel(navigationService: navigation);

        vm.CloseCommand.Execute(null);

        navigation.Received(1).GoBack();
    }

    private static BulletinsViewModel CreateViewModel(
        IBulletinService? bulletinService = null,
        INavigationService? navigationService = null) => new(
            bulletinService ?? Substitute.For<IBulletinService>(),
            navigationService ?? Substitute.For<INavigationService>());
}
