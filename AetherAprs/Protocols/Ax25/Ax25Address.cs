// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Protocols.Ax25;

public sealed record Ax25Address(string CallSign, byte Ssid, bool HasBeenRepeated = false);
