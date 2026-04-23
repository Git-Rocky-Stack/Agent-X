namespace AgentX.Core.Constants;

/// <summary>
/// Centralized application constants. Replaces scattered magic numbers
/// throughout services and ViewModels with named, documented values.
/// </summary>
public static class AppConstants
{
    // ── Dashboard & UI ──────────────────────────────────────────────
    public const int RecentItemsLimit = 5;
    public const int MaxSearchHistoryItems = 20;
    public const int MaxSyncHistoryItems = 50;

    // ── Indexing ────────────────────────────────────────────────────
    public const int EmbeddingBatchSize = 16;

    // ── Document Processing ─────────────────────────────────────────
    public const int MaxDocumentCharsForSummary = 8000;
    public const int MaxTranslationChars = 4000;
    public const int MaxContentPreviewChars = 100;
    public const int MaxAutoTagContentChars = 2000;

    // ── Security ────────────────────────────────────────────────────
    public const int Pbkdf2Iterations = 100_000;

    // ── Search ──────────────────────────────────────────────────────
    public const int DefaultSearchTopK = 10;
    public const float DefaultSearchMinScore = 0.3f;
    public const int DefaultSearchCacheMaxEntries = 100;
    public static readonly TimeSpan DefaultSearchCacheTtl = TimeSpan.FromMinutes(5);

    // ── Knowledge Graph ─────────────────────────────────────────────
    public const double GraphRepulsionStrength = 5000.0;
    public const double GraphAttractionStrength = 0.01;
    public const double GraphIdealEdgeLength = 100.0;
    public const double GraphCenterGravity = 0.01;
    public const double GraphDamping = 0.85;
    public const int GraphLayoutIterations = 100;
    public const double GraphCanvasExtent = 1000.0;

    // ── Network ─────────────────────────────────────────────────────
    public static readonly TimeSpan DefaultHttpTimeout = TimeSpan.FromSeconds(15);

    // ── Plugin ──────────────────────────────────────────────────────
    public const int MaxPluginReadmeBytes = 10_240; // 10 KB
    public const int MaxPluginNameLength = 100;

    // ── Duplicate Detection ─────────────────────────────────────────
    public const int MaxNearDuplicateScanDocuments = 500;

    // ── Conversation ────────────────────────────────────────────────
    public const int MaxBranchDepth = 100;
    public const int MaxBranchIterations = 100;
    public const int ContextWindowTokenReserve = 1024;
    public const int MaxConversationSummarySourceChars = 12_000;
    public const int MaxConversationSummaryTailChars = 6_000;
    public const int MaxConversationSummaryPreviewChars = 220;
    public const int MaxConversationSummaryKeyPoints = 5;
    public const int MaxConversationSummaryRecentItems = 6;

    // ── Timeouts ──────────────────────────────────────────────────
    public static readonly TimeSpan StatusBarPollInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan InitialStatusCheckDelay = TimeSpan.FromMilliseconds(5000);
    public static readonly TimeSpan WebScraperTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan OllamaCheckTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan OpenAiCheckTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan AnthropicCheckTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan StreamingResponseTimeout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ModelDownloadTimeout = TimeSpan.FromHours(2);
    public const int ModelCacheDurationSeconds = 30;

    // ── Retry ─────────────────────────────────────────────────────
    public const int RetryBaseDelayMs = 500;
    public const double RetryJitterFactor = 0.25;

    // ── Batch & Buffer ────────────────────────────────────────────
    public const int WebScraperBatchDelayMs = 500;
    public const int FileStreamBufferSize = 81920;
    public const int ResponseBuilderCapacity = 1024;
    public const int TextInputDebounceMs = 530;

    // ── AI & Inference ────────────────────────────────────────────
    public const int DefaultLocalContextSize = 8192;
    public const int DefaultMaxTokens = 4096;
    public const int DefaultContextWindow = 8192;
    public const int ChatDefaultMaxTokens = 2048;
    public const int ChatDefaultContextWindow = 4096;
    public const int CharsPerToken = 4;
    public const int MessageOverheadTokens = 4;
    public const int MaxContextWindowCap = 131072;
    public const int MinPreservedMessages = 4;
    public const int HydeMaxTokens = 512;
    public const int CompressionMaxTokens = 512;
    public const int RerankerMaxTokens = 256;
    public const int MultiQueryMaxTokens = 256;
    public const int DefaultModelParamCountMillions = 3000;
    public const int EmbeddingContextSize = 512;

    // ── Cryptography ──────────────────────────────────────────────
    public const int AesKeyBytes = 32;
    public const int GcmNonceBytes = 12;
    public const int GcmTagBytes = 16;
    public const int PbkdfSaltBytes = 16;
    public const int AesKeySizeBits = 256;
    public const int AesBlockSizeBits = 128;
    public const int IvSizeBytes = 16;

    // ── Validation ────────────────────────────────────────────────
    public const int MinEncryptionKeyLength = 8;
    public const int MinSyncIntervalMinutes = 1;
    public const int MaxSyncIntervalMinutes = 1440;
    public const int MaxTokensLimit = 128_000;
    public const int MaxContextWindowLimit = 1_048_576;
    public const int MaxChunkSize = 8192;

    // ── Backup ────────────────────────────────────────────────────
    public const int DefaultBackupIntervalHours = 168;
    public const int DefaultMaxBackupsToKeep = 5;
    public const int DefaultSyncIntervalMinutes = 30;

    // ── Search Retrieval ──────────────────────────────────────────
    public const int SearchTopKMultiplier = 3;
    public const int SearchTopKCap = 500;
    public const int RelevanceScoreBarMaxWidth = 150;

    // ── Visualization ─────────────────────────────────────────────
    public const int MinCanvasDimension = 50;
    public const int CanvasPadding = 40;
    public const int MaxLabelLength = 15;
    public const int NodeStrokeThicknessSelected = 2;
    public const int LabelBaseFontSize = 10;
    public const double MaxZoomScaleForFont = 1.5;
    public const int LabelMaxWidthBase = 80;
}
