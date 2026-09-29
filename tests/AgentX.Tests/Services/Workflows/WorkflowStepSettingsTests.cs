using AgentX.Core.Services.Workflows;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Workflows;

/// <summary>
/// The settings check the workflow builder runs as step settings are typed. Each case mirrors
/// how <see cref="WorkflowEngine"/> reads the settings; WorkflowEngineTests holds the cases that
/// run the engine itself on settings the check accepts or rejects.
/// </summary>
public sealed class WorkflowStepSettingsTests
{
    [Fact]
    public void Step_types_are_the_five_the_engine_runs()
    {
        WorkflowStepSettings.StepTypes.Should().Equal(
            "AiPrompt", "DocumentLookup", "TextTransform", "ConditionalBranch", "OutputFormat");
    }

    [Theory]
    [InlineData("AiPrompt", false, false)]
    [InlineData("DocumentLookup", true, false)]
    [InlineData("TextTransform", true, false)]
    [InlineData("ConditionalBranch", true, true)]
    [InlineData("OutputFormat", true, false)]
    [InlineData("CustomPluginStep", false, false)]
    [InlineData(null, false, false)]
    public void Only_the_types_the_engine_reads_settings_for_have_settings(string? stepType, bool hasSettings, bool requiresSettings)
    {
        WorkflowStepSettings.HasSettings(stepType).Should().Be(hasSettings);
        WorkflowStepSettings.RequiresSettings(stepType).Should().Be(requiresSettings);
        (WorkflowStepSettings.Example(stepType).Length > 0).Should().Be(hasSettings);
    }

    [Theory]
    [InlineData("DocumentLookup")]
    [InlineData("TextTransform")]
    [InlineData("ConditionalBranch")]
    [InlineData("OutputFormat")]
    public void Every_example_passes_the_check(string stepType)
    {
        WorkflowStepSettings.Validate(stepType, WorkflowStepSettings.Example(stepType)).Should().BeNull();
    }

    [Theory]
    [InlineData("AiPrompt", "not json at all")]
    [InlineData("CustomPluginStep", "{")]
    [InlineData(null, "[1]")]
    public void Settings_of_a_type_that_reads_none_are_not_checked(string? stepType, string configJson)
    {
        WorkflowStepSettings.Validate(stepType, configJson).Should().BeNull();
    }

    [Theory]
    [InlineData("DocumentLookup")]
    [InlineData("TextTransform")]
    [InlineData("OutputFormat")]
    public void Optional_settings_may_be_left_empty(string stepType)
    {
        WorkflowStepSettings.Validate(stepType, null).Should().BeNull();
        WorkflowStepSettings.Validate(stepType, "").Should().BeNull();
        WorkflowStepSettings.Validate(stepType, "  \n ").Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_conditional_branch_requires_settings(string? configJson)
    {
        Kind("ConditionalBranch", configJson).Should().Be(WorkflowStepSettingsProblemKind.Required);
    }

    [Fact]
    public void Json_that_does_not_parse_reports_its_line_and_position()
    {
        var problem = WorkflowStepSettings.Validate("TextTransform", "{\n  \"transform\": lowercase\n}");

        problem.Should().NotBeNull();
        problem!.Kind.Should().Be(WorkflowStepSettingsProblemKind.InvalidJson);
        problem.Line.Should().Be(2);
        problem.Position.Should().Be(16);
    }

    [Theory]
    [InlineData("{\"transform\": \"lowercase\",}")]
    [InlineData("{\"transform\": \"lowercase\"} // lower")]
    [InlineData("{'transform': 'lowercase'}")]
    public void Json_the_engine_parser_rejects_is_invalid(string configJson)
    {
        // The engine parses with the defaults: no trailing commas, comments or single quotes.
        Kind("TextTransform", configJson).Should().Be(WorkflowStepSettingsProblemKind.InvalidJson);
    }

    [Theory]
    [InlineData("DocumentLookup", "3")]
    [InlineData("TextTransform", "\"lowercase\"")]
    [InlineData("ConditionalBranch", "[{\"condition\": \"contains\"}]")]
    [InlineData("OutputFormat", "null")]
    public void Settings_must_be_an_object(string stepType, string configJson)
    {
        var problem = WorkflowStepSettings.Validate(stepType, configJson);

        problem!.Kind.Should().Be(WorkflowStepSettingsProblemKind.NotAnObject);
        problem.Example.Should().Be(WorkflowStepSettings.Example(stepType));
    }

    [Theory]
    [InlineData("{\"collectionId\": 3}")]
    [InlineData("{\"collectionId\": -1}")]
    [InlineData("{}")]
    public void A_whole_number_collection_id_passes(string configJson)
    {
        WorkflowStepSettings.Validate("DocumentLookup", configJson).Should().BeNull();
    }

    [Theory]
    [InlineData("{\"collectionId\": \"3\"}")]
    [InlineData("{\"collectionId\": 2.5}")]
    [InlineData("{\"collectionId\": 3.0}")]
    [InlineData("{\"collectionId\": null}")]
    [InlineData("{\"collectionId\": true}")]
    public void A_collection_id_that_is_not_a_whole_number_is_reported(string configJson)
    {
        var problem = WorkflowStepSettings.Validate("DocumentLookup", configJson);

        problem!.Kind.Should().Be(WorkflowStepSettingsProblemKind.NotAWholeNumber);
        problem.Setting.Should().Be("collectionId");
        problem.Example.Should().Be("3");
    }

    [Fact]
    public void Every_transform_passes_in_any_letter_case()
    {
        foreach (var transform in WorkflowStepSettings.TextTransforms)
        {
            WorkflowStepSettings.Validate("TextTransform", $"{{\"transform\": \"{transform}\"}}").Should().BeNull(transform);
            WorkflowStepSettings.Validate("TextTransform", $"{{\"transform\": \"{transform.ToUpperInvariant()}\"}}").Should().BeNull(transform);
        }

        // A null transform is the default, uppercase.
        WorkflowStepSettings.Validate("TextTransform", "{\"transform\": null}").Should().BeNull();
    }

    [Theory]
    [InlineData("rot13")]
    [InlineData("")]
    [InlineData("upper case")]
    public void An_unknown_transform_is_reported_with_the_choices(string transform)
    {
        var problem = WorkflowStepSettings.Validate("TextTransform", $"{{\"transform\": \"{transform}\"}}");

        problem!.Kind.Should().Be(WorkflowStepSettingsProblemKind.UnknownChoice);
        problem.Setting.Should().Be("transform");
        problem.Value.Should().Be(transform);
        problem.Choices.Should().Equal(WorkflowStepSettings.TextTransforms);
    }

    [Theory]
    [InlineData("{\"transform\": 5}")]
    [InlineData("{\"transform\": [\"lowercase\"]}")]
    [InlineData("{\"transform\": {\"name\": \"lowercase\"}}")]
    public void A_transform_that_is_not_text_is_reported(string configJson)
    {
        var problem = WorkflowStepSettings.Validate("TextTransform", configJson);

        problem!.Kind.Should().Be(WorkflowStepSettingsProblemKind.NotText);
        problem.Setting.Should().Be("transform");
    }

    [Theory]
    [InlineData("TextTransform", "{\"Transform\": \"lowercase\"}", "Transform", new[] { "transform" })]
    [InlineData("DocumentLookup", "{\"collection\": 3}", "collection", new[] { "collectionId" })]
    [InlineData("ConditionalBranch", "{\"condition\": \"contains\", \"value\": \"x\", \"true\": \"y\"}", "true", new[] { "condition", "value", "trueBranch", "falseBranch" })]
    [InlineData("OutputFormat", "{\"format\": \"json\", \"header\": \"x\"}", "header", new[] { "format", "prefix", "suffix" })]
    public void A_setting_the_step_does_not_read_is_reported(string stepType, string configJson, string setting, string[] settings)
    {
        var problem = WorkflowStepSettings.Validate(stepType, configJson);

        problem!.Kind.Should().Be(WorkflowStepSettingsProblemKind.UnknownSetting);
        problem.Setting.Should().Be(setting);
        problem.Choices.Should().Equal(settings);
    }

    [Fact]
    public void Every_condition_passes_in_any_letter_case()
    {
        foreach (var condition in WorkflowStepSettings.Conditions)
        {
            var value = condition == "length_greater_than" ? "10" : "x";
            WorkflowStepSettings.Validate("ConditionalBranch", Branch(condition, value)).Should().BeNull(condition);
            WorkflowStepSettings.Validate("ConditionalBranch", Branch(condition.ToUpperInvariant(), value)).Should().BeNull(condition);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"value\": \"urgent\"}")]
    [InlineData("{\"condition\": null, \"value\": null, \"trueBranch\": null, \"falseBranch\": null}")]
    public void A_branch_may_leave_out_what_has_a_default(string configJson)
    {
        // The engine defaults the condition to contains, the value to empty, and each branch
        // to the previous output.
        WorkflowStepSettings.Validate("ConditionalBranch", configJson).Should().BeNull();
    }

    [Fact]
    public void An_unknown_condition_is_reported_with_the_choices()
    {
        var problem = WorkflowStepSettings.Validate("ConditionalBranch", Branch("has", "x"));

        problem!.Kind.Should().Be(WorkflowStepSettingsProblemKind.UnknownChoice);
        problem.Setting.Should().Be("condition");
        problem.Value.Should().Be("has");
        problem.Choices.Should().Equal(WorkflowStepSettings.Conditions);
    }

    [Theory]
    [InlineData("{\"condition\": 1}", "condition")]
    [InlineData("{\"condition\": \"length_greater_than\", \"value\": 100}", "value")]
    [InlineData("{\"condition\": \"contains\", \"value\": \"x\", \"trueBranch\": true}", "trueBranch")]
    [InlineData("{\"condition\": \"contains\", \"value\": \"x\", \"falseBranch\": [\"no\"]}", "falseBranch")]
    public void A_branch_setting_that_is_not_text_is_reported(string configJson, string setting)
    {
        var problem = WorkflowStepSettings.Validate("ConditionalBranch", configJson);

        problem!.Kind.Should().Be(WorkflowStepSettingsProblemKind.NotText);
        problem.Setting.Should().Be(setting);
    }

    [Theory]
    [InlineData("matches", "(unclosed")]
    [InlineData("MATCHES", "[a-")]
    public void A_pattern_that_does_not_parse_is_reported(string condition, string pattern)
    {
        var problem = WorkflowStepSettings.Validate("ConditionalBranch", Branch(condition, pattern));

        problem!.Kind.Should().Be(WorkflowStepSettingsProblemKind.InvalidPattern);
        problem.Setting.Should().Be("value");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("10.5")]
    [InlineData("99999999999")]
    public void A_length_that_is_not_a_whole_number_is_reported(string length)
    {
        var problem = WorkflowStepSettings.Validate("ConditionalBranch", Branch("length_greater_than", length));

        problem!.Kind.Should().Be(WorkflowStepSettingsProblemKind.NotAWholeNumber);
        problem.Setting.Should().Be("value");
        problem.Example.Should().Be("\"100\"");
    }

    [Fact]
    public void Every_format_passes_and_prefix_and_suffix_are_text()
    {
        foreach (var format in WorkflowStepSettings.OutputFormats)
        {
            WorkflowStepSettings.Validate("OutputFormat", $"{{\"format\": \"{format}\"}}").Should().BeNull(format);
        }

        WorkflowStepSettings.Validate("OutputFormat", "{\"prefix\": \"Summary:\\n\", \"suffix\": \"\\n-- end\"}").Should().BeNull();
        WorkflowStepSettings.Validate("OutputFormat", "{\"format\": \"\"}").Should().BeNull();
        WorkflowStepSettings.Validate("OutputFormat", "{\"format\": null}").Should().BeNull();
    }

    [Theory]
    [InlineData("JSON")]
    [InlineData("Markdown")]
    [InlineData("table")]
    public void A_format_must_match_exactly(string format)
    {
        // The engine matches the format with its exact letter case and passes anything else
        // through unformatted.
        var problem = WorkflowStepSettings.Validate("OutputFormat", $"{{\"format\": \"{format}\"}}");

        problem!.Kind.Should().Be(WorkflowStepSettingsProblemKind.UnknownChoice);
        problem.Setting.Should().Be("format");
        problem.Choices.Should().Equal(WorkflowStepSettings.OutputFormats);
    }

    [Theory]
    [InlineData("{\"format\": 1}", "format")]
    [InlineData("{\"prefix\": 1}", "prefix")]
    [InlineData("{\"format\": \"json\", \"suffix\": false}", "suffix")]
    public void A_format_setting_that_is_not_text_is_reported(string configJson, string setting)
    {
        var problem = WorkflowStepSettings.Validate("OutputFormat", configJson);

        problem!.Kind.Should().Be(WorkflowStepSettingsProblemKind.NotText);
        problem.Setting.Should().Be(setting);
    }

    private static WorkflowStepSettingsProblemKind? Kind(string stepType, string? configJson) =>
        WorkflowStepSettings.Validate(stepType, configJson)?.Kind;

    private static string Branch(string condition, string value) =>
        System.Text.Json.JsonSerializer.Serialize(new { condition, value, trueBranch = "yes", falseBranch = "no" });
}
