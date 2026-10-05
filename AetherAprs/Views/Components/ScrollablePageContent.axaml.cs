// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Controls;

namespace AetherAprs.Views.Components;

/// <summary>
/// Reusable component that wraps page content with a ScrollViewer
/// and auto-scroll-to-focused-control behavior.
/// </summary>
public partial class ScrollablePageContent : UserControl
{
    public ScrollablePageContent()
    {
        InitializeComponent();
    }
}
