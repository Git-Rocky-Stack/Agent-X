using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentX.Core.Services.Workflows;

/// <summary>
/// The settings each workflow step type reads from its
/// <see cref="AgentX.Core.Data.Entities.WorkflowStepEntity.ConfigJson"/>, and a check of settings
/// against the way <see cref="WorkflowEngine"/> reads them. The workflow builder runs the check
/// while the settings are typed, so a mistake is reported there instead of failing the run or
/// being silently ignored by it.
/// </summary>
public static class WorkflowStepSettings
{
    public const string AiPrompt = "AiPrompt";
    public const string DocumentLookup = "DocumentLookup";
    public const string TextTransform = "TextTransform";
    public const string ConditionalBranch = "ConditionalBranch";
    public const string OutputFormat = "OutputFormat";

    /// <summary>The step types the engine runs, in the order the builder offers them.</summary>
    public static IReadOnlyList<string> StepTypes { get; } =
        [AiPrompt, DocumentLookup, TextTransform, ConditionalBranch, OutputFormat];

    /// <summary>The values of a text transform's "transform" setting (any letter case).</summary>
    public static IReadOnlyList<string> TextTransforms { get; } =
    [
        "uppercase", "lowercase", "titlecase", "trim", "extract_lines", "word_count",
        "char_count", "reverse_lines", "deduplicate_lines", "sort_lines", "number_lines",
    ];

    /// <summary>The values of a conditional branch's "condition" setting (any letter case).</summary>
    public static IReadOnlyList<string> Conditions { get; } =
        ["contains", "not_contains", "starts_with", "ends_with", "equals", "matches", "length_greater_than"];

    /// <summary>The values of an output format's "format" setting (exact letter case).</summary>
    public static IReadOnlyList<string> OutputFormats { get; } =
        ["json", "markdown", "html", "bullet_list", "numbered_list"];

    private static readonly IReadOnlyList<string> DocumentLookupSettings = ["collectionId"];
    private static readonly IReadOnlyList<string> TextTransformSettings = ["transform"];
    private static readonly IReadOnlyList<string> ConditionalBranchSettings = ["condition", "value", "trueBranch", "falseBranch"];
    private static readonly IReadOnlyList<string> OutputFormatSettings = ["format", "prefix", "suffix"];

    /// <summary>True when the engine reads settings for steps of this type.</summary>
    public static bool HasSettings(string? stepType) =>
        stepType is DocumentLookup or TextTransform or ConditionalBranch or OutputFormat;

    /// <summary>True when a step of this type fails without settings.</summary>
    public static bool RequiresSettings(string? stepType) => stepType == ConditionalBranch;

    /// <summary>A working example of the settings for this step type, or empty when it has none.</summary>
    public static string Example(string? stepType) => stepType switch
    {
        DocumentLookup => "{\"collectionId\": 3}",
        TextTransform => "{\"transform\": \"lowercase\"}",
        ConditionalBranch => "{\"condition\": \"contains\", \"value\": \"urgent\", \"trueBranch\": \"Urgent: {{previous_output}}\", \"falseBranch\": \"{{previous_output}}\"}",
        OutputFormat => "{\"format\": \"bullet_list\"}",
        _ => string.Empty,
    };

    /// <summary>
    /// Checks the settings of a step, reading them the way the engine does. Returns the first
    /// problem, or null when the engine can use the settings as written. A problem is anything
    /// that fails the step (settings a conditional branch lacks, a value of the wrong kind, a
    /// transform or pattern the engine rejects) or that the engine would silently ignore
    /// (settings that are not valid JSON, a setting name it does not read, a condition or
    /// format it does not know, a collection id that is not a whole number).
    /// </summary>
    public static WorkflowStepSettingsProblem? Validate(string? stepType, string? configJson)
    {
        if (!HasSettings(stepType))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(configJson))
        {
            return RequiresSettings(stepType)
                ? new WorkflowStepSettingsProblem(WorkflowStepSettingsProblemKind.Required)
                : null;
        }

        JsonDocument document;
        try
        {
            // The engine's parser, with its defaults: no comments, no trailing commas.
            document = JsonDocument.Parse(configJson);
        }
        catch (JsonException ex)
        {
            return new WorkflowStepSettingsProblem(WorkflowStepSettingsProblemKind.InvalidJson)
            {
                Line = (ex.LineNumber ?? 0) + 1,
                Position = (ex.BytePositionInLine ?? 0) + 1,
            };
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new WorkflowStepSettingsProblem(WorkflowStepSettingsProblemKind.NotAnObject)
                {
                    Example = Example(stepType),
                };
            }

            var (problem, settingNames) = stepType switch
            {
                DocumentLookup => (CheckDocumentLookup(root), DocumentLookupSettings),
                TextTransform => (CheckTextTransform(root), TextTransformSettings),
                ConditionalBranch => (CheckConditionalBranch(root), ConditionalBranchSettings),
                _ => (CheckOutputFormat(root), OutputFormatSettings),
            };

            return problem ?? CheckSettingNames(root, settingNames);
        }
    }

    private static WorkflowStepSettingsProblem? CheckDocumentLookup(JsonElement root)
    {
        // The engine reads the id with TryGetInt64: a value that is not a number fails the
        // step, and a number that is not a whole number is ignored (every document is searched).
        if (root.TryGetProperty("collectionId", out var collectionId)
            && !(collectionId.ValueKind == JsonValueKind.Number && collectionId.TryGetInt64(out _)))
        {
            return new WorkflowStepSettingsProblem(WorkflowStepSettingsProblemKind.NotAWholeNumber)
            {
                Setting = "collectionId",
                Example = "3",
            };
        }

        return null;
    }

    private static WorkflowStepSettingsProblem? CheckTextTransform(JsonElement root)
    {
        if (!TryReadText(root, "transform", out var transform, out var problem))
        {
            return problem;
        }

        return transform is null || TextTransforms.Contains(transform, StringComparer.OrdinalIgnoreCase)
            ? null
            : UnknownChoice("transform", transform, TextTransforms);
    }

    private static WorkflowStepSettingsProblem? CheckConditionalBranch(JsonElement root)
    {
        if (!TryReadText(root, "condition", out var condition, out var problem)
            || !TryReadText(root, "value", out var value, out problem)
            || !TryReadText(root, "trueBranch", out _, out problem)
            || !TryReadText(root, "falseBranch", out _, out problem))
        {
            return problem;
        }

        // A missing condition is "contains" and a missing value is empty, as in the engine.
        condition ??= "contains";
        value ??= string.Empty;

        if (!Conditions.Contains(condition, StringComparer.OrdinalIgnoreCase))
        {
            return UnknownChoice("condition", condition, Conditions);
        }

        if (string.Equals(condition, "matches", StringComparison.OrdinalIgnoreCase) && !IsValidPattern(value))
        {
            return new WorkflowStepSettingsProblem(WorkflowStepSettingsProblemKind.InvalidPattern) { Setting = "value" };
        }

        // The engine compares lengths only when the value parses as a whole number; otherwise
        // the condition is never met.
        if (string.Equals(condition, "length_greater_than", StringComparison.OrdinalIgnoreCase)
            && !int.TryParse(value, out _))
        {
            return new WorkflowStepSettingsProblem(WorkflowStepSettingsProblemKind.NotAWholeNumber)
            {
                Setting = "value",
                Example = "\"100\"",
            };
        }

        return null;
    }

    private static WorkflowStepSettingsProblem? CheckOutputFormat(JsonElement root)
    {
        if (!TryReadText(root, "format", out var format, out var problem)
            || !TryReadText(root, "prefix", out _, out problem)
            || !TryReadText(root, "suffix", out _, out problem))
        {
            return problem;
        }

        // A blank format means no formatting; any other value must match exactly.
        return string.IsNullOrWhiteSpace(format) || OutputFormats.Contains(format, StringComparer.Ordinal)
            ? null
            : UnknownChoice("format", format, OutputFormats);
    }

    /// <summary>
    /// Reads an optional text setting the way the engine does (JsonElement.GetString): absent or
    /// null gives null, a string gives its text, and anything else is a problem because
    /// GetString throws on it and the step fails.
    /// </summary>
    private static bool TryReadText(
        JsonElement root, string name, out string? text, out WorkflowStepSettingsProblem? problem)
    {
        text = null;
        problem = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            problem = new WorkflowStepSettingsProblem(WorkflowStepSettingsProblemKind.NotText) { Setting = name };
            return false;
        }

        text = element.GetString();
        return true;
    }

    /// <summary>
    /// A setting the engine does not read is most often a misspelled or wrongly capitalized
    /// name, which the engine ignores without a word.
    /// </summary>
    private static WorkflowStepSettingsProblem? CheckSettingNames(JsonElement root, IReadOnlyList<string> settingNames)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!settingNames.Contains(property.Name, StringComparer.Ordinal))
            {
                return new WorkflowStepSettingsProblem(WorkflowStepSettingsProblemKind.UnknownSetting)
                {
                    Setting = property.Name,
                    Choices = settingNames,
                };
            }
        }

        return null;
    }

    private static WorkflowStepSettingsProblem UnknownChoice(string setting, string value, IReadOnlyList<string> choices) =>
        new(WorkflowStepSettingsProblemKind.UnknownChoice)
        {
            Setting = setting,
            Value = value,
            Choices = choices,
        };

    private static bool IsValidPattern(string pattern)
    {
        try
        {
            // Only parsed here, never matched.
            _ = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>What is wrong with a workflow step's settings.</summary>
public enum WorkflowStepSettingsProblemKind
{
    /// <summary>The step type needs settings and has none.</summary>
    Required,

    /// <summary>The settings are not valid JSON (<see cref="WorkflowStepSettingsProblem.Line"/>, <see cref="WorkflowStepSettingsProblem.Position"/>).</summary>
    InvalidJson,

    /// <summary>The settings are JSON but not an object (<see cref="WorkflowStepSettingsProblem.Example"/> shows one).</summary>
    NotAnObject,

    /// <summary>The step type has no setting of this name (<see cref="WorkflowStepSettingsProblem.Choices"/> lists its settings).</summary>
    UnknownSetting,

    /// <summary>The setting must be text in quotes.</summary>
    NotText,

    /// <summary>The setting must be a whole number (<see cref="WorkflowStepSettingsProblem.Example"/> shows one).</summary>
    NotAWholeNumber,

    /// <summary>The setting's value is not one the engine knows (<see cref="WorkflowStepSettingsProblem.Choices"/> lists them).</summary>
    UnknownChoice,

    /// <summary>The setting is not a valid regular expression.</summary>
    InvalidPattern,
}

/// <summary>
/// A problem found in a workflow step's settings. <see cref="Setting"/> names the setting it
/// concerns; the other details are set for the kinds that use them.
/// </summary>
public sealed record WorkflowStepSettingsProblem(WorkflowStepSettingsProblemKind Kind)
{
    public string? Setting { get; init; }

    public string? Value { get; init; }

    public IReadOnlyList<string> Choices { get; init; } = [];

    public string? Example { get; init; }

    /// <summary>The 1-based line of a JSON syntax error.</summary>
    public long Line { get; init; }

    /// <summary>The 1-based position in <see cref="Line"/> of a JSON syntax error, counted in bytes of UTF-8.</summary>
    public long Position { get; init; }
}
