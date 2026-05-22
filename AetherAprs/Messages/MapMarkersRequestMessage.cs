// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Messages;

using CommunityToolkit.Mvvm.Messaging.Messages;
using System.Collections.Generic;
using NetTopologySuite.Geometries;

public class MapMarkersRequestMessage : RequestMessage<IEnumerable<Point>>
{
}
