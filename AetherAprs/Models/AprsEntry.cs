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

    public Point? Location { get; init; }

    public string? Comment { get; init; }

    public AprsSymbol? Symbol { get; init; }

    public DateTimeOffset? Timestamp { get; init; }

    public AprsPartialTimestamp? AprsTimestamp { get; init; }

    public AprsStationDataType? StationDataType { get; init; }

    public string? ObjectName { get; init; }

    public string? ItemName { get; init; }

    public string? MessageRecipient { get; init; }

    public string? MessageText { get; init; }

    public string? MessageId { get; init; }

    public bool IsAlive { get; init; } = true;

    public void Validate()
    {
        AprsTimestamp?.Validate();

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

        if (StationDataType is null)
        {
            throw new InvalidOperationException("Station entries require a station data type.");
        }

        if (StationDataType is AprsStationDataType.TimestampedPositionWithoutMessaging or AprsStationDataType.TimestampedPositionWithMessaging)
        {
            if (AprsTimestamp is null && Timestamp is null)
            {
                throw new InvalidOperationException("Timestamped station entries require an APRS timestamp or a full timestamp.");
            }
        }
        else if (AprsTimestamp is not null || Timestamp is not null)
        {
            throw new InvalidOperationException("Untimestamped station entries cannot carry a timestamp.");
        }
    }

    private void ValidateObject()
    {
        ValidateProtocolIdentifier(ObjectName, 9, "Object entries require an object name.", "Object names must be at most 9 characters.");

        if (AprsTimestamp is null && Timestamp is null)
        {
            throw new InvalidOperationException("Object entries require an APRS timestamp or a full timestamp.");
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
        ValidateProtocolIdentifier(ItemName, 9, "Item entries require an item name.", "Item names must be at most 9 characters.");

        if (ItemName!.Contains('!') || ItemName.Contains('_'))
        {
            throw new InvalidOperationException("Item names cannot contain APRS item state separators.");
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
        ValidateProtocolIdentifier(MessageRecipient, 9, "Message entries require a message recipient.", "Message recipients must be at most 9 characters.");

        if (string.IsNullOrWhiteSpace(MessageText))
        {
            throw new InvalidOperationException("Message entries require message text.");
        }
    }

    private static void ValidateProtocolIdentifier(string? value, int maxLength, string missingMessage, string tooLongMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(missingMessage);
        }

        if (value.Length > maxLength)
        {
            throw new InvalidOperationException(tooLongMessage);
        }
    }
}
