// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Android;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services;

/// <summary>
/// Shared runtime permission helper for Bluetooth Scan/Connect on API 31+.
/// </summary>
internal static class BluetoothPermissionHelper
{
    private const int BluetoothPermissionRequestCode = 1100;

    private static readonly string[] RequiredPermissions =
    [
        Manifest.Permission.BluetoothScan,
        Manifest.Permission.BluetoothConnect
    ];

    public static bool HasBluetoothPermissions()
    {
        var context = global::Android.App.Application.Context;
        return RequiredPermissions.All(permission =>
            ContextCompat.CheckSelfPermission(context, permission) == Permission.Granted);
    }

    public static async Task EnsurePermissionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (HasBluetoothPermissions())
        {
            return;
        }

        var activity = MainActivity.Instance
            ?? throw new InvalidOperationException("MainActivity is not available to request Bluetooth permissions.");

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnPermissionResultHandler(int requestCode, string[] permissions, Permission[] grantResults)
        {
            if (requestCode != BluetoothPermissionRequestCode)
            {
                return;
            }

            MainActivity.OnPermissionResult -= OnPermissionResultHandler;
            var granted = grantResults.Length > 0 && grantResults.All(result => result == Permission.Granted);
            tcs.TrySetResult(granted);
        }

        await using var registration = cancellationToken.Register(() =>
        {
            MainActivity.OnPermissionResult -= OnPermissionResultHandler;
            tcs.TrySetCanceled(cancellationToken);
        });

        MainActivity.OnPermissionResult += OnPermissionResultHandler;
        ActivityCompat.RequestPermissions(activity, RequiredPermissions, BluetoothPermissionRequestCode);

        var granted = await tcs.Task.ConfigureAwait(false);
        if (!granted)
        {
            throw new UnauthorizedAccessException("Bluetooth permissions were not granted.");
        }
    }

    public static BluetoothAdapter GetAdapterOrThrow()
    {
        var context = global::Android.App.Application.Context;
        if (context.GetSystemService(Context.BluetoothService) is BluetoothManager manager
            && manager.Adapter is BluetoothAdapter adapter)
        {
            return adapter;
        }

        throw new InvalidOperationException("Bluetooth adapter is not available on this device.");
    }
}