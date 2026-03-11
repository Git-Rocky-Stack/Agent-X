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

        builder
            .UseMaui()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-SemiBold.ttf", "OpenSansSemiBold");
            });

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
            return new AgentXApiClient(settings.ApiUrl);
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
