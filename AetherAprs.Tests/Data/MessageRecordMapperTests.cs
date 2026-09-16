// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Data;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using Xunit;

namespace AetherAprs.Tests.Data;

public sealed class MessageRecordMapperTests
{
    [Fact]
    public void RoundTripPreservesCallsignAndPayload()
    {
        var portId = Guid.NewGuid();
        var stored = new StoredMessage
        {
            Peer = new Callsign("K0PEER", 7),
            Text = "Hello",
            Timestamp = DateTimeOffset.Parse("2026-01-02T03:04:05Z"),
            IsOutbound = true,
            MessageNumber = 12,
            PortId = portId
        };

        var roundTripped = MessageRecordMapper.ToStoredMessage(MessageRecordMapper.ToRecord(stored));

        Assert.NotNull(roundTripped);
        Assert.Equal(stored.Peer, roundTripped.Peer);
        Assert.Equal("Hello", roundTripped.Text);
        Assert.Equal(stored.Timestamp, roundTripped.Timestamp);
        Assert.True(roundTripped.IsOutbound);
        Assert.Equal(12, roundTripped.MessageNumber);
        Assert.Equal(portId, roundTripped.PortId);
    }

    [Fact]
    public void ToStoredMessageReturnsNullForInvalidPeer()
    {
        var record = new MessageRecord { Peer = "not a callsign", Text = "x" };

        Assert.Null(MessageRecordMapper.ToStoredMessage(record));
    }
}
