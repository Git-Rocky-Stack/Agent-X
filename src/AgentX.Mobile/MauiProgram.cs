using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using AgentX.Mobile.Services;
using AgentX.Mobile.ViewModels;
using AgentX.Mobile.Views;

namespace AgentX.Mobile;

/// <summary>
/// MAUI application bootstrap. Registers all services, view-models, and pages
/// with the DI container before the application starts.
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        // No custom fonts are registered: the app ships none (the OpenSans registrations pointed at
        // files that never existed), and the pages use the platform default face.
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // ── Services ──────────────────────────────────────────────────────────

        // SettingsService reads/writes Preferences; construct before ApiClient
        // so the persisted URL can be passed in.
        builder.Services.AddSingleton<SettingsService>();

        builder.Services.AddSingleton<AgentXApiClient>(sp =>
        {
            var settings = sp.GetRequiredService<SettingsService>();

            // The client awaits the persisted pairing token (secure storage) before its first
            // request, so a page that loads during startup is never sent unauthenticated.
            return new AgentXApiClient(settings.ApiUrl, persistedTokenLoader: settings.GetApiTokenAsync);
        });

        // ── View-Models ───────────────────────────────────────────────────────

        builder.Services.AddTransient<DocumentsViewModel>();
        builder.Services.AddTransient<SearchViewModel>();
        builder.Services.AddTransient<ConversationsViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

        // ── Pages ─────────────────────────────────────────────────────────────

        builder.Services.AddTransient<DocumentsPage>();
        builder.Services.AddTransient<SearchPage>();
        builder.Services.AddTransient<ConversationsPage>();
        builder.Services.AddTransient<SettingsPage>();

        // ── Shell ─────────────────────────────────────────────────────────────

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddSingleton<App>();

        return builder.Build();
    }
}
