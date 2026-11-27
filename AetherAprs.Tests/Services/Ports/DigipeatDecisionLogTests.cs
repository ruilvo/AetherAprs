// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using Xunit;

namespace AetherAprs.Tests.Services.Ports;

public sealed class DigipeatDecisionLogTests
{
    [Fact]
    public void GetRecentDecisions_ReturnsRecordsOldestFirst()
    {
        DigipeatDecisionLog log = new();
        TestClock clock = new();
        Guid firstPort = Guid.NewGuid();
        Guid secondPort = Guid.NewGuid();

        log.Record(clock.UtcNow, firstPort, targetPortId: null, DigipeatReason.OwnStationSource);
        clock.Advance(TimeSpan.FromSeconds(1));
        log.Record(clock.UtcNow, secondPort, targetPortId: null, DigipeatReason.DuplicateSuppressed);

        IReadOnlyList<DigipeatDecisionRecord> recent = log.GetRecentDecisions();

        Assert.Equal(2, recent.Count);
        Assert.Equal(DigipeatReason.OwnStationSource, recent[0].Reason);
        Assert.Equal(DigipeatReason.DuplicateSuppressed, recent[1].Reason);
        Assert.Equal(firstPort, recent[0].SourcePortId);
        Assert.Equal(secondPort, recent[1].SourcePortId);
    }

    [Fact]
    public void GetRecentDecisions_Maximum_ReturnsOnlyTheNewestRecords()
    {
        DigipeatDecisionLog log = new(capacity: 4);
        TestClock clock = new();

        log.Record(clock.UtcNow, Guid.NewGuid(), Guid.NewGuid(), DigipeatReason.Accepted);
        log.Record(clock.UtcNow, Guid.NewGuid(), Guid.NewGuid(), DigipeatReason.RateLimited);
        log.Record(clock.UtcNow, Guid.NewGuid(), Guid.NewGuid(), DigipeatReason.NoTargetPorts);

        IReadOnlyList<DigipeatDecisionRecord> recent = log.GetRecentDecisions(maximum: 2);

        Assert.Equal(2, recent.Count);
        Assert.Equal(DigipeatReason.RateLimited, recent[0].Reason);
        Assert.Equal(DigipeatReason.NoTargetPorts, recent[1].Reason);
    }

    [Fact]
    public void GetRecentDecisions_ZeroMaximum_IsEmpty()
    {
        DigipeatDecisionLog log = new();
        log.Record(DateTimeOffset.UnixEpoch, Guid.NewGuid(), targetPortId: null, DigipeatReason.Accepted);

        Assert.Empty(log.GetRecentDecisions(maximum: 0));
    }

    [Fact]
    public void Record_MoreThanCapacity_OverwritesTheOldestRecord()
    {
        DigipeatDecisionLog log = new(capacity: 2);
        TestClock clock = new();

        log.Record(clock.UtcNow, Guid.NewGuid(), targetPortId: null, DigipeatReason.Accepted);
        log.Record(clock.UtcNow, Guid.NewGuid(), targetPortId: null, DigipeatReason.RateLimited);
        log.Record(clock.UtcNow, Guid.NewGuid(), targetPortId: null, DigipeatReason.QueueFull);

        IReadOnlyList<DigipeatDecisionRecord> recent = log.GetRecentDecisions();

        Assert.Equal(2, log.Capacity);
        Assert.Equal(2, recent.Count);
        Assert.Equal(DigipeatReason.RateLimited, recent[0].Reason);
        Assert.Equal(DigipeatReason.QueueFull, recent[1].Reason);
    }

    [Fact]
    public void GetCount_CountsEveryRecordedReasonIncludingOverwrittenOnes()
    {
        DigipeatDecisionLog log = new(capacity: 1);
        TestClock clock = new();

        log.Record(clock.UtcNow, Guid.NewGuid(), targetPortId: null, DigipeatReason.Accepted);
        log.Record(clock.UtcNow, Guid.NewGuid(), targetPortId: null, DigipeatReason.Accepted);
        log.Record(clock.UtcNow, Guid.NewGuid(), targetPortId: null, DigipeatReason.LoopDetected);

        Assert.Equal(2, log.GetCount(DigipeatReason.Accepted));
        Assert.Equal(1, log.GetCount(DigipeatReason.LoopDetected));
        Assert.Equal(0, log.GetCount(DigipeatReason.TransmitFailed));
    }

    [Fact]
    public void GetCounts_CoversEveryReason()
    {
        DigipeatDecisionLog log = new();
        log.Record(DateTimeOffset.UnixEpoch, Guid.NewGuid(), Guid.NewGuid(), DigipeatReason.Accepted);

        IReadOnlyDictionary<DigipeatReason, long> counts = log.GetCounts();

        Assert.Equal(Enum.GetValues<DigipeatReason>().Length, counts.Count);
        Assert.Equal(1, counts[DigipeatReason.Accepted]);
        Assert.All(
            counts.Where(pair => pair.Key != DigipeatReason.Accepted),
            pair => Assert.Equal(0, pair.Value));
    }

    [Fact]
    public void Record_KeepsNoPayloadFacts()
    {
        // Forwarding history exists to answer "what happened to this route?" without
        // retaining anything the operator or a third party transmitted.
        string[] allowed = ["RecordedAt", "SourcePortId", "TargetPortId", "Reason"];
        PropertyInfo[] properties = typeof(DigipeatDecisionRecord).GetProperties();

        Assert.Equal(
            allowed.OrderBy(name => name, StringComparer.Ordinal),
            properties.Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveCapacity_IsRejected(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DigipeatDecisionLog(capacity));
    }
}
