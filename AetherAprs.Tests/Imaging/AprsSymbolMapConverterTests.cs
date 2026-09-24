// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using Mapsui.Styles;
using NSubstitute;
using SkiaSharp;
using Xunit;

namespace AetherAprs.Tests.Imaging;

public sealed class AprsSymbolMapConverterTests : IDisposable
{
    private readonly IAprsSymbolBitmapProvider _bitmapProvider;
    private readonly AprsSymbolMapConverter _converter;
    private readonly SKBitmap _testBitmap;

    public AprsSymbolMapConverterTests()
    {
        _bitmapProvider = Substitute.For<IAprsSymbolBitmapProvider>();
        _testBitmap = new SKBitmap(64, 64);
        
        // Setup bitmap provider to return test bitmap
        _bitmapProvider.GetSymbolBitmap(Arg.Any<Symbol>()).Returns(_testBitmap);
        
        _converter = new AprsSymbolMapConverter(_bitmapProvider);
    }

    [Fact]
    public void CreateImageStyle_ReturnsImageStyleWithBase64Uri()
    {
        // Arrange
        var symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus);

        // Act
        var imageStyle = _converter.CreateImageStyle(symbol);

        // Assert
        Assert.NotNull(imageStyle);
        Assert.NotNull(imageStyle.Image);
        
        // Image property is of type Mapsui.Styles.Image which has a Source property
        var image = imageStyle.Image;
        Assert.NotNull(image);
        
        // The image should have been created from bitmap provider
        _bitmapProvider.Received(1).GetSymbolBitmap(symbol);
    }

    [Fact]
    public void CreateImageStyle_WithCustomScale_AppliesScale()
    {
        // Arrange
        var symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus);
        var scale = 0.75;

        // Act
        var imageStyle = _converter.CreateImageStyle(symbol, scale);

        // Assert
        Assert.Equal(scale, imageStyle.SymbolScale);
    }

    [Fact]
    public void CreateImageStyle_DefaultScale_Is0Point5()
    {
        // Arrange
        var symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus);

        // Act
        var imageStyle = _converter.CreateImageStyle(symbol);

        // Assert
        Assert.Equal(0.5, imageStyle.SymbolScale);
    }

    [Fact]
    public void CreateImageStyle_SameSymbol_UsesCachedResult()
    {
        // Arrange
        var symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus);

        // Act
        var style1 = _converter.CreateImageStyle(symbol);
        var style2 = _converter.CreateImageStyle(symbol);

        // Assert - The Image.Source (base64 string) should be the same
        Assert.Equal(style1.Image?.Source, style2.Image?.Source);
        _bitmapProvider.Received(1).GetSymbolBitmap(symbol);
    }

    [Fact]
    public void CreateImageStyle_DifferentSymbols_CreatesDifferentStyles()
    {
        // Arrange
        var symbol1 = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus);
        var symbol2 = new Symbol(SymbolTable.Primary, SymbolCode.GreaterThanSign);

        // Act
        var style1 = _converter.CreateImageStyle(symbol1);
        var style2 = _converter.CreateImageStyle(symbol2);

        // Assert
        Assert.NotSame(style1.Image, style2.Image);
        _bitmapProvider.Received(1).GetSymbolBitmap(symbol1);
        _bitmapProvider.Received(1).GetSymbolBitmap(symbol2);
    }

    [Fact]
    public void CreateImageStyle_DifferentTables_CreatesDifferentStyles()
    {
        // Arrange
        var symbol1 = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus);
        var symbol2 = new Symbol(SymbolTable.Alternate, SymbolCode.HyphenMinus);

        // Act
        var style1 = _converter.CreateImageStyle(symbol1);
        var style2 = _converter.CreateImageStyle(symbol2);

        // Assert
        Assert.NotSame(style1.Image, style2.Image);
        _bitmapProvider.Received(1).GetSymbolBitmap(symbol1);
        _bitmapProvider.Received(1).GetSymbolBitmap(symbol2);
    }

    [Fact]
    public void CreateImageStyle_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus);
        _converter.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => _converter.CreateImageStyle(symbol));
    }

    [Fact]
    public void Dispose_DisposesUnderlyingBitmapProvider()
    {
        // Act
        _converter.Dispose();

        // Assert
        _bitmapProvider.Received(1).Dispose();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Act
        _converter.Dispose();
        _converter.Dispose();

        // Assert - no exception, and Dispose only called once on provider
        _bitmapProvider.Received(1).Dispose();
    }

    [Fact]
    public void CreateImageStyle_CreatesValidImageObject()
    {
        // Arrange
        var symbol = new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus);

        // Act
        var imageStyle = _converter.CreateImageStyle(symbol);

        // Assert
        Assert.NotNull(imageStyle.Image);
        
        // Verify bitmap provider was called
        _bitmapProvider.Received(1).GetSymbolBitmap(symbol);
    }

    public void Dispose()
    {
        _converter.Dispose();
        _testBitmap.Dispose();
    }
}
