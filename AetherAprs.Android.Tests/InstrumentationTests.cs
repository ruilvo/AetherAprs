// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Util;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using Xamarin.Android.NUnitLite;

namespace AetherAprs.Android.Tests;

[Instrumentation(Name = "com.aetheraprs.tests.TestInstrumentation")]
public sealed class TestInstrumentation(IntPtr handle, JniHandleOwnership transfer)
    : TestSuiteInstrumentation(handle, transfer)
{
    protected override void AddTests()
    {
    }

    public override void OnCreate(Bundle? arguments)
    {
        int exitCode = 0;
        try
        {
            AndroidLifecycleInstrumentationTests.AndroidProcessAndPermissionsAreAvailable();
            AndroidLifecycleInstrumentationTests.LifecycleStateMachineRecordsProcessDeathAndForegroundRejection();
            AndroidLifecycleInstrumentationTests.AndroidManifestExposesForegroundServicePermissions();
            AndroidLifecycleInstrumentationTests.TransportReconnectIsBoundedAndObservable();
            Log.Info("AetherAprs.Tests", "Android instrumentation tests passed: 4");
        }
        catch (Exception exception)
        {
            exitCode = 1;
            Log.Error("AetherAprs.Tests", $"Android instrumentation test failed: {exception}");
        }

        Bundle results = new();
        results.PutInt("nunit-exit-code", exitCode);
        Finish(exitCode == 0 ? Result.Ok : Result.Canceled, results);
    }
}

public sealed class AndroidLifecycleInstrumentationTests
{
    public static void AndroidProcessAndPermissionsAreAvailable()
    {
        Context context = Application.Context ?? throw new InvalidOperationException("Android context is unavailable.");
        Ensure(context.PackageName == "com.AetherRadio.AetherAprs.Tests", "Unexpected test package.");
        Ensure(
            context.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications)
                is Permission.Denied or Permission.Granted,
            "Notification permission state is invalid.");
    }

    public static void LifecycleStateMachineRecordsProcessDeathAndForegroundRejection()
    {
        AndroidLifecycleState state = new();
        state.ProcessStarted();
        state.ForegroundStartRequested();
        state.ForegroundRejected();
        state.ProcessStopped();

        Ensure(!state.Snapshot.ProcessAlive, "Process death was not recorded.");
        Ensure(
            state.Snapshot.ForegroundState == AndroidForegroundState.Killed,
            "Foreground kill state was not recorded.");
    }

    public static void AndroidManifestExposesForegroundServicePermissions()
    {
        Context targetContext = Application.Context.CreatePackageContext(
            "com.AetherRadio.AetherAprs",
            PackageContextFlags.IgnoreSecurity)
            ?? throw new InvalidOperationException("Target application context unavailable.");
        PackageInfo packageInfo = targetContext.PackageManager!.GetPackageInfo(
            targetContext.PackageName!,
            PackageInfoFlags.Permissions) ?? throw new InvalidOperationException("Package metadata unavailable.");
        IList<string> permissions = packageInfo.RequestedPermissions ?? [];

        Ensure(
            permissions.Contains(global::Android.Manifest.Permission.ForegroundService),
            "Foreground service permission is missing.");
        Ensure(
            permissions.Contains(global::Android.Manifest.Permission.PostNotifications),
            "Notification permission is missing.");
    }

    public static void TransportReconnectIsBoundedAndObservable()
    {
        Guid portId = Guid.NewGuid();
        RecordingPortController controller = new();
        List<TimeSpan> delays = [];
        SessionCoordinator coordinator = new(
            controller,
            new RecordingStateStore(),
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            jitter: () => 0,
            initialBackoff: TimeSpan.FromSeconds(1),
            maximumBackoff: TimeSpan.FromSeconds(3));

        coordinator.InitializeAsync().GetAwaiter().GetResult();
        coordinator.StartAsync([portId]).GetAwaiter().GetResult();
        WaitFor(() => controller.ConnectAttempts >= 1);
        coordinator.ReportPortFaultAsync(portId, new InvalidOperationException("transport lost"))
            .GetAwaiter()
            .GetResult();
        WaitFor(() => controller.ConnectAttempts >= 2);

        Ensure(
            coordinator.State == SessionLifecycleState.Running,
            "Transport reconnect did not restore the running state.");
        Ensure(
            delays.All(delay => delay <= TimeSpan.FromSeconds(3)),
            "Transport reconnect exceeded its configured bound.");
        coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static void WaitFor(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            Thread.Sleep(10);
        }

        Ensure(condition(), "Timed out waiting for the transport reconnect.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class RecordingPortController : ISessionPortController
    {
        public int ConnectAttempts { get; private set; }

        public Task ConnectAsync(Guid portId, CancellationToken cancellationToken)
        {
            ConnectAttempts++;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(Guid portId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RecordingStateStore : ISessionStateStore
    {
        public Task<SessionSnapshot?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<SessionSnapshot?>(null);

        public Task SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
