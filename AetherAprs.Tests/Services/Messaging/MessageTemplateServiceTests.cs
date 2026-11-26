// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    private AppSettings _settings = new();
    private readonly MessageTemplateService _service;

    public MessageTemplateServiceTests()
    {
        IConfigurationService configurationService = Substitute.For<IConfigurationService>();
        configurationService.Current.Returns(_ => _settings);
        configurationService
            .UpdateAsync(
                Arg.Any<Func<AppSettings, AppSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _settings = call.Arg<Func<AppSettings, AppSettings>>()(_settings);
                return Task.FromResult(_settings);
            });
        _service = new MessageTemplateService(configurationService);
    }

    private void AddTemplate(MessageTemplate template) =>
        _settings = _settings with
        {
            MessageTemplates = _settings.MessageTemplates with
            {
                Templates = [.. _settings.MessageTemplates.Templates, template],
            },
        };

    [Fact]
    public async Task AddAsync_AppendsTemplate()
    {
        MessageTemplate template = new() { Name = "Spot", Destination = "SOTA", Body = "{summit}" };

        await _service.AddAsync(template, TestContext.Current.CancellationToken);

        MessageTemplate stored = Assert.Single(_settings.MessageTemplates.Templates);
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
        AddTemplate(
            new MessageTemplate { Name = "Old", Destination = "SOTA", Body = "old body" });
        AddTemplate(
            new MessageTemplate { Name = "Other", Destination = "N0CALL", Body = "other body" });
        MessageTemplate replacement = new() { Name = "New", Destination = "SOTA", Body = "new body" };

        await _service.UpdateAsync("OLD", replacement, TestContext.Current.CancellationToken);

        Assert.Equal(2, _settings.MessageTemplates.Templates.Count);
        Assert.Equal("New", _settings.MessageTemplates.Templates[0].Name);
        Assert.Equal("new body", _settings.MessageTemplates.Templates[0].Body);
        Assert.Equal("Other", _settings.MessageTemplates.Templates[1].Name);
    }

    [Fact]
    public async Task UpdateAsync_NoOp_WhenNameNotFound()
    {
        AddTemplate(
            new MessageTemplate { Name = "Existing", Destination = "SOTA", Body = "existing" });

        await _service.UpdateAsync(
            "Missing",
            new MessageTemplate { Name = "Replacement", Destination = "N0CALL", Body = "replacement" }, TestContext.Current.CancellationToken);

        MessageTemplate stored = Assert.Single(_settings.MessageTemplates.Templates);
        Assert.Equal("Existing", stored.Name);
        Assert.Equal("existing", stored.Body);
    }

    [Fact]
    public async Task DeleteAsync_RemovesByName()
    {
        AddTemplate(
            new MessageTemplate { Name = "Spot", Destination = "SOTA", Body = "spot" });
        AddTemplate(
            new MessageTemplate { Name = "Other", Destination = "N0CALL", Body = "other" });

        await _service.DeleteAsync("spot", TestContext.Current.CancellationToken);

        MessageTemplate stored = Assert.Single(_settings.MessageTemplates.Templates);
        Assert.Equal("Other", stored.Name);
    }

    [Fact]
    public async Task DeleteAsync_NoOp_WhenNameNotFound()
    {
        AddTemplate(
            new MessageTemplate { Name = "Existing", Destination = "SOTA", Body = "existing" });

        await _service.DeleteAsync("Missing", TestContext.Current.CancellationToken);

        MessageTemplate stored = Assert.Single(_settings.MessageTemplates.Templates);
        Assert.Equal("Existing", stored.Name);
    }

    [Fact]
    public void Apply_ReplacesPlaceholdersCaseInsensitively()
    {
        MessageTemplate template = new() { Name = "Spot", Destination = "SOTA", Body = "{summit} {freq} {mode}" };
        Dictionary<string, string> values = new()
        {
            ["SUMMIT"] = "ON/ON-010",
            ["Freq"] = "144.320",
            ["mode"] = "SSB",
        };

        string? result = _service.Apply(template, values);

        Assert.Equal("ON/ON-010 144.320 SSB", result);
    }

    [Fact]
    public void Apply_TrimsValues_AndBlanksBecomeEmpty()
    {
        MessageTemplate template = new() { Name = "Spot", Body = "{freq} | {missing} | {blank}" };
        Dictionary<string, string> values = new()
        {
            ["freq"] = "  144.320  ",
            ["blank"] = "   ",
        };

        string? result = _service.Apply(template, values);

        Assert.Equal("144.320 |  | ", result);
    }

    [Fact]
    public void Apply_DoesNotResubstituteBracesInValues()
    {
        MessageTemplate template = new() { Name = "Spot", Body = "{summit}" };
        Dictionary<string, string> values = new()
        { ["summit"] = "a {freq} b" };

        string? result = _service.Apply(template, values);

        Assert.Equal("a {freq} b", result);
    }

    [Fact]
    public void Apply_ReturnsNull_WhenResultExceeds67Chars()
    {
        MessageTemplate template = new() { Name = "Spot", Body = "{msg}" };

        string? atLimit = _service.Apply(template, new Dictionary<string, string> { ["msg"] = new string('x', 67) });
        string? overLimit = _service.Apply(template, new Dictionary<string, string> { ["msg"] = new string('x', 68) });

        Assert.NotNull(atLimit);
        Assert.Equal(67, atLimit!.Length);
        Assert.Null(overLimit);
    }

    [Fact]
    public void GetPlaceholderNames_ReturnsDistinctInOrder()
    {
        IReadOnlyList<string> names = _service.GetPlaceholderNames("{b} {a} {b} text {c} {bad-name} {x.y} {a1_B}");

        Assert.Equal(["b", "a", "c", "a1_B"], names);
    }

    [Fact]
    public void GetTemplatesForPeer_MatchesBaseIgnoringSsid()
    {
        Assert.True(AprsAddress.TryParse("SOTA-1", out AprsAddress peer));
        AddTemplate(
            new MessageTemplate { Name = "Sota", Destination = "SOTA", Body = "x" });
        AddTemplate(
            new MessageTemplate { Name = "Other", Destination = "OTHER", Body = "y" });

        IReadOnlyList<MessageTemplate> result = _service.GetTemplatesForPeer(peer);

        MessageTemplate template = Assert.Single(result);
        Assert.Equal("Sota", template.Name);
    }

    [Fact]
    public void GetTemplatesForPeer_MatchesATacticalAddress()
    {
        Assert.True(AprsAddress.TryParse("APRS2SOTA", out AprsAddress peer));
        AddTemplate(
            new MessageTemplate { Name = "Sota spot", Destination = "APRS2SOTA", Body = "x" });
        AddTemplate(
            new MessageTemplate { Name = "Other", Destination = "EMAIL", Body = "y" });

        IReadOnlyList<MessageTemplate> result = _service.GetTemplatesForPeer(peer);

        MessageTemplate template = Assert.Single(result);
        Assert.Equal("Sota spot", template.Name);
    }

    [Fact]
    public void GetByName_ReturnsCaseInsensitiveMatch()
    {
        AddTemplate(
            new MessageTemplate { Name = "Spot", Destination = "SOTA", Body = "x" });

        MessageTemplate? match = _service.GetByName("spot");

        Assert.NotNull(match);
        Assert.Equal("Spot", match!.Name);
        Assert.Null(_service.GetByName("Missing"));
    }

    [Fact]
    public void Templates_ReturnsDefensiveCopy()
    {
        AddTemplate(
            new MessageTemplate { Name = "Spot", Destination = "SOTA", Body = "x" });

        IReadOnlyList<MessageTemplate> templates = _service.Templates;

        Assert.NotSame(_settings.MessageTemplates.Templates, templates);
        Assert.Equal(
            _settings.MessageTemplates.Templates.Select(t => t.Name).ToList(),
            [.. templates.Select(t => t.Name)]);
    }
}
