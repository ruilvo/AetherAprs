// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AetherAprs.Services.Diagnostics;

public enum DiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record DiagnosticEvent(
    Guid Id,
    DateTimeOffset TimestampUtc,
    string Category,
    DiagnosticSeverity Severity,
    string State,
    string Reason,
    string? SafeIdentifier);

public interface IDiagnosticEventSink
{
    IReadOnlyList<DiagnosticEvent> Events { get; }

    void Record(
        string category,
        DiagnosticSeverity severity,
        string state,
        string reason,
        string? safeIdentifier = null);

    void Clear();
}

public sealed class BoundedDiagnosticEventStore : IDiagnosticEventSink
{
    private readonly object _gate = new();
    private readonly Queue<DiagnosticEvent> _events = new();

    public BoundedDiagnosticEventStore()
        : this(256)
    {
    }

    public BoundedDiagnosticEventStore(int maximumEvents)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEvents);
        MaximumEvents = maximumEvents;
    }

    public int MaximumEvents { get; }

    public IReadOnlyList<DiagnosticEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public void Record(
        string category,
        DiagnosticSeverity severity,
        string state,
        string reason,
        string? safeIdentifier = null)
    {
        lock (_gate)
        {
            while (_events.Count >= MaximumEvents)
            {
                _events.Dequeue();
            }

            _events.Enqueue(new DiagnosticEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                Limit(category),
                severity,
                Limit(state),
                DiagnosticRedactor.Redact(reason),
                Limit(safeIdentifier)));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
        }
    }

    private static string Limit(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Length <= 120 ? value : value[..120];
}

internal static partial class DiagnosticRedactor
{
    [GeneratedRegex(
        @"(?i)\b(passcode|password|secret|token|authorization)\b\s*[:=]\s*\S+|[A-Z0-9]{6,9}>[A-Z0-9]{2,9}(?:[,/][^ ]*)?:[^\r\n]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveValuePattern();

    public static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return SensitiveValuePattern().Replace(value, "[REDACTED]");
    }
}
