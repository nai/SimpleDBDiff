using SimpleDBDiff.Web.Components;
using SimpleDBDiff.Web.Services;
using SimpleDBDiff.Core;
using SimpleDBDiff.Postgres;
using MudBlazor.Services;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".data", "blazor-keys")));
builder.Services.AddMudServices();
builder.Services.AddSingleton<ConnectionProfileStore>();
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
    .AddInteractiveServerRenderMode();

app.Run();
