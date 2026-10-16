using SimpleDBDiff.Web.Components;
using SimpleDBDiff.Shared.Services;
using SimpleDBDiff.Core;
using SimpleDBDiff.Postgres;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".data", "blazor-keys")));
builder.Services.AddHttpClient();
builder.Services.AddFluentUIComponents();
builder.Services.AddSingleton<IWorkspaceStorage>(new SqliteWorkspaceStorage(Path.Combine(builder.Environment.ContentRootPath, ".data", "profiles.db")));
builder.Services.AddSingleton<ComparisonSessionStore>();
builder.Services.AddScoped<PasswordVault>();
builder.Services.AddScoped<ConnectionProfileStore>();
builder.Services.AddSingleton<ISchemaReader, PostgresSchemaReader>();
builder.Services.AddSingleton<IMigrationGenerator, PostgresMigrationGenerator>();
builder.Logging.AddJsonConsole();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddAdditionalAssemblies(typeof(SimpleDBDiff.Shared.Components.Pages.Home).Assembly)
    .AddInteractiveServerRenderMode();

app.Run();
