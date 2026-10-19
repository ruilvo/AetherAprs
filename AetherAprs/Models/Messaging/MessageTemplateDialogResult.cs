// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Messaging;

/// <summary>
/// The result of the message template picker dialog: the applied message text
/// and the destination callsign to use for the message.
/// </summary>
public sealed record MessageTemplateDialogResult(string Text, string Destination);
