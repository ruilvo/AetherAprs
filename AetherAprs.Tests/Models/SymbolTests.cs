// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Models.Aprs;
using Xunit;

namespace AetherAprs.Tests.Models;

public class SymbolTests
{
    [Fact]
    public void Constructor_SetsTableAndCode()
    {
        var symbol = new Symbol(SymbolTable.Primary, SymbolCode.GreaterThanSign);
        Assert.Equal(SymbolTable.Primary, symbol.Table);
        Assert.Equal(SymbolCode.GreaterThanSign, symbol.Code);
    }

    [Fact]
    public void Constructor_WithOverlay_SetsOverlay()
    {
        var symbol = new Symbol(SymbolTable.Alternate, SymbolCode.Exclamation, 'L');
        Assert.Equal(SymbolTable.Alternate, symbol.Table);
        Assert.Equal(SymbolCode.Exclamation, symbol.Code);
        Assert.Equal('L', symbol.Overlay);
    }

    [Fact]
    public void Constructor_WithoutOverlay_OverlayIsNull()
    {
        var symbol = new Symbol(SymbolTable.Primary, SymbolCode.GreaterThanSign);
        Assert.Null(symbol.Overlay);
    }

    [Fact]
    public void TableChar_ReturnsCorrectChar()
    {
        Assert.Equal('/', new Symbol(SymbolTable.Primary, SymbolCode.NumberSign).TableChar);
        Assert.Equal('\\', new Symbol(SymbolTable.Alternate, SymbolCode.NumberSign).TableChar);
    }

    [Fact]
    public void CodeChar_ReturnsCorrectChar()
    {
        Assert.Equal('>', new Symbol(SymbolTable.Primary, SymbolCode.GreaterThanSign).CodeChar);
        Assert.Equal('#', new Symbol(SymbolTable.Primary, SymbolCode.NumberSign).CodeChar);
    }

    [Fact]
    public void Equality_SameTableAndCode_AreEqual()
    {
        var a = new Symbol(SymbolTable.Primary, SymbolCode.GreaterThanSign);
        var b = new Symbol(SymbolTable.Primary, SymbolCode.GreaterThanSign);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_SameTableCodeAndOverlay_AreEqual()
    {
        var a = new Symbol(SymbolTable.Alternate, SymbolCode.Exclamation, 'L');
        var b = new Symbol(SymbolTable.Alternate, SymbolCode.Exclamation, 'L');
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentTable_AreNotEqual()
    {
        var a = new Symbol(SymbolTable.Primary, SymbolCode.GreaterThanSign);
        var b = new Symbol(SymbolTable.Alternate, SymbolCode.GreaterThanSign);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_DifferentCode_AreNotEqual()
    {
        var a = new Symbol(SymbolTable.Primary, SymbolCode.GreaterThanSign);
        var b = new Symbol(SymbolTable.Primary, SymbolCode.LessThanSign);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_DifferentOverlay_AreNotEqual()
    {
        var a = new Symbol(SymbolTable.Alternate, SymbolCode.Exclamation, 'L');
        var b = new Symbol(SymbolTable.Alternate, SymbolCode.Exclamation, 'R');
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void AlternateTable_Works()
    {
        var symbol = new Symbol(SymbolTable.Alternate, SymbolCode.Exclamation);
        Assert.Equal(SymbolTable.Alternate, symbol.Table);
        Assert.Equal(SymbolCode.Exclamation, symbol.Code);
        Assert.Equal('\\', symbol.TableChar);
        Assert.Equal('!', symbol.CodeChar);
    }

    [Fact]
    public void SymbolTable_ToChar_ReturnsCorrectChar()
    {
        Assert.Equal('/', SymbolTable.Primary.ToChar());
        Assert.Equal('\\', SymbolTable.Alternate.ToChar());
    }

    [Fact]
    public void SymbolTable_FromChar_ReturnsCorrectTable()
    {
        Assert.Equal(SymbolTable.Primary, '/'.ToSymbolTable());
        Assert.Equal(SymbolTable.Alternate, '\\'.ToSymbolTable());
    }

    [Fact]
    public void SymbolCode_ToChar_ReturnsCorrectChar()
    {
        Assert.Equal('#', SymbolCode.NumberSign.ToChar());
        Assert.Equal('>', SymbolCode.GreaterThanSign.ToChar());
        Assert.Equal('/', SymbolCode.Solidus.ToChar());
    }

    [Fact]
    public void SymbolCode_FromChar_ReturnsCorrectCode()
    {
        Assert.Equal(SymbolCode.NumberSign, '#'.ToSymbolCode());
        Assert.Equal(SymbolCode.GreaterThanSign, '>'.ToSymbolCode());
        Assert.Equal(SymbolCode.Space, ' '.ToSymbolCode());
        Assert.Equal(SymbolCode.Tilde, '~'.ToSymbolCode());
    }

    [Fact]
    public void SymbolCode_FromInvalidChar_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => '\x1F'.ToSymbolCode());
        Assert.Throws<ArgumentOutOfRangeException>(() => '\x7F'.ToSymbolCode());
    }
}