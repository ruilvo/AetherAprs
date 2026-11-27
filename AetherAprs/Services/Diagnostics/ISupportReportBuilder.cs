// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Diagnostics;

/// <summary>
/// Builds the redacted support report the operator can preview before sharing it.
/// </summary>
/// <remarks>
/// The report is a presentation-independent document: the same builder produces
/// the JSON for the in-app preview and for a file the operator chooses to share.
/// It must never include message text, raw frames or precise coordinates.
/// </remarks>
public interface ISupportReportBuilder
{
    /// <summary>
    /// Builds the report and returns it as indented JSON.
    /// </summary>
    /// <returns>The redacted support report.</returns>
    string GenerateJson();
}
