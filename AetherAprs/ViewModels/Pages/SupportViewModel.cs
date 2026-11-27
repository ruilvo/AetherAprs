// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Localization;
using AetherAprs.Services.Diagnostics;
using AetherAprs.Services.Maintenance;
using AetherAprs.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// Support page: clears one category of local data at a time and previews the
/// redacted support report.
/// </summary>
/// <remarks>
/// Every clear is independent and confirmed first. The page never shares, uploads
/// or writes the report; it only renders what the builder produced so the operator
/// can review it before choosing to share it elsewhere.
/// </remarks>
public partial class SupportViewModel : ViewModelBase
{
    private readonly ILocalDataMaintenanceService _maintenanceService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly ITransientFeedbackService _transientFeedbackService;
    private readonly ISupportReportBuilder _supportReportBuilder;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearPacketHistoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearMessagesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearPendingOutboundCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearOfflineMapTilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearDiagnosticsCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string PacketHistoryStatus { get; set; } = Strings.Get("SupportStatusNeverCleared");

    [ObservableProperty]
    public partial string MessagesStatus { get; set; } = Strings.Get("SupportStatusNeverCleared");

    [ObservableProperty]
    public partial string PendingOutboundStatus { get; set; } = Strings.Get("SupportStatusNeverCleared");

    [ObservableProperty]
    public partial string OfflineMapTilesStatus { get; set; } = Strings.Get("SupportStatusNeverCleared");

    [ObservableProperty]
    public partial string DiagnosticsStatus { get; set; } = Strings.Get("SupportStatusNeverCleared");

    [ObservableProperty]
    public partial string ReportPreview { get; set; } = string.Empty;

    public SupportViewModel(
        ILocalDataMaintenanceService maintenanceService,
        IConfirmationDialogService confirmationDialogService,
        ITransientFeedbackService transientFeedbackService,
        ISupportReportBuilder supportReportBuilder,
        INavigationService navigationService)
    {
        _maintenanceService = maintenanceService;
        _confirmationDialogService = confirmationDialogService;
        _transientFeedbackService = transientFeedbackService;
        _supportReportBuilder = supportReportBuilder;
        _navigationService = navigationService;
    }

    private bool CanClear() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanClear))]
    private Task ClearPacketHistoryAsync() => ClearAsync(
        "SupportClearConfirmationPacketHistory",
        "SupportClearedPacketHistoryFeedback",
        token => _maintenanceService.ClearPacketHistoryAsync(token),
        message => PacketHistoryStatus = message);

    [RelayCommand(CanExecute = nameof(CanClear))]
    private Task ClearMessagesAsync() => ClearAsync(
        "SupportClearConfirmationMessages",
        "SupportClearedMessagesFeedback",
        token => _maintenanceService.ClearMessagesAsync(token),
        message => MessagesStatus = message);

    [RelayCommand(CanExecute = nameof(CanClear))]
    private Task ClearPendingOutboundAsync() => ClearAsync(
        "SupportClearConfirmationPendingOutbound",
        "SupportClearedPendingOutboundFeedback",
        token => _maintenanceService.ClearPendingOutboundAsync(token),
        message => PendingOutboundStatus = message);

    [RelayCommand(CanExecute = nameof(CanClear))]
    private Task ClearOfflineMapTilesAsync() => ClearAsync(
        "SupportClearConfirmationOfflineMapTiles",
        "SupportClearedOfflineMapTilesFeedback",
        token => _maintenanceService.ClearOfflineMapTilesAsync(token),
        message => OfflineMapTilesStatus = message);

    [RelayCommand(CanExecute = nameof(CanClear))]
    private Task ClearDiagnosticsAsync() => ClearAsync(
        "SupportClearConfirmationDiagnostics",
        "SupportClearedDiagnosticsFeedback",
        token => Task.FromResult(_maintenanceService.ClearDiagnostics()),
        message => DiagnosticsStatus = message);

    [RelayCommand]
    private void GenerateReportPreview() => ReportPreview = _supportReportBuilder.GenerateJson();

    [RelayCommand]
    private void Close() => _navigationService.GoBack();

    private async Task ClearAsync(
        string confirmationMessageKey,
        string feedbackKey,
        Func<CancellationToken, Task<LocalDataClearResult>> clear,
        Action<string> setStatus)
    {
        if (IsBusy)
        {
            return;
        }

        bool confirmed = await _confirmationDialogService.ShowConfirmationAsync(
            Strings.Get("SupportClearConfirmationTitle"),
            Strings.Get(confirmationMessageKey));

        if (!confirmed || IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            LocalDataClearResult result = await clear(CancellationToken.None);
            string message = Strings.Format(feedbackKey, result.RemovedRows);
            setStatus(message);
            _transientFeedbackService.Show(message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
