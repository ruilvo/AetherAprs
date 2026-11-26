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
        var order = new List<string>();
        var startup = Create(
            RecordStep("database", order),
            RecordStep("settings", order),
            RecordStep("ports", order),
            RecordStep("sessions", order));

        var report = await startup.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "database", "settings", "ports", "sessions" }, order);
        Assert.True(report.Succeeded);
        Assert.All(report.Steps, step => Assert.Equal(StartupStepOutcome.Completed, step.Outcome));
    }

    [Fact]
    public async Task RunAsync_RequiredFailure_StopsTheRunAndSkipsLaterSteps()
    {
        var ran = new List<string>();
        var startup = Create(
            Step("database", () => throw new InvalidOperationException("schema"), isRequired: true),
            RecordStep("settings", ran),
            RecordStep("sessions", ran));

        var report = await startup.RunAsync(TestContext.Current.CancellationToken);

        Assert.False(report.Succeeded);
        Assert.False(report.WasCancelled);
        Assert.Equal("database", report.Failure?.Name);
        Assert.Empty(ran);
        Assert.Equal(
            new[]
            {
                StartupStepOutcome.Failed,
                StartupStepOutcome.Skipped,
                StartupStepOutcome.Skipped
            },
            report.Steps.Select(step => step.Outcome).ToArray());
    }

    [Fact]
    public async Task RunAsync_OptionalFailure_IsRecordedAndTheRunContinues()
    {
        var ran = new List<string>();
        var startup = Create(
            Step("permissions", () => throw new InvalidOperationException("denied"), isRequired: false),
            RecordStep("sessions", ran));

        var report = await startup.RunAsync(TestContext.Current.CancellationToken);

        Assert.False(report.Succeeded);
        Assert.Equal("permissions", report.Failure?.Name);
        Assert.Equal(new[] { "sessions" }, ran);
    }

    [Fact]
    public async Task RunAsync_SecondCall_ReturnsTheFirstReportWithoutRunningAgain()
    {
        var runs = 0;
        var startup = Create(Step("database", () => runs++));

        var first = await startup.RunAsync(TestContext.Current.CancellationToken);
        var second = await startup.RunAsync(TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Equal(1, runs);
        Assert.True(startup.HasRun);
        Assert.Same(first, startup.LastReport);
    }

    [Fact]
    public async Task RunAsync_ConcurrentCalls_ShareOneRun()
    {
        var runs = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startup = Create(new StartupStep("database", async _ =>
        {
            runs++;
            await gate.Task;
        }));

        var first = startup.RunAsync(TestContext.Current.CancellationToken);
        var second = startup.RunAsync(TestContext.Current.CancellationToken);
        gate.SetResult();

        var reports = await Task.WhenAll(first, second).WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Same(reports[0], reports[1]);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task RunAsync_AlreadyCancelledToken_SkipsEveryStep()
    {
        var ran = false;
        var startup = Create(Step("database", () => ran = true));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var report = await startup.RunAsync(cancellation.Token);

        Assert.False(ran);
        Assert.True(report.WasCancelled);
        Assert.False(report.Succeeded);
        Assert.All(report.Steps, step => Assert.Equal(StartupStepOutcome.Skipped, step.Outcome));
    }

    [Fact]
    public async Task RunAsync_CancellationInsideAStep_StopsTheRun()
    {
        var ran = false;
        using var cancellation = new CancellationTokenSource();
        var startup = Create(
            new StartupStep("sessions", async token =>
            {
                await cancellation.CancelAsync();
                token.ThrowIfCancellationRequested();
            }),
            Step("maintenance", () => ran = true));

        var report = await startup.RunAsync(cancellation.Token);

        Assert.False(ran);
        Assert.True(report.WasCancelled);
        Assert.Equal(StartupStepOutcome.Skipped, report.Steps[0].Outcome);
    }

    [Fact]
    public async Task RunAsync_Failure_IsRecordedInDiagnostics()
    {
        var diagnostics = Substitute.For<IDiagnosticEventSink>();
        var startup = new ApplicationStartup(
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
        var startup = Create(Step("database", () => { }));
        startup.StepCompleted += (_, _) => throw new InvalidOperationException("observer");

        var report = await startup.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(report.Succeeded);
    }

    [Fact]
    public async Task RunAsync_RaisesStepCompletedForEachStep()
    {
        var observed = new List<string>();
        var startup = Create(Step("database", () => { }), Step("sessions", () => { }));
        startup.StepCompleted += (_, args) => observed.Add(args.Result.Name);

        await startup.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "database", "sessions" }, observed);
    }

    [Fact]
    public void HasRun_IsFalseBeforeTheSequenceFinishes()
    {
        var startup = Create(Step("database", () => { }));

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
