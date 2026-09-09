// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using Xunit;

namespace AetherAprs.Tests.Models;

public class SymbolTests
{
    [Fact]
    public void Constructor_SetsTableAndCode()
    {
        var symbol = new Symbol('/', '>');
        Assert.Equal('/', symbol.Table);
        Assert.Equal('>', symbol.Code);
    }

    [Fact]
    public void Equality_SameTableAndCode_AreEqual()
    {
        var a = new Symbol('/', '>');
        var b = new Symbol('/', '>');
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentTable_AreNotEqual()
    {
        var a = new Symbol('/', '>');
        var b = new Symbol('\\', '>');
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_DifferentCode_AreNotEqual()
    {
        var a = new Symbol('/', '>');
        var b = new Symbol('/', '<');
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void AlternateTable_Works()
    {
        var symbol = new Symbol('\\', '!');
        Assert.Equal('\\', symbol.Table);
        Assert.Equal('!', symbol.Code);
    }
}