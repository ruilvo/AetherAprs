// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Configuration;
using AetherAprs.Data.Entities;
using AetherAprs.Data.Mappers;
using AetherAprs.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AetherAprs.Tests.Services.Platform;

public sealed class SecretStorageTests
{
    [Fact]
    public void PortRecordMapper_StoresProtectedPasscodeRepresentation()
    {
        PortConfig port = new()
        {
            Name = "APRS-IS",
            TypeSettings = new AprsIsSettings { Passcode = "12345" }
        };

        PortRecord record = PortRecordMapper.ToRecord(port, "ks1:encrypted-value");

        Assert.Equal("ks1:encrypted-value", record.AprsIs?.Passcode);
        Assert.NotEqual("12345", record.AprsIs?.Passcode);
    }

    [Fact]
    public void PlaintextSecretProtector_IsOnlyTheNonAndroidFallback()
    {
        PlaintextSecretProtector protector = new(NullLogger<PlaintextSecretProtector>.Instance);

        Assert.False(protector.IsProtected("12345"));
        Assert.Equal("12345", protector.Protect("12345"));
        Assert.Equal("12345", protector.Unprotect("12345"));
    }

    [Fact]
    public void NullSecretProtector_ThrowsInsteadOfStoringCleartext()
    {
        NullSecretProtector protector = new();

        Assert.True(protector.IsProtected("12345"));
        Assert.Throws<NotSupportedException>(() => protector.Protect("12345"));
        Assert.Throws<NotSupportedException>(() => protector.Unprotect("12345"));
    }
}
