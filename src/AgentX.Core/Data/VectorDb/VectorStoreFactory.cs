using AgentX.Core.AI;
using AgentX.Core.Services.Settings;
using Serilog;

namespace AgentX.Core.Data.VectorDb;

/// <summary>
/// Static factory that creates the appropriate <see cref="IVectorStore"/> implementation
/// based on the current <see cref="AppSettings"/> configuration.
///
/// Selection logic:
///   - If <see cref="AppSettings.EnableHnswIndex"/> is false, returns <see cref="SqliteVecStore"/>
///     (linear scan, no index overhead, optimal for small collections).
///   - If true, returns <see cref="HnswVectorStore"/> with the configured HNSW parameters.
///     HnswVectorStore has built-in fallback to linear scan when the embedding count is
///     below the configured threshold, so it works well for all collection sizes.
/// </summary>
public static class VectorStoreFactory
{
    /// <summary>
    /// Creates the appropriate <see cref="IVectorStore"/> implementation based on settings.
    /// </summary>
    /// <param name="settingsService">Settings service providing app configuration.</param>
    /// <param name="embeddingService">Embedding service providing dimension information.</param>
    /// <param name="logger">Serilog logger instance.</param>
    /// <param name="connectionFactory">Encrypted connection factory — required so PRAGMA key is applied when opening SQLite.</param>
    /// <returns>A fully constructed (but not yet initialized) <see cref="IVectorStore"/>.</returns>
    public static IVectorStore Create(
        ISettingsService settingsService,
        IEmbeddingService embeddingService,
        ILogger logger,
        IEncryptedConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(embeddingService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        // Wave 4b: VSTHRD002 is suppressed because this factory is invoked from a sync
        // DI factory lambda (Microsoft.Extensions.DependencyInjection does not support
        // async construction). SettingsService caches its result on first access and
        // performs no I/O after that, so the GetResult call is non-blocking in practice.
        // A proper fix would require pre-resolving settings before container build —
        // architectural change tracked separately.
#pragma warning disable VSTHRD002
        var settings = settingsService.GetSettingsAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002

        if (!settings.EnableHnswIndex)
        {
            logger.Information("HNSW index disabled in settings; using SqliteVecStore (linear scan)");
            return new SqliteVecStore(settingsService, logger, connectionFactory);
        }

        logger.Information(
            "HNSW index enabled; creating HnswVectorStore (M={M}, EfConstruction={EfConstruction}, FallbackThreshold={Threshold}, Dimensions={Dimensions})",
            settings.HnswM, settings.HnswEfConstruction, settings.HnswFallbackThreshold, embeddingService.Dimensions);

        return new HnswVectorStore(
            settingsService,
            logger,
            m: settings.HnswM,
            efConstruction: settings.HnswEfConstruction,
            dimensions: embeddingService.Dimensions,
            fallbackThreshold: settings.HnswFallbackThreshold,
            connectionFactory: connectionFactory);
    }
}
