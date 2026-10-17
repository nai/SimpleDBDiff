using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using SimpleDBDiff.Core;
using SimpleDBDiff.Postgres;
using SimpleDBDiff.Shared.Services;

namespace SimpleDBDiff.Desktop;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddHttpClient();
        builder.Services.AddFluentUIComponents();
        builder.Services.AddSingleton<IWorkspaceStorage>(new SqliteWorkspaceStorage(
            Path.Combine(FileSystem.AppDataDirectory, "workspace.db")));
        builder.Services.AddSingleton<PasswordVault>();
        builder.Services.AddSingleton<ConnectionProfileStore>();
        builder.Services.AddSingleton<ComparisonSessionStore>();
        builder.Services.AddSingleton<ISchemaReader, PostgresSchemaReader>();
        builder.Services.AddSingleton<IMigrationGenerator, PostgresMigrationGenerator>();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
