// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using AetherAprs.ViewModels;
using SkiaSharp;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class AddEditPortDialogViewModelTests
{
    [Fact]
    public void DefaultsUseHumanSymbolAndCreatePreview()
    {
        using var provider = new TestSymbolBitmapProvider();
        var viewModel = CreateViewModel(provider);

        Assert.Equal("/", viewModel.SymbolTableCharacter);
        Assert.Equal("[", viewModel.SymbolCodeCharacter);
        Assert.True(viewModel.IsSymbolValid);
        Assert.Null(viewModel.SymbolPreview);
        Assert.Equal(1, provider.RequestCount);

        var config = viewModel.BuildConfig();
        Assert.Equal("/", config.SymbolTableCharacter);
        Assert.Equal("[", config.SymbolCodeCharacter);
    }

    [Fact]
    public void ChangingSymbolCharactersUpdatesPreviewAndConfig()
    {
        using var provider = new TestSymbolBitmapProvider();
        var viewModel = CreateViewModel(provider);

        viewModel.SymbolTableCharacter = "\\";
        viewModel.SymbolCodeCharacter = ">";

        Assert.True(viewModel.IsSymbolValid);
        Assert.Null(viewModel.SymbolPreview);
        Assert.Equal(3, provider.RequestCount);
        var config = viewModel.BuildConfig();
        Assert.Equal("\\", config.SymbolTableCharacter);
        Assert.Equal(">", config.SymbolCodeCharacter);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("x")]
    public void InvalidTableCharacterDisablesSymbolAndRejectsConfig(string tableCharacter)
    {
        using var provider = new TestSymbolBitmapProvider();
        var viewModel = CreateViewModel(provider);
        viewModel.SymbolTableCharacter = tableCharacter;

        if (tableCharacter == "/")
        {
            Assert.True(viewModel.IsSymbolValid);
            Assert.Equal(1, provider.RequestCount);
            return;
        }

        Assert.False(viewModel.IsSymbolValid);
        Assert.Null(viewModel.SymbolPreview);
        Assert.Throws<InvalidOperationException>(() => viewModel.BuildConfig());
    }

    [Fact]
    public void InvalidCodeCharacterDisablesSymbolAndRejectsConfig()
    {
        using var provider = new TestSymbolBitmapProvider();
        var viewModel = CreateViewModel(provider);
        viewModel.SymbolCodeCharacter = "\x1F";

        Assert.False(viewModel.IsSymbolValid);
        Assert.Null(viewModel.SymbolPreview);
        Assert.Throws<InvalidOperationException>(() => viewModel.BuildConfig());
    }

    private sealed class TestSymbolBitmapProvider : IAprsSymbolBitmapProvider, IDisposable
    {
        public int RequestCount { get; private set; }

        public SKBitmap GetSymbolBitmap(Symbol symbol)
        {
            RequestCount++;
            return new SKBitmap(8, 8);
        }

        public void Dispose()
        {
        }
    }

    private static AddEditPortDialogViewModel CreateViewModel(TestSymbolBitmapProvider provider) =>
        new("CT7ALW", 1, provider, _ => null);
}
