// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Services.Diagnostics;
using AetherAprs.Services.Startup;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Xunit;

namespace AetherAprs.Tests.Services.Startup;

public sealed class ApplicationStartupTests
{
    [Fact]
    public async Task RunAsync_RunsStepsInDeclarationOrder()
    {
        List<string> order = [];
        ApplicationStartup startup = Create(
            RecordStep("database", order),
            RecordStep("settings", order),
            RecordStep("ports", order),
            RecordStep("sessions", order));

        StartupReport report = await startup.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "database", "settings", "ports", "sessions" }, order);
        Assert.True(report.Succeeded);
        Assert.All(report.Steps, step => Assert.Equal(StartupStepOutcome.Completed, step.Outcome));
    }

    [Fact]
    public async Task RunAsync_RequiredFailure_StopsTheRunAndSkipsLaterSteps()
    {
        List<string> ran = [];
        ApplicationStartup startup = Create(
            Step("database", () => throw new InvalidOperationException("schema"), isRequired: true),
            RecordStep("settings", ran),
            RecordStep("sessions", ran));

        StartupReport report = await startup.RunAsync(TestContext.Current.CancellationToken);

        Assert.False(report.Succeeded);
        Assert.False(report.WasCancelled);
        Assert.Equal("database", report.Failure?.Name);
        Assert.Empty(ran);
        Assert.Equal(
            [
                StartupStepOutcome.Failed,
                StartupStepOutcome.Skipped,
                StartupStepOutcome.Skipped
            ],
            report.Steps.Select(step => step.Outcome).ToArray());
    }

    [Fact]
    public async Task RunAsync_OptionalFailure_IsRecordedAndTheRunContinues()
    {
        List<string> ran = [];
        ApplicationStartup startup = Create(
            Step("permissions", () => throw new InvalidOperationException("denied"), isRequired: false),
            RecordStep("sessions", ran));

        StartupReport report = await startup.RunAsync(TestContext.Current.CancellationToken);

        Assert.False(report.Succeeded);
        Assert.Equal("permissions", report.Failure?.Name);
        Assert.Equal(new[] { "sessions" }, ran);
    }

    [Fact]
    public async Task RunAsync_SecondCall_ReturnsTheFirstReportWithoutRunningAgain()
    {
        int runs = 0;
        ApplicationStartup startup = Create(Step("database", () => runs++));

        StartupReport first = await startup.RunAsync(TestContext.Current.CancellationToken);
        StartupReport second = await startup.RunAsync(TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Equal(1, runs);
        Assert.True(startup.HasRun);
        Assert.Same(first, startup.LastReport);
    }

    [Fact]
    public async Task RunAsync_ConcurrentCalls_ShareOneRun()
    {
        int runs = 0;
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ApplicationStartup startup = Create(new StartupStep("database", async _ =>
        {
            runs++;
            await gate.Task;
        }));

        Task<StartupReport> first = startup.RunAsync(TestContext.Current.CancellationToken);
        Task<StartupReport> second = startup.RunAsync(TestContext.Current.CancellationToken);
        gate.SetResult();

        StartupReport[] reports = await Task.WhenAll(first, second).WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Same(reports[0], reports[1]);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task RunAsync_AlreadyCancelledToken_SkipsEveryStep()
    {
        bool ran = false;
        ApplicationStartup startup = Create(Step("database", () => ran = true));
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        StartupReport report = await startup.RunAsync(cancellation.Token);

        Assert.False(ran);
        Assert.True(report.WasCancelled);
        Assert.False(report.Succeeded);
        Assert.All(report.Steps, step => Assert.Equal(StartupStepOutcome.Skipped, step.Outcome));
    }

    [Fact]
    public async Task RunAsync_CancellationInsideAStep_StopsTheRun()
    {
        bool ran = false;
        using CancellationTokenSource cancellation = new();
        ApplicationStartup startup = Create(
            new StartupStep("sessions", async token =>
            {
                await cancellation.CancelAsync();
                token.ThrowIfCancellationRequested();
            }),
            Step("maintenance", () => ran = true));

        StartupReport report = await startup.RunAsync(cancellation.Token);

        Assert.False(ran);
        Assert.True(report.WasCancelled);
        Assert.Equal(StartupStepOutcome.Skipped, report.Steps[0].Outcome);
    }

    [Fact]
    public async Task RunAsync_Failure_IsRecordedInDiagnostics()
    {
        IDiagnosticEventSink diagnostics = Substitute.For<IDiagnosticEventSink>();
        ApplicationStartup startup = new(
            [Step("database", () => throw new InvalidOperationException("schema"))],
            NullLogger<ApplicationStartup>.Instance,
            diagnostics);

        await startup.RunAsync(TestContext.Current.CancellationToken);

        diagnostics.Received(1).Record(
            "startup",
            DiagnosticSeverity.Error,
            "Failed",
            Arg.Any<string>(),
            "database");
    }

    [Fact]
    public async Task RunAsync_StepObserverFailure_DoesNotFailTheStep()
    {
        ApplicationStartup startup = Create(Step("database", () => { }));
        startup.StepCompleted += (_, _) => throw new InvalidOperationException("observer");

        StartupReport report = await startup.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(report.Succeeded);
    }

    [Fact]
    public async Task RunAsync_RaisesStepCompletedForEachStep()
    {
        List<string> observed = [];
        ApplicationStartup startup = Create(Step("database", () => { }), Step("sessions", () => { }));
        startup.StepCompleted += (_, args) => observed.Add(args.Result.Name);

        await startup.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "database", "sessions" }, observed);
    }

    [Fact]
    public void HasRun_IsFalseBeforeTheSequenceFinishes()
    {
        ApplicationStartup startup = Create(Step("database", () => { }));

        Assert.False(startup.HasRun);
        Assert.Null(startup.LastReport);
    }

    [Fact]
    public void StartupStep_RejectsMissingNameOrWork()
    {
        Assert.Throws<ArgumentException>(() => new StartupStep(" ", _ => Task.CompletedTask));

        Assert.Throws<ArgumentNullException>(() => new StartupStep("database", null!));
    }

    private static ApplicationStartup Create(params StartupStep[] steps) =>
        new(steps, NullLogger<ApplicationStartup>.Instance);

    private static StartupStep Step(string name, Action run, bool isRequired = true) =>
        new(
            name,
            _ =>
            {
                run();
                return Task.CompletedTask;
            },
            isRequired);

    private static StartupStep RecordStep(string name, List<string> order) =>
        Step(name, () => order.Add(name));
}
