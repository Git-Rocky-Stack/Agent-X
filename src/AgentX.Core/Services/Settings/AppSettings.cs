namespace AgentX.Core.Services.Settings;

public class AppSettings
{
    // Onboarding
    public bool OnboardingCompleted { get; set; } = false;

    // AI Provider
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string DefaultModel { get; set; } = "llama3.2";
    public string EmbeddingModel { get; set; } = "all-minilm";

    // Inference
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 4096;
    public int ContextWindow { get; set; } = 8192;

    // Knowledge Vault
    public int ChunkSize { get; set; } = 512;
    public int ChunkOverlap { get; set; } = 50;
    public int TopKResults { get; set; } = 5;
    public bool AutoIndexWatchFolders { get; set; } = true;

    // Storage
    public string StoragePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgentX");
}
