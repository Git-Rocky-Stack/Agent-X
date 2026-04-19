using LocaleAudit;

if (args.Length >= 1 && (args[^1] == "--output" || args[^1] == "--fail-below"))
{
    Console.Error.WriteLine($"Error: flag '{args[^1]}' requires a value.");
    return 2;
}

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: LocaleAudit.Tool <app-xaml-root> <app-csharp-root> <strings-root> [--output report.json] [--fail-below 98]");
    return 2;
}

var xamlRoot = args[0];
var csharpRoot = args[1];
var stringsRoot = args[2];
var outputPath = ParseArg(args, "--output") ?? "audit-report.json";
var failBelow = double.TryParse(ParseArg(args, "--fail-below"), out var t) ? t : 98.0;

try
{
    var xamlUids = XamlUidExtractor.ExtractAll(xamlRoot);
    var codeKeys = CSharpGetStringExtractor.ExtractAll(csharpRoot);
    var locales = ReswReader.ReadAllLocales(stringsRoot);
    var report = CoverageReport.Build(xamlUids, codeKeys, locales);
    CoverageReport.WriteJson(report, outputPath);
    CoverageReport.PrintSummary(report, Console.Out, failBelow);

    return report.ShouldFail(failBelow) ? 1 : 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"LocaleAudit failed: {ex}");
    return 3;
}

static string? ParseArg(string[] args, string name)
{
    // First occurrence wins; duplicate flags are silently ignored (POSIX-style).
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i] == name) return args[i + 1];
    return null;
}
