using AgentX.Core.AI.Models;

namespace AgentX.Core.AI;

/// <summary>
/// Ensures the bundled "built-in" local LLM (Llama 3.2 3B Instruct, Q4_K_M GGUF) is present on
/// disk. The SLIM installer ships without the ~1.9 GB model to stay under GitHub's per-asset
/// limit, so on first run the app fetches it once from a public source. The OFFLINE installer
/// pre-places the same file, in which case <see cref="IsInstalled"/> short-circuits the download.
/// <para>
/// Cloud providers (OpenAI/Anthropic/Ollama) never require this model — it powers only the
/// fully-offline built-in provider.
/// </para>
/// </summary>
public interface IBuiltInModelBootstrap
{
    /// <summary>GGUF file name of the built-in model (matches the local provider's model file).</summary>
    string ModelFileName { get; }

    /// <summary>Human-friendly model name for UI surfaces.</summary>
    string ModelDisplayName { get; }

    /// <summary>Approximate on-disk size of the model, for UI display and progress fallback.</summary>
    long ExpectedSizeBytes { get; }

    /// <summary>Full path where the model is expected on disk.</summary>
    string ModelPath { get; }

    /// <summary>
    /// True when a complete model file is already present (size at or above the validity floor),
    /// so a download can be skipped. A truncated leftover is deliberately treated as NOT installed.
    /// </summary>
    bool IsInstalled();

    /// <summary>
    /// Downloads the model only if it is not already installed. Idempotent and safe to call on
    /// every launch; a no-op (aside from an "already installed" progress report) when present.
    /// </summary>
    Task EnsureInstalledAsync(IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Downloads the model to a temporary <c>.part</c> file, verifies the transfer, then atomically
    /// publishes it to <see cref="ModelPath"/>. A cancelled or failed download leaves no partial
    /// file behind, so <see cref="IsInstalled"/> can never observe a half-written, corrupt model.
    /// </summary>
    Task DownloadAsync(IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default);
}
