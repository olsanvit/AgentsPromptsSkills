using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Blazored.LocalStorage;
using Blazored.Modal;
using Blazored.SessionStorage;
using AgentsPromptsSkills.Mobile.Services;
using SharedServices;
using SharedServices.Services;
using AgentsPromptsSkills.Infrastructure;

namespace AgentsPromptsSkills.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // Configuration from embedded appsettings.json
        var assembly = typeof(MauiProgram).Assembly;
        using var stream = assembly.GetManifestResourceStream("AgentsPromptsSkills.Mobile.appsettings.json");
        if (stream is not null)
        {
            var config = new ConfigurationBuilder().AddJsonStream(stream).Build();
            builder.Configuration.AddConfiguration(config);
        }

        // EF Core — direct PostgreSQL connection
        var connStr = builder.Configuration.GetConnectionString("ApsDatabase")
            ?? throw new InvalidOperationException("Missing ApsDatabase in appsettings.json");

        builder.Services.AddDbContextFactory<AppDbContextAps>(opt =>
        {
            opt.UseNpgsql(connStr);
#if DEBUG
            opt.EnableDetailedErrors();
#endif
        });

        // Shared services
        builder.Services.AddScoped<ToastService>();
        builder.Services.AddScoped<AlertService>();
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddBlazoredModal();
        builder.Services.AddBlazoredLocalStorage();
        builder.Services.AddBlazoredSessionStorage();
        builder.Services.AddScoped<EfCoreService<AppDbContextAps>>();

        builder.Services.AddScoped<LoadingService>();
        builder.Services.AddScoped<ConfirmService>();
        builder.Services.AddScoped<UserPreferencesService>();
        builder.Services.AddScoped<ClipboardService>();
        builder.Services.AddTransient<Debouncer>();
        builder.Services.AddSingleton<ConnectionStateService>();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<ConnectivityService>();
        builder.Services.AddSingleton<SecureStorageService>();

        return builder.Build();
    }
}
