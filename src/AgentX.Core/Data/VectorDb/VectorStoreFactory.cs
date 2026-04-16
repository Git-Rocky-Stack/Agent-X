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
    /// <param name="logger">Serilog logger instance.</param>
    /// <returns>A fully constructed (but not yet initialized) <see cref="IVectorStore"/>.</returns>
    public static IVectorStore Create(ISettingsService settingsService, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(logger);

        var settings = settingsService.GetSettingsAsync().GetAwaiter().GetResult();

        if (!settings.EnableHnswIndex)
        {
            logger.Information("HNSW index disabled in settings; using SqliteVecStore (linear scan)");
            return new SqliteVecStore(settingsService, logger);
        }

        logger.Information(
            "HNSW index enabled; creating HnswVectorStore (M={M}, EfConstruction={EfConstruction}, FallbackThreshold={Threshold})",
            settings.HnswM, settings.HnswEfConstruction, settings.HnswFallbackThreshold);

        return new HnswVectorStore(
            settingsService,
            logger,
            m: settings.HnswM,
            efConstruction: settings.HnswEfConstruction,
            dimensions: 384, // all-MiniLM-L6-v2 default; matches EmbeddingService.Dimensions
            fallbackThreshold: settings.HnswFallbackThreshold);
    }
}