using AgentX.Core.Services.Export;
using AgentX.Core.Services.Export.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Export;

public class ExportTemplateServiceTests
{
    [Fact]
    public void GetTemplates_ReturnsThreeBuiltInTemplates()
    {
        var service = new ExportTemplateService();
        var templates = service.GetTemplates();
        templates.Should().HaveCount(3);
    }

    [Fact]
    public void GetTemplates_ContainsResearchReport()
    {
        var service = new ExportTemplateService();
        var templates = service.GetTemplates();
        templates.Should().Contain(t => t.Id == ExportTemplateId.ResearchReport);
    }

    [Fact]
    public void GetTemplates_ContainsExecutiveSummary()
    {
        var service = new ExportTemplateService();
        var templates = service.GetTemplates();
        templates.Should().Contain(t => t.Id == ExportTemplateId.ExecutiveSummary);
    }

    [Fact]
    public void GetTemplates_ContainsAnnotatedBibliography()
    {
        var service = new ExportTemplateService();
        var templates = service.GetTemplates();
        templates.Should().Contain(t => t.Id == ExportTemplateId.AnnotatedBibliography);
    }

    [Fact]
    public async Task ApplyTemplate_ResearchReport_StructuresContent()
    {
        var service = new ExportTemplateService();
        var messages = new List<TemplateMessage>
        {
            new() { Role = "user", Content = "What is AI?" },
            new() { Role = "assistant", Content = "AI is transforming industries..." }
        };

        var result = await service.ApplyTemplateAsync(
            ExportTemplateId.ResearchReport, messages, "AI Research");
        result.Should().Contain("## Introduction");
        result.Should().Contain("## Findings");
        result.Should().Contain("## Conclusion");
    }

    [Fact]
    public async Task ApplyTemplate_ExecutiveSummary_StructuresContent()
    {
        var service = new ExportTemplateService();
        var messages = new List<TemplateMessage>
        {
            new() { Role = "assistant", Content = "Key finding: AI adoption is accelerating." },
            new() { Role = "assistant", Content = "Second finding: Cost reduction of 30%." }
        };

        var result = await service.ApplyTemplateAsync(
            ExportTemplateId.ExecutiveSummary, messages, "AI Summary");
        result.Should().Contain("## Executive Summary");
        result.Should().Contain("## Key Findings");
    }

    [Fact]
    public async Task ApplyTemplate_AnnotatedBibliography_StructuresContent()
    {
        var service = new ExportTemplateService();
        var messages = new List<TemplateMessage>
        {
            new() { Role = "assistant", Content = "This paper discusses neural networks.", DocumentName = "AI Paper 2024" },
            new() { Role = "assistant", Content = "Further analysis of transformers.", DocumentName = "AI Paper 2024" }
        };

        var result = await service.ApplyTemplateAsync(
            ExportTemplateId.AnnotatedBibliography, messages, "AI Bibliography");
        result.Should().Contain("## Overview");
        result.Should().Contain("## Sources");
        result.Should().Contain("AI Paper 2024");
    }

    [Fact]
    public async Task ApplyTemplate_ResearchReport_IncludesReferences()
    {
        var service = new ExportTemplateService();
        var messages = new List<TemplateMessage>
        {
            new() { Role = "assistant", Content = "Introduction content.", DocumentName = "Source A" },
            new() { Role = "assistant", Content = "Middle finding." },
            new() { Role = "assistant", Content = "Conclusion content." }
        };

        var result = await service.ApplyTemplateAsync(
            ExportTemplateId.ResearchReport, messages, "Test Report");
        result.Should().Contain("## References");
        result.Should().Contain("Source A");
    }

    [Fact]
    public async Task ApplyTemplate_ExecutiveSummary_IncludesRecommendations()
    {
        var service = new ExportTemplateService();
        var messages = new List<TemplateMessage>
        {
            new() { Role = "assistant", Content = "Summary text." },
            new() { Role = "assistant", Content = "Finding one." },
            new() { Role = "assistant", Content = "Recommendation text." }
        };

        var result = await service.ApplyTemplateAsync(
            ExportTemplateId.ExecutiveSummary, messages, "Test Summary");
        result.Should().Contain("## Recommendations");
        result.Should().Contain("Recommendation text.");
    }

    [Fact]
    public async Task ApplyTemplate_UnknownId_ThrowsArgumentOutOfRangeException()
    {
        var service = new ExportTemplateService();
        var messages = new List<TemplateMessage>
        {
            new() { Role = "assistant", Content = "Content" }
        };

        var act = () => service.ApplyTemplateAsync(
            (ExportTemplateId)999, messages, "Invalid");

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ExportOptions_Default_IncludeBranchesIsTrue()
    {
        var options = new ExportOptions();
        options.IncludeBranches.Should().BeTrue();
    }

    [Fact]
    public void ExportOptions_Default_TemplateIdIsNull()
    {
        var options = new ExportOptions();
        options.TemplateId.Should().BeNull();
    }
}
