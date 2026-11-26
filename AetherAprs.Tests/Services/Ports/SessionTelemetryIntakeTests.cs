// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AetherAprs.Services.Ports;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AetherAprs.Tests.Services.Ports;

public sealed class SessionTelemetryIntakeTests
{
    [Fact]
    public async Task Observations_AreDeliveredInOrder()
    {
        var handled = new List<Guid>();
        var intake = new SessionTelemetryIntake(
            observation =>
            {
                handled.Add(observation.PortId);
                return Task.CompletedTask;
            },
            NullLogger.Instance,
            queueCapacity: 8);

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();

        Assert.True(intake.Post(Receive(first)));
        Assert.True(intake.Post(Receive(second)));
        Assert.True(intake.Post(Receive(third)));

        await intake.DisposeAsync();

        Assert.Equal([first, second, third], handled);
        Assert.Equal(0, intake.DroppedObservations);
    }

    [Fact]
    public async Task FullQueue_DropsTheNewestObservationAndCountsIt()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handled = new List<Guid>();

        var blocking = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var fourth = Guid.NewGuid();

        var intake = new SessionTelemetryIntake(
            observation =>
            {
                if (observation.PortId == blocking)
                {
                    entered.TrySetResult();
                    return gate.Task;
                }

                handled.Add(observation.PortId);
                return Task.CompletedTask;
            },
            NullLogger.Instance,
            queueCapacity: 2);

        Assert.True(intake.Post(Receive(blocking)));
        await entered.Task;

        Assert.True(intake.Post(Receive(second)));
        Assert.True(intake.Post(Receive(third)));
        Assert.False(intake.Post(Receive(fourth)));
        Assert.Equal(1, intake.DroppedObservations);

        gate.TrySetResult();
        await intake.DisposeAsync();

        Assert.Equal([second, third], handled);
    }

    [Fact]
    public async Task HandlerFailure_DoesNotStopLaterObservations()
    {
        var failed = Guid.NewGuid();
        var delivered = Guid.NewGuid();
        var handled = new List<Guid>();

        var intake = new SessionTelemetryIntake(
            observation =>
            {
                if (observation.PortId == failed)
                {
                    throw new InvalidOperationException("simulated observation failure");
                }

                handled.Add(observation.PortId);
                return Task.CompletedTask;
            },
            NullLogger.Instance);

        Assert.True(intake.Post(Receive(failed)));
        Assert.True(intake.Post(Receive(delivered)));

        await intake.DisposeAsync();

        Assert.Equal([delivered], handled);
    }

    [Fact]
    public async Task ObservationsAfterDispose_AreRejected()
    {
        var intake = new SessionTelemetryIntake(
            _ => Task.CompletedTask,
            NullLogger.Instance);

        await intake.DisposeAsync();

        Assert.False(intake.Post(Receive(Guid.NewGuid())));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SessionTelemetryIntake(_ => Task.CompletedTask, NullLogger.Instance, 0));
    }

    private static SessionTelemetryObservation Receive(Guid portId) =>
        new(portId, SessionTelemetryKind.Receive, Error: null);
}
