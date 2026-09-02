// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Metadata;
using Material.Icons;

namespace AetherAprs.Extensions;

/// <summary>
/// XAML markup extension that converts Material Design icon names into vector geometry.
/// </summary>
/// <remarks>
/// This markup extension bridges Material.Icons (which provides icon data) with Avalonia's
/// geometry system (which renders vector shapes). It takes a Material icon name and converts
/// it to a StreamGeometry that can be used with properties expecting Geometry objects.
/// 
/// This is particularly useful for controls like ContentPage that have an Icon property
/// expecting Geometry rather than a full control.
/// 
/// Example usage in XAML:
/// <code>
/// &lt;ContentPage Header="Home" Icon="{ext:MaterialIconGeometryExt Home}" /&gt;
/// </code>
/// 
/// Note: This class was adapted from Material.Avalonia.Demo and is not included in the
/// Material.Avalonia package itself.
/// </remarks>
public class MaterialIconGeometryExt : MarkupExtension
{
    public MaterialIconGeometryExt() { }

    public MaterialIconGeometryExt(MaterialIconKind kind)
    {
        Kind = kind;
    }

    [ConstructorArgument("kind")]
    public MaterialIconKind Kind { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var data = MaterialIconDataProvider.GetData(Kind);
        return StreamGeometry.Parse(data);
    }
}
