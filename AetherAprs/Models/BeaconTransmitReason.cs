// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Localization;
using System;
using System.Globalization;

namespace AetherAprs.Models;

/// <summary>
/// Strongly-typed beacon transmit reason with localization support.
/// </summary>
public abstract class BeaconTransmitReason
{
    /// <summary>
    /// Gets the localized reason string.
    /// </summary>
    public abstract string GetLocalizedString();

    /// <summary>
    /// No location available yet.
    /// </summary>
    public sealed class NoLocationAvailable : BeaconTransmitReason
    {
        public override string GetLocalizedString() => Strings.Get("NoLocationAvailable");
    }

    /// <summary>
    /// Initial beacon or time interval exceeded.
    /// </summary>
    public sealed class InitialOrIntervalExceeded : BeaconTransmitReason
    {
        public override string GetLocalizedString() => Strings.Get("ReasonInitialOrIntervalExceeded");
    }

    /// <summary>
    /// Awaiting time interval before next beacon.
    /// </summary>
    public sealed class AwaitingTimeInterval : BeaconTransmitReason
    {
        public override string GetLocalizedString() => Strings.Get("ReasonAwaitingTimeInterval");
    }

    /// <summary>
    /// Distance moved is below minimum threshold.
    /// </summary>
    public sealed class DistanceBelowMinimum : BeaconTransmitReason
    {
        public double ActualDistanceMeters { get; init; }
        public double MinimumDistanceMeters { get; init; }

        public override string GetLocalizedString()
        {
            return string.Format(
                CultureInfo.CurrentUICulture,
                Strings.Get("ReasonDistanceBelowMinimum"),
                ActualDistanceMeters,
                MinimumDistanceMeters);
        }
    }

    /// <summary>
    /// Course changed significantly.
    /// </summary>
    public sealed class CourseChanged : BeaconTransmitReason
    {
        public double CourseDeltaDegrees { get; init; }

        public override string GetLocalizedString()
        {
            return string.Format(
                CultureInfo.CurrentUICulture,
                Strings.Get("ReasonCourseChanged"),
                CourseDeltaDegrees);
        }
    }

    /// <summary>
    /// Beacon interval exceeded.
    /// </summary>
    public sealed class IntervalExceeded : BeaconTransmitReason
    {
        public int IntervalSeconds { get; init; }

        public override string GetLocalizedString()
        {
            return string.Format(
                CultureInfo.CurrentUICulture,
                Strings.Get("ReasonIntervalExceeded"),
                IntervalSeconds);
        }
    }

    /// <summary>
    /// Waiting for next interval.
    /// </summary>
    public sealed class WaitingForNextInterval : BeaconTransmitReason
    {
        public int SecondsUntilNext { get; init; }

        public override string GetLocalizedString()
        {
            return string.Format(
                CultureInfo.CurrentUICulture,
                Strings.Get("ReasonWaitingForNextInterval"),
                SecondsUntilNext);
        }
    }
}
