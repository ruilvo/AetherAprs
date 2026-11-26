// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Platform;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Android implementation of permission service.
/// Waits for MainActivity to complete permission requests.
/// </summary>
public class AndroidPermissionService : IPermissionService
{
    private static readonly TaskCompletionSource<bool> _permissionsCompleteTcs = new();

    // Android reports "should show rationale" as false both before the first
    // request and after a permanent denial, so the service remembers which kinds
    // it already asked about. Only a kind that was asked before and no longer
    // suggests a rationale is a permanent denial.
    private static readonly ConcurrentDictionary<PermissionKind, bool> _promptedKinds = new();

    private static readonly TimeSpan _permissionWaitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Called by MainActivity when initial permission requests are complete.
    /// </summary>
    internal static void SignalPermissionsComplete()
    {
        _permissionsCompleteTcs.TrySetResult(true);
    }

    private static async Task WaitForPermissionsAsync(CancellationToken cancellationToken)
    {
        // Wait for MainActivity to complete permission requests, with timeout
        var timeoutTask = Task.Delay(_permissionWaitTimeout, cancellationToken);
        var completedTask = await Task.WhenAny(_permissionsCompleteTcs.Task, timeoutTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (completedTask != _permissionsCompleteTcs.Task)
        {
            throw new TimeoutException("Timeout waiting for MainActivity permission requests");
        }
    }

    /// <inheritdoc/>
    public async Task<PermissionRequestResult> RequestAsync(
        PermissionKind kind,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await WaitForPermissionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Without the startup burst there is no reliable prompt host, so the
            // permission is treated as unavailable instead of silently denied.
            global::Android.Util.Log.Warn("AetherAprs", $"Permission check for {kind} timed out: {exception.Message}");
            return PermissionRequestResult.Unavailable(kind);
        }

        var activity = MainActivity.Instance;
        if (activity == null || activity.IsDestroyed)
        {
            return PermissionRequestResult.Unavailable(kind);
        }

        if (!IsRequiredOnThisPlatform(kind))
        {
            return PermissionRequestResult.Granted(kind);
        }

        if (kind == PermissionKind.BatteryOptimization)
        {
            return RequestBatteryOptimizationExemption(activity);
        }

        var requestedBefore = _promptedKinds.ContainsKey(kind);
        var granted = await PromptAsync(activity, kind).ConfigureAwait(false);
        if (!granted)
        {
            _promptedKinds[kind] = true;
        }

        return PermissionRequestPolicy.Evaluate(
            kind,
            granted,
            HasRationale(activity, kind),
            requestedBefore);
    }

    /// <inheritdoc/>
    public Task<bool> OpenApplicationSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var activity = MainActivity.Instance;
        if (activity == null || activity.IsDestroyed)
        {
            return Task.FromResult(false);
        }

        try
        {
            var intent = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionApplicationDetailsSettings);
            intent.SetData(global::Android.Net.Uri.Parse($"package:{activity.PackageName}"));
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            activity.StartActivity(intent);
            return Task.FromResult(true);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn("AetherAprs", $"Could not open application settings: {exception.Message}");
            return Task.FromResult(false);
        }
    }

    private static bool IsRequiredOnThisPlatform(PermissionKind kind) => kind switch
    {
        // Both permissions only exist as runtime permissions from these levels; the
        // older levels declare them in the manifest, so nothing has to be prompt.
        PermissionKind.Notifications => global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Tiramisu,
        PermissionKind.Bluetooth => global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.S,
        _ => true
    };

    private static Task<bool> PromptAsync(MainActivity activity, PermissionKind kind) => kind switch
    {
        PermissionKind.Notifications => activity.RequestNotificationPermissionAsync(),
        PermissionKind.Bluetooth => activity.RequestBluetoothPermissionAsync(),
        PermissionKind.Location => activity.RequestLocationPermissionsAsync(),
        PermissionKind.Audio => activity.RequestAudioPermissionAsync(),
        _ => Task.FromResult(false)
    };

    private static bool HasRationale(MainActivity activity, PermissionKind kind) => kind switch
    {
        PermissionKind.Notifications =>
            activity.ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.PostNotifications),
        PermissionKind.Bluetooth =>
            activity.ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.BluetoothScan) ||
            activity.ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.BluetoothConnect),
        PermissionKind.Location =>
            activity.ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.AccessFineLocation) ||
            activity.ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.AccessCoarseLocation),
        PermissionKind.Audio =>
            activity.ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.RecordAudio),
        _ => false
    };

    private static PermissionRequestResult RequestBatteryOptimizationExemption(MainActivity activity)
    {
        var powerManager = activity.GetSystemService(global::Android.Content.Context.PowerService) as global::Android.OS.PowerManager;
        if (powerManager == null)
        {
            return PermissionRequestResult.Unavailable(PermissionKind.BatteryOptimization);
        }

        if (powerManager.IsIgnoringBatteryOptimizations(activity.PackageName!))
        {
            return PermissionRequestResult.Granted(PermissionKind.BatteryOptimization);
        }

        try
        {
            // The system dialog result is asynchronous and outside our control, so a
            // plain denial reports "prompt shown" and the caller may ask again.
            var intent = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations);
            intent.SetData(global::Android.Net.Uri.Parse($"package:{activity.PackageName}"));
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            activity.StartActivity(intent);
            return PermissionRequestResult.Denied(PermissionKind.BatteryOptimization);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn("AetherAprs", $"Could not open battery optimization settings: {exception.Message}");
            return PermissionRequestResult.Unavailable(PermissionKind.BatteryOptimization);
        }
    }
}
