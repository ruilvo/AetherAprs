// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Services.Ports;
using AetherAprs.ViewModels.Components;
using Xunit;
using Xunit.Sdk;

namespace AetherAprs.Tests.ViewModels.Components;

public sealed class PortItemViewModelTests : TestFixtureBase
{
    [Fact]
    public void Constructor_NullConfig_Throws() => Assert.Throws<ArgumentNullException>(() =>
                                                            new PortItemViewModel(null!, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => { }, logger: null));

    [Fact]
    public void TypeName_MapsAprsIs()
    {
        PortItemViewModel vm = Create(new PortConfig { Name = "Port", TypeSettings = new AprsIsSettings() });
        Assert.Equal("APRS-IS", vm.TypeName);
    }

    [Fact]
    public void TypeName_MapsKiss()
    {
        PortItemViewModel vm = Create(new PortConfig { Name = "Port", TypeSettings = new KissSettings() });
        Assert.Equal("KISS", vm.TypeName);
    }

    [Fact]
    public void TypeName_MapsAfsk()
    {
        PortItemViewModel vm = Create(new PortConfig { Name = "Port", TypeSettings = new AfskSettings() });
        Assert.Equal("AFSK", vm.TypeName);
    }

    [Fact]
    public void TypeName_UnknownWhenNull()
    {
        PortItemViewModel vm = Create(new PortConfig { Name = "Port", TypeSettings = null });
        Assert.Equal("Unknown", vm.TypeName);
    }

    [Fact]
    public void StatusText_TracksIsEnabled()
    {
        PortItemViewModel vm = Create(new PortConfig { Name = "Port", IsEnabled = false });

        Assert.Equal(Localization.Strings.Get("Stopped"), vm.StatusText);

        vm.IsEnabled = true;

        Assert.Equal(Localization.Strings.Get("Running"), vm.StatusText);
    }

    [Fact]
    public void StatusText_DistinguishesAConnectedPortFromOneWaitingToRetry()
    {
        PortConfig config = new() { Name = "Port", IsEnabled = true };
        PortItemViewModel vm = Create(config);

        vm.ApplySessionStatus(new PortSessionStatus(config.Id, PortSessionState.Connected));
        Assert.Equal(Localization.Strings.Get("PortStatusConnected"), vm.StatusText);
        Assert.Equal(0, vm.RetryCount);
        Assert.Null(vm.NextRetryUtc);

        DateTimeOffset nextRetry = DateTimeOffset.UtcNow.AddSeconds(30);
        vm.ApplySessionStatus(new PortSessionStatus(
            config.Id,
            PortSessionState.Stopped,
            RetryCount: 2,
            NextRetryUtc: nextRetry));

        // A port sitting in backoff must not read as connected.
        Assert.Equal(Localization.Strings.Format("PortStatusWaitingToRetry", 2), vm.StatusText);
        Assert.Equal(2, vm.RetryCount);
        Assert.Equal(nextRetry, vm.NextRetryUtc);
    }

    [Fact]
    public void StatusText_PrefersIntentWhenThePortIsDisabled()
    {
        PortConfig config = new() { Name = "Port", IsEnabled = false };
        PortItemViewModel vm = Create(config);
        vm.ApplySessionStatus(new PortSessionStatus(config.Id, PortSessionState.Connected));

        Assert.Equal(Localization.Strings.Get("Stopped"), vm.StatusText);
    }

    [Fact]
    public async Task PropertyChanges_InvokeCallbacks()
    {
        PortItemViewModel? toggled = null;
        PortItemViewModel? mapToggled = null;
        TaskCompletionSource<PortItemViewModel> toggledTcs = new();
        TaskCompletionSource<PortItemViewModel> mapToggledTcs = new();
        PortConfig config = new() { Name = "Port", IsEnabled = false, ShowOnMap = false };

        PortItemViewModel vm = new(
            config,
            item => { toggled = item; toggledTcs.SetResult(item); return Task.CompletedTask; },
            item => { mapToggled = item; mapToggledTcs.SetResult(item); return Task.CompletedTask; },
            _ => Task.CompletedTask,
            _ => { },
            logger: null)
        {
            IsEnabled = true,
            ShowOnMap = true
        };

        await toggledTcs.Task.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);
        await mapToggledTcs.Task.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.Same(vm, toggled);
        Assert.Same(vm, mapToggled);
    }

    [Fact]
    public async Task DeleteAndEdit_InvokeCallbacks()
    {
        PortItemViewModel? deleted = null;
        PortItemViewModel? edited = null;
        PortItemViewModel vm = new(
            new PortConfig { Name = "Port" },
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            item => { deleted = item; return Task.CompletedTask; },
            item => edited = item,
            logger: null);

        await vm.DeleteCommand.ExecuteAsync(null);
        vm.EditCommand.Execute(null);

        Assert.Same(vm, deleted);
        Assert.Same(vm, edited);
    }

    [Fact]
    public void BuildConfig_ReturnsIndependentCopyWithCurrentFlags()
    {
        PortConfig config = new() { Name = "Port", IsEnabled = false, ShowOnMap = false };
        PortItemViewModel vm = Create(config);

        vm.IsEnabled = true;
        vm.ShowOnMap = true;
        PortConfig built = vm.BuildConfig();

        // The item holds a snapshot of service state; building an edit config
        // must not mutate it, and the returned copy must not alias it either.
        Assert.NotSame(config, built);
        Assert.Equal(config.Id, built.Id);
        Assert.True(built.IsEnabled);
        Assert.True(built.ShowOnMap);
        Assert.False(config.IsEnabled);
        Assert.False(config.ShowOnMap);

        built.IsEnabled = false;
        Assert.True(vm.IsEnabled);
    }

    [Fact]
    public void Constructor_DoesNotInvokeCallbacksDuringInitialization()
    {
        // This test prevents the infinite loop bug where setting properties
        // during construction triggered callbacks that reloaded the ports list
        int toggleCount = 0;
        int mapToggleCount = 0;

        PortConfig config = new()
        {
            Name = "Port",
            IsEnabled = true,  // Setting to true during construction
            ShowOnMap = true   // Setting to true during construction
        };

        PortItemViewModel vm = new(
            config,
            _ => { toggleCount++; return Task.CompletedTask; },
            _ => { mapToggleCount++; return Task.CompletedTask; },
            _ => Task.CompletedTask,
            _ => { },
            logger: null);

        // Callbacks should NOT have been invoked during construction
        Assert.Equal(0, toggleCount);
        Assert.Equal(0, mapToggleCount);

        // But properties should be set correctly
        Assert.True(vm.IsEnabled);
        Assert.True(vm.ShowOnMap);
    }

    [Fact]
    public async Task PropertyChanges_AfterInitialization_InvokeCallbacks()
    {
        // This test ensures callbacks ARE invoked after initialization
        int toggleCount = 0;
        int mapToggleCount = 0;
        TaskCompletionSource<bool> toggleTcs = new();
        TaskCompletionSource<bool> mapToggleTcs = new();
        PortConfig config = new() { Name = "Port", IsEnabled = false, ShowOnMap = false };

        PortItemViewModel vm = new(
            config,
            _ => { toggleCount++; toggleTcs.TrySetResult(true); return Task.CompletedTask; },
            _ => { mapToggleCount++; mapToggleTcs.TrySetResult(true); return Task.CompletedTask; },
            _ => Task.CompletedTask,
            _ => { },
            logger: null);

        // Verify no callbacks during construction
        Assert.Equal(0, toggleCount);
        Assert.Equal(0, mapToggleCount);

        // Now change properties - callbacks SHOULD be invoked
        vm.IsEnabled = true;
        await toggleTcs.Task.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);
        Assert.Equal(1, toggleCount);
        Assert.Equal(0, mapToggleCount);

        vm.ShowOnMap = true;
        await mapToggleTcs.Task.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);
        Assert.Equal(1, toggleCount);
        Assert.Equal(1, mapToggleCount);
    }

    private static PortItemViewModel Create(PortConfig config) =>
        new(config, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => { }, logger: null);
}
