// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Services.Diagnostics;
using AetherAprs.Services.Maintenance;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels.Pages;

using NSubstitute;

using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

public sealed class SupportViewModelTests
{
    private const int RemovedRows = 7;

    private readonly ILocalDataMaintenanceService _maintenanceService =
        Substitute.For<ILocalDataMaintenanceService>();
    private readonly IConfirmationDialogService _confirmationDialogService =
        Substitute.For<IConfirmationDialogService>();
    private readonly ITransientFeedbackService _transientFeedbackService =
        Substitute.For<ITransientFeedbackService>();
    private readonly ISupportReportBuilder _supportReportBuilder =
        Substitute.For<ISupportReportBuilder>();
    private readonly INavigationService _navigationService =
        Substitute.For<INavigationService>();

    public SupportViewModelTests()
    {
        _confirmationDialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        _maintenanceService.ClearPacketHistoryAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new LocalDataClearResult(LocalDataCategory.PacketHistory, RemovedRows)));
        _maintenanceService.ClearMessagesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new LocalDataClearResult(LocalDataCategory.Messages, RemovedRows)));
        _maintenanceService.ClearPendingOutboundAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new LocalDataClearResult(LocalDataCategory.PendingOutbound, RemovedRows)));
        _maintenanceService.ClearOfflineMapTilesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new LocalDataClearResult(LocalDataCategory.OfflineMapTiles, RemovedRows)));
        _maintenanceService.ClearDiagnostics()
            .Returns(new LocalDataClearResult(LocalDataCategory.Diagnostics, RemovedRows));
    }

    [Fact]
    public async Task ClearPacketHistory_WhenConfirmed_ClearsOnlyPacketHistoryAndShowsCount()
    {
        SupportViewModel viewModel = CreateViewModel();

        await viewModel.ClearPacketHistoryCommand.ExecuteAsync(null);

        await _maintenanceService.Received(1).ClearPacketHistoryAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearMessagesAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearPendingOutboundAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearOfflineMapTilesAsync(Arg.Any<CancellationToken>());
        _maintenanceService.DidNotReceive().ClearDiagnostics();

        _transientFeedbackService.Received(1).Show(Arg.Is<string>(message => message.Contains('7')));
        Assert.Contains("7", viewModel.PacketHistoryStatus);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task ClearMessages_WhenConfirmed_ClearsOnlyMessagesAndShowsCount()
    {
        SupportViewModel viewModel = CreateViewModel();

        await viewModel.ClearMessagesCommand.ExecuteAsync(null);

        await _maintenanceService.Received(1).ClearMessagesAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearPacketHistoryAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearPendingOutboundAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearOfflineMapTilesAsync(Arg.Any<CancellationToken>());
        _maintenanceService.DidNotReceive().ClearDiagnostics();

        _transientFeedbackService.Received(1).Show(Arg.Is<string>(message => message.Contains('7')));
        Assert.Contains("7", viewModel.MessagesStatus);
    }

    [Fact]
    public async Task ClearPendingOutbound_WhenConfirmed_ClearsOnlyPendingOutboundAndShowsCount()
    {
        SupportViewModel viewModel = CreateViewModel();

        await viewModel.ClearPendingOutboundCommand.ExecuteAsync(null);

        await _maintenanceService.Received(1).ClearPendingOutboundAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearPacketHistoryAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearMessagesAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearOfflineMapTilesAsync(Arg.Any<CancellationToken>());
        _maintenanceService.DidNotReceive().ClearDiagnostics();

        _transientFeedbackService.Received(1).Show(Arg.Is<string>(message => message.Contains('7')));
        Assert.Contains("7", viewModel.PendingOutboundStatus);
    }

    [Fact]
    public async Task ClearOfflineMapTiles_WhenConfirmed_ClearsOnlyMapTilesAndShowsCount()
    {
        SupportViewModel viewModel = CreateViewModel();

        await viewModel.ClearOfflineMapTilesCommand.ExecuteAsync(null);

        await _maintenanceService.Received(1).ClearOfflineMapTilesAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearPacketHistoryAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearMessagesAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearPendingOutboundAsync(Arg.Any<CancellationToken>());
        _maintenanceService.DidNotReceive().ClearDiagnostics();

        _transientFeedbackService.Received(1).Show(Arg.Is<string>(message => message.Contains('7')));
        Assert.Contains("7", viewModel.OfflineMapTilesStatus);
    }

    [Fact]
    public async Task ClearDiagnostics_WhenConfirmed_ClearsOnlyDiagnosticsAndShowsCount()
    {
        SupportViewModel viewModel = CreateViewModel();

        await viewModel.ClearDiagnosticsCommand.ExecuteAsync(null);

        _maintenanceService.Received(1).ClearDiagnostics();
        await _maintenanceService.DidNotReceive().ClearPacketHistoryAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearMessagesAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearPendingOutboundAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearOfflineMapTilesAsync(Arg.Any<CancellationToken>());

        _transientFeedbackService.Received(1).Show(Arg.Is<string>(message => message.Contains('7')));
        Assert.Contains("7", viewModel.DiagnosticsStatus);
    }

    [Fact]
    public async Task Clear_WhenConfirmationIsCancelled_ClearsNothing()
    {
        _confirmationDialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(false));
        SupportViewModel viewModel = CreateViewModel();

        await viewModel.ClearPacketHistoryCommand.ExecuteAsync(null);
        await viewModel.ClearMessagesCommand.ExecuteAsync(null);
        await viewModel.ClearPendingOutboundCommand.ExecuteAsync(null);
        await viewModel.ClearOfflineMapTilesCommand.ExecuteAsync(null);
        await viewModel.ClearDiagnosticsCommand.ExecuteAsync(null);

        await _maintenanceService.DidNotReceive().ClearPacketHistoryAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearMessagesAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearPendingOutboundAsync(Arg.Any<CancellationToken>());
        await _maintenanceService.DidNotReceive().ClearOfflineMapTilesAsync(Arg.Any<CancellationToken>());
        _maintenanceService.DidNotReceive().ClearDiagnostics();

        _transientFeedbackService.DidNotReceive().Show(Arg.Any<string>());
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Clear_WhileBusy_DoesNotClearTwice()
    {
        TaskCompletionSource<LocalDataClearResult> pendingClear = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _maintenanceService.ClearPacketHistoryAsync(Arg.Any<CancellationToken>())
            .Returns(pendingClear.Task);
        SupportViewModel viewModel = CreateViewModel();

        Task firstClear = viewModel.ClearPacketHistoryCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsBusy);

        await viewModel.ClearPacketHistoryCommand.ExecuteAsync(null);

        await _maintenanceService.Received(1).ClearPacketHistoryAsync(Arg.Any<CancellationToken>());
        _transientFeedbackService.DidNotReceive().Show(Arg.Any<string>());

        pendingClear.SetResult(new LocalDataClearResult(LocalDataCategory.PacketHistory, RemovedRows));
        await firstClear;

        Assert.False(viewModel.IsBusy);
        _transientFeedbackService.Received(1).Show(Arg.Any<string>());
    }

    [Fact]
    public void GenerateReportPreview_FillsPreviewFromBuilderWithoutClearing()
    {
        const string report = "{\n  \"schemaVersion\": 1\n}";
        _supportReportBuilder.GenerateJson().Returns(report);
        SupportViewModel viewModel = CreateViewModel();

        viewModel.GenerateReportPreviewCommand.Execute(null);

        Assert.Equal(report, viewModel.ReportPreview);
        _supportReportBuilder.Received(1).GenerateJson();
        _maintenanceService.DidNotReceive().ClearDiagnostics();
        _transientFeedbackService.DidNotReceive().Show(Arg.Any<string>());
    }

    [Fact]
    public void Close_NavigatesBack()
    {
        SupportViewModel viewModel = CreateViewModel();

        viewModel.CloseCommand.Execute(null);

        _navigationService.Received(1).GoBack();
    }

    private SupportViewModel CreateViewModel() => new(
        _maintenanceService,
        _confirmationDialogService,
        _transientFeedbackService,
        _supportReportBuilder,
        _navigationService);
}
