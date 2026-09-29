namespace AgentX.Core.AI;

/// <summary>
/// Download source and integrity expectations for a GGUF model the built-in provider can fetch.
/// </summary>
/// <param name="FileName">GGUF file name inside the models directory.</param>
/// <param name="DisplayName">Human-friendly model name for UI surfaces.</param>
/// <param name="DownloadUrl">Public download URL.</param>
/// <param name="ExpectedSizeBytes">Approximate size, for display and as a progress fallback.</param>
/// <param name="MinimumValidBytes">Size floor below which a file is treated as truncated.</param>
/// <param name="Sha256">
/// Expected SHA-256 (hex) of the file. When set, every download of this model is verified
/// against it before the file is published; null means only the size checks apply.
/// </param>
public sealed record BuiltInModelSource(
    string FileName,
    string DisplayName,
    string DownloadUrl,
    long ExpectedSizeBytes,
    long MinimumValidBytes,
    string? Sha256);

/// <summary>
/// The GGUF models the built-in provider knows how to download. Both the first-run bootstrap
/// (<see cref="BuiltInModelBootstrap"/>) and the Model Manager pull path use this single table,
/// so a download of the same file always goes through the same integrity checks.
/// <para>
/// The URLs still track the publisher's <c>main</c> branch and no SHA-256 is pinned yet: the
/// hashes must come from a verified download of the exact revision, not be written from memory.
/// Pinning <c>resolve/&lt;commit&gt;/</c> URLs together with their SHA-256 values is a tracked
/// follow-up; once filled in here, verification applies to every download automatically.
/// </para>
/// </summary>
public static class BuiltInModelCatalog
{
    /// <summary>Llama 3.2 3B Instruct, the default built-in model.</summary>
    public static BuiltInModelSource Llama32Instruct3B { get; } = new(
        FileName: "llama-3.2-3b-instruct-q4_k_m.gguf",
        DisplayName: "Llama 3.2 3B Instruct (Q4_K_M)",
        DownloadUrl: "https://huggingface.co/hugging-quants/Llama-3.2-3B-Instruct-Q4_K_M-GGUF/resolve/main/llama-3.2-3b-instruct-q4_k_m.gguf",
        ExpectedSizeBytes: 2_019_000_000L,
        MinimumValidBytes: 1_700_000_000L,
        Sha256: null);

    /// <summary>Llama 3.2 1B Instruct, the small alternative for low-memory machines.</summary>
    public static BuiltInModelSource Llama32Instruct1B { get; } = new(
        FileName: "llama-3.2-1b-instruct-q4_k_m.gguf",
        DisplayName: "Llama 3.2 1B Instruct (Q4_K_M)",
        DownloadUrl: "https://huggingface.co/hugging-quants/Llama-3.2-1B-Instruct-Q4_K_M-GGUF/resolve/main/llama-3.2-1b-instruct-q4_k_m.gguf",
        ExpectedSizeBytes: 808_000_000L,
        MinimumValidBytes: 700_000_000L,
        Sha256: null);

    /// <summary>All downloadable models.</summary>
    public static IReadOnlyList<BuiltInModelSource> All { get; } = [Llama32Instruct3B, Llama32Instruct1B];

    /// <summary>Finds the download source for a GGUF file name (case-insensitive), or null.</summary>
    public static BuiltInModelSource? Find(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var trimmed = fileName.Trim();
        return All.FirstOrDefault(m => string.Equals(m.FileName, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
