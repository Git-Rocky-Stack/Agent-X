using AgentX.Core.Services.Search;

namespace AgentX.Core.Services.Settings;

public class AppSettings
{
    // Onboarding
    public bool OnboardingCompleted { get; set; } = false;

    // AI Provider — Active selection ("local", "ollama", "openai", "anthropic")
    public string ActiveProviderId { get; set; } = "local";

    // Built-in Local LLM (LLamaSharp)
    public string LocalModelFileName { get; set; } = "llama-3.2-3b-instruct-q4_k_m.gguf";
    public int LocalContextSize { get; set; } = 8192;
    public int LocalGpuLayers { get; set; } = 0; // 0 = CPU only; increase for GPU offloading

    // Ollama Provider
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string DefaultModel { get; set; } = "llama3.2";
    public string EmbeddingModel { get; set; } = "all-minilm";

    // OpenAI Provider
    public string? OpenAiApiKey { get; set; }
    public string OpenAiEndpoint { get; set; } = "https://api.openai.com/v1/";
    public string? OpenAiDefaultModel { get; set; } = "gpt-4o-mini";

    // Anthropic Provider
    public string? AnthropicApiKey { get; set; }
    public string AnthropicEndpoint { get; set; } = "https://api.anthropic.com/v1/";
    public string? AnthropicDefaultModel { get; set; } = "claude-sonnet-4-20250514";

    // Inference
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 4096;
    public int ContextWindow { get; set; } = 8192;

    // Knowledge Vault
    public int ChunkSize { get; set; } = 512;
    public int ChunkOverlap { get; set; } = 50;
    public int TopKResults { get; set; } = 5;
    public bool AutoIndexWatchFolders { get; set; } = true;

    // Multi-Model Routing
    public bool EnableModelRouting { get; set; } = false;
    public string ActiveRoutingProfileId { get; set; } = "balanced";

    // Deep Research Mode
    public bool EnableResearchMode { get; set; } = false;
    public WebSearchProvider WebSearchProvider { get; set; } = WebSearchProvider.Brave;
    public string? WebSearchApiKey { get; set; }
    public int MaxSearchResults { get; set; } = 10;
    public int SearchCacheTtlMinutes { get; set; } = 60;

    // Storage
    public string StoragePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgentX");
}
