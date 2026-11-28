// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

using AetherAprs.Data;
using AetherAprs.Data.Entities;
using AetherAprs.Models.Aprs;
using AetherAprs.Tests.Helpers;

using Xunit;

namespace AetherAprs.Tests.Data;

public sealed class AppDbContextTests
{
    [Fact]
    public void DateTimes_RoundTripAsUtc()
    {
        using TempAppDatabase database = TempAppDatabase.CreateEmpty();
        DateTime timestamp = new(2026, 11, 24, 12, 34, 56, DateTimeKind.Utc);
        DateTime packetTimestamp = timestamp.AddMinutes(-1);
        DateTime nextRetryTime = timestamp.AddMinutes(1);

        using (AppDbContext context = database.CreateContext())
        {
            context.Database.EnsureCreated();
            context.Packets.Add(new PacketRecord
            {
                SourceBase = "N0CALL",
                SourceSsid = AprsSsid.PrimaryStation,
                DestinationBase = "APRS",
                DestinationSsid = AprsSsid.PrimaryStation,
                PacketType = "Message",
                Timestamp = timestamp,
                PacketTimestamp = packetTimestamp,
                RawInfo = "raw",
                RawFrameHex = string.Empty,
                RawPath = string.Empty,
                Message = new MessageDataRecord
                {
                    AddresseeBase = "N0CALL",
                    AddresseeSsid = AprsSsid.PrimaryStation,
                    Text = "hello",
                    NextRetryTime = nextRetryTime
                }
            });
            context.SaveChanges();
        }

        using AppDbContext reloadedContext = database.CreateContext();
        PacketRecord reloaded = Assert.Single(reloadedContext.Packets);

        Assert.Equal(timestamp, reloaded.Timestamp);
        Assert.Equal(DateTimeKind.Utc, reloaded.Timestamp.Kind);
        Assert.Equal(packetTimestamp, reloaded.PacketTimestamp);
        Assert.Equal(DateTimeKind.Utc, reloaded.PacketTimestamp!.Value.Kind);
        Assert.Equal(nextRetryTime, reloaded.Message!.NextRetryTime);
        Assert.Equal(DateTimeKind.Utc, reloaded.Message.NextRetryTime!.Value.Kind);
    }
}
