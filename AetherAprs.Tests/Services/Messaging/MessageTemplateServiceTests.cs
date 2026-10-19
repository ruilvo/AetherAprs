// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.Configuration.Settings;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Messaging;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services.Messaging;

public sealed class MessageTemplateServiceTests
{
    private readonly AppSettings _settings = new();
    private readonly MessageTemplateService _service;

    public MessageTemplateServiceTests()
    {
        var configurationService = Substitute.For<IConfigurationService>();
        configurationService.Settings.Returns(_settings);
        configurationService
            .UpdateSettingsAsync(Arg.Do<Action<AppSettings>>(updateAction => updateAction(_settings)))
            .Returns(Task.CompletedTask);
        _service = new MessageTemplateService(configurationService);
    }

    [Fact]
    public async Task AddAsync_AppendsTemplate()
    {
        var template = new MessageTemplate { Name = "Spot", Destination = "SOTA", Body = "{summit}" };

        await _service.AddAsync(template, TestContext.Current.CancellationToken);

        var stored = Assert.Single(_settings.MessageTemplates.Templates);
        Assert.Equal("Spot", stored.Name);
        Assert.Equal("SOTA", stored.Destination);
        Assert.Equal("{summit}", stored.Body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddAsync_RejectsNullOrBlankName(string? name)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.AddAsync(new MessageTemplate { Name = name!, Destination = "SOTA", Body = "x" }, TestContext.Current.CancellationToken));

        Assert.Empty(_settings.MessageTemplates.Templates);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesByName()
    {
        _settings.MessageTemplates.Templates.Add(
            new MessageTemplate { Name = "Old", Destination = "SOTA", Body = "old body" });
        _settings.MessageTemplates.Templates.Add(
            new MessageTemplate { Name = "Other", Destination = "N0CALL", Body = "other body" });
        var replacement = new MessageTemplate { Name = "New", Destination = "SOTA", Body = "new body" };

        await _service.UpdateAsync("OLD", replacement, TestContext.Current.CancellationToken);

        Assert.Equal(2, _settings.MessageTemplates.Templates.Count);
        Assert.Equal("New", _settings.MessageTemplates.Templates[0].Name);
        Assert.Equal("new body", _settings.MessageTemplates.Templates[0].Body);
        Assert.Equal("Other", _settings.MessageTemplates.Templates[1].Name);
    }

    [Fact]
    public async Task UpdateAsync_NoOp_WhenNameNotFound()
    {
        _settings.MessageTemplates.Templates.Add(
            new MessageTemplate { Name = "Existing", Destination = "SOTA", Body = "existing" });

        await _service.UpdateAsync(
            "Missing",
            new MessageTemplate { Name = "Replacement", Destination = "N0CALL", Body = "replacement" }, TestContext.Current.CancellationToken);

        var stored = Assert.Single(_settings.MessageTemplates.Templates);
        Assert.Equal("Existing", stored.Name);
        Assert.Equal("existing", stored.Body);
    }

    [Fact]
    public async Task DeleteAsync_RemovesByName()
    {
        _settings.MessageTemplates.Templates.Add(
            new MessageTemplate { Name = "Spot", Destination = "SOTA", Body = "spot" });
        _settings.MessageTemplates.Templates.Add(
            new MessageTemplate { Name = "Other", Destination = "N0CALL", Body = "other" });

        await _service.DeleteAsync("spot", TestContext.Current.CancellationToken);

        var stored = Assert.Single(_settings.MessageTemplates.Templates);
        Assert.Equal("Other", stored.Name);
    }

    [Fact]
    public async Task DeleteAsync_NoOp_WhenNameNotFound()
    {
        _settings.MessageTemplates.Templates.Add(
            new MessageTemplate { Name = "Existing", Destination = "SOTA", Body = "existing" });

        await _service.DeleteAsync("Missing", TestContext.Current.CancellationToken);

        var stored = Assert.Single(_settings.MessageTemplates.Templates);
        Assert.Equal("Existing", stored.Name);
    }

    [Fact]
    public void Apply_ReplacesPlaceholdersCaseInsensitively()
    {
        var template = new MessageTemplate { Name = "Spot", Destination = "SOTA", Body = "{summit} {freq} {mode}" };
        var values = new Dictionary<string, string>
        {
            ["SUMMIT"] = "ON/ON-010",
            ["Freq"] = "144.320",
            ["mode"] = "SSB",
        };

        var result = _service.Apply(template, values);

        Assert.Equal("ON/ON-010 144.320 SSB", result);
    }

    [Fact]
    public void Apply_TrimsValues_AndBlanksBecomeEmpty()
    {
        var template = new MessageTemplate { Name = "Spot", Body = "{freq} | {missing} | {blank}" };
        var values = new Dictionary<string, string>
        {
            ["freq"] = "  144.320  ",
            ["blank"] = "   ",
        };

        var result = _service.Apply(template, values);

        Assert.Equal("144.320 |  | ", result);
    }

    [Fact]
    public void Apply_DoesNotResubstituteBracesInValues()
    {
        var template = new MessageTemplate { Name = "Spot", Body = "{summit}" };
        var values = new Dictionary<string, string> { ["summit"] = "a {freq} b" };

        var result = _service.Apply(template, values);

        Assert.Equal("a {freq} b", result);
    }

    [Fact]
    public void Apply_ReturnsNull_WhenResultExceeds67Chars()
    {
        var template = new MessageTemplate { Name = "Spot", Body = "{msg}" };

        var atLimit = _service.Apply(template, new Dictionary<string, string> { ["msg"] = new string('x', 67) });
        var overLimit = _service.Apply(template, new Dictionary<string, string> { ["msg"] = new string('x', 68) });

        Assert.NotNull(atLimit);
        Assert.Equal(67, atLimit!.Length);
        Assert.Null(overLimit);
    }

    [Fact]
    public void GetPlaceholderNames_ReturnsDistinctInOrder()
    {
        var names = _service.GetPlaceholderNames("{b} {a} {b} text {c} {bad-name} {x.y} {a1_B}");

        Assert.Equal(new[] { "b", "a", "c", "a1_B" }, names);
    }

    [Fact]
    public void GetTemplatesForPeer_MatchesBaseIgnoringSsid()
    {
        Assert.True(Callsign.TryParse("SOTA-1", out var peer));
        _settings.MessageTemplates.Templates.Add(
            new MessageTemplate { Name = "Sota", Destination = "SOTA", Body = "x" });
        _settings.MessageTemplates.Templates.Add(
            new MessageTemplate { Name = "Other", Destination = "OTHER", Body = "y" });

        var result = _service.GetTemplatesForPeer(peer);

        var template = Assert.Single(result);
        Assert.Equal("Sota", template.Name);
    }

    [Fact]
    public void GetByName_ReturnsCaseInsensitiveMatch()
    {
        _settings.MessageTemplates.Templates.Add(
            new MessageTemplate { Name = "Spot", Destination = "SOTA", Body = "x" });

        var match = _service.GetByName("spot");

        Assert.NotNull(match);
        Assert.Equal("Spot", match!.Name);
        Assert.Null(_service.GetByName("Missing"));
    }

    [Fact]
    public void Templates_ReturnsDefensiveCopy()
    {
        _settings.MessageTemplates.Templates.Add(
            new MessageTemplate { Name = "Spot", Destination = "SOTA", Body = "x" });

        var templates = _service.Templates;

        Assert.NotSame(_settings.MessageTemplates.Templates, templates);
        Assert.Equal(
            _settings.MessageTemplates.Templates.Select(t => t.Name).ToList(),
            templates.Select(t => t.Name).ToList());
    }
}
