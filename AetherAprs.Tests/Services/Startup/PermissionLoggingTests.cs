// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Startup;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services.Startup;

/// <summary>
/// Pins what the permission reporting path is allowed to write down: the
/// permission and its consequence, never the operator's configuration content.
/// </summary>
public sealed class PermissionLoggingTests
{
    private const string SecretPasscode = "54123";
    private const string SecretServer = "private.example.invalid";
    private const string SecretFilter = "b/EXAMPLE-1/EXAMPLE-2";
    private const string SecretPortName = "CAMPER-RELAY";

    [Fact]
    public async Task RequestAsync_WhenADenialIsReported_LogsOnlyThePermissionAndItsConsequence()
    {
        CapturingLogger logger = new();
        IPermissionService permissions = Substitute.For<IPermissionService>();
        permissions.RequestAsync(Arg.Any<PermissionKind>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(
                call.Arg<PermissionKind>() == PermissionKind.Notifications
                    ? PermissionRequestResult.Denied(PermissionKind.Notifications)
                    : PermissionRequestResult.Granted(call.Arg<PermissionKind>())));

        await PortStartupPermissions.RequestAsync(
            [CreateSensitiveAprsIsPort(), CreateSensitiveKissPort()],
            permissions,
            logger,
            TestContext.Current.CancellationToken);

        LogRecord record = Assert.Single(logger.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("Notifications", record.Message, StringComparison.Ordinal);
        Assert.Contains("denied", record.Message, StringComparison.Ordinal);

        // Neither the rendered message nor the structured state may carry the
        // operator's server, passcode, filter or port name.
        foreach (string? forbidden in new[] { SecretPasscode, SecretServer, SecretFilter, SecretPortName })
        {
            Assert.DoesNotContain(forbidden, record.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, record.State, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RequestAsync_WhenEveryPermissionIsGranted_LogsNothing()
    {
        CapturingLogger logger = new();
        IPermissionService permissions = Substitute.For<IPermissionService>();
        permissions.RequestAsync(Arg.Any<PermissionKind>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(PermissionRequestResult.Granted(call.Arg<PermissionKind>())));

        await PortStartupPermissions.RequestAsync(
            [CreateSensitiveAprsIsPort(), CreateSensitiveKissPort()],
            permissions,
            logger,
            TestContext.Current.CancellationToken);

        Assert.Empty(logger.Records);
    }

    private static PortConfig CreateSensitiveAprsIsPort() =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = SecretPortName,
            IsEnabled = true,
            TypeSettings = new AprsIsSettings
            {
                Server = SecretServer,
                Passcode = SecretPasscode,
                Filter = SecretFilter
            }
        };

    private static PortConfig CreateSensitiveKissPort() =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = SecretPortName,
            IsEnabled = true,
            TypeSettings = new KissSettings { Transport = new BluetoothLeKissTransportSettings() }
        };

    /// <summary>
    /// Keeps every record's rendered message and structured state so a test can
    /// assert on what a log sink would actually receive.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<LogRecord> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Records.Add(new LogRecord(logLevel, formatter(state, exception), state?.ToString() ?? string.Empty));
    }

    private sealed record LogRecord(LogLevel Level, string Message, string State);
}
