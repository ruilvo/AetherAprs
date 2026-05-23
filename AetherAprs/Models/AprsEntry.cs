// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models;

using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;

public enum AprsEntryKind
{
    Station,
    Object,
    Item,
    Message,
}

public sealed class AprsEntry
{
    public required AprsEntryKind Kind { get; init; }

    public required string Source { get; init; }

    public required string Destination { get; init; }

    public IReadOnlyList<string> Path { get; init; } = [];

    public Point? Location { get; init; }

    public string? Comment { get; init; }

    public AprsSymbol? Symbol { get; init; }

    public DateTimeOffset? Timestamp { get; init; }

    public string? ObjectName { get; init; }

    public string? ItemName { get; init; }

    public string? MessageRecipient { get; init; }

    public string? MessageText { get; init; }

    public string? MessageId { get; init; }

    public bool IsAlive { get; init; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Source))
        {
            throw new InvalidOperationException("Source is required.");
        }

        if (string.IsNullOrWhiteSpace(Destination))
        {
            throw new InvalidOperationException("Destination is required.");
        }

        switch (Kind)
        {
            case AprsEntryKind.Station:
                ValidateStation();
                break;
            case AprsEntryKind.Object:
                ValidateObject();
                break;
            case AprsEntryKind.Item:
                ValidateItem();
                break;
            case AprsEntryKind.Message:
                ValidateMessage();
                break;
            default:
                throw new InvalidOperationException($"Unsupported APRS entry kind '{Kind}'.");
        }
    }

    private void ValidateStation()
    {
        if (Location is null)
        {
            throw new InvalidOperationException("Station entries require a location.");
        }

        if (Symbol is null)
        {
            throw new InvalidOperationException("Station entries require a symbol.");
        }
    }

    private void ValidateObject()
    {
        if (string.IsNullOrWhiteSpace(ObjectName))
        {
            throw new InvalidOperationException("Object entries require an object name.");
        }

        if (Timestamp is null)
        {
            throw new InvalidOperationException("Object entries require a timestamp.");
        }

        if (Location is null)
        {
            throw new InvalidOperationException("Object entries require a location.");
        }

        if (Symbol is null)
        {
            throw new InvalidOperationException("Object entries require a symbol.");
        }
    }

    private void ValidateItem()
    {
        if (string.IsNullOrWhiteSpace(ItemName))
        {
            throw new InvalidOperationException("Item entries require an item name.");
        }

        if (Location is null)
        {
            throw new InvalidOperationException("Item entries require a location.");
        }

        if (Symbol is null)
        {
            throw new InvalidOperationException("Item entries require a symbol.");
        }
    }

    private void ValidateMessage()
    {
        if (string.IsNullOrWhiteSpace(MessageRecipient))
        {
            throw new InvalidOperationException("Message entries require a message recipient.");
        }

        if (string.IsNullOrWhiteSpace(MessageText))
        {
            throw new InvalidOperationException("Message entries require message text.");
        }
    }
}
