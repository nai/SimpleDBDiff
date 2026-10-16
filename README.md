# SimpleDBDiff

A .NET 11 Blazor web and MAUI Blazor Hybrid desktop app for comparing PostgreSQL table schemas. The entire UI uses interactive server rendering and Fluent UI Blazor. The left connection is the source; generated SQL changes the right connection to match it. SQL is displayed for review and is never executed by the app.

## Run

Install a .NET 11 SDK, then run:

```sh
dotnet restore SimpleDBDiff.sln
dotnet run --project src/SimpleDBDiff.Web
```

The local launch profile uses HTTP and the app has no authentication, as requested for Stage 1. Bind it to localhost and avoid exposing it to other machines. Enter both database connections in the UI. Profiles and sessions are saved in `src/SimpleDBDiff.Web/.data/profiles.db`, which Git ignores. **Open Vault** creates or opens one vault for all saved passwords. AES-GCM encryption uses a PBKDF2-SHA256 key (310,000 iterations) derived from the shared passphrase; neither the passphrase nor plaintext passwords are persisted. While locked, enter database passwords manually; saving or retrieving passwords requires opening the vault. **Close Vault** clears the key and connection passwords held in memory and keeps loaded comparison results visible. Existing profiles are retained. On first open, legacy saved passwords are imported if they all use the entered passphrase; the original rows remain intact. Blazor's own data-protection keys also stay in this ignored `.data` directory.

## Workspace

Configure the left source and right target using the compact connection cards. **Test Connection** checks credentials without changing loaded metadata. **Connect** and **Save and Connect** load the schema automatically; saving and connecting report separate outcomes. **Refresh Schema** fetches current metadata. The card shows connection status, table count, database, and refresh time.

**Compare schemas** compares the loaded snapshots without database access. Editing host, port, database, or username marks the existing snapshot out of date and requires reconnecting. A failed refresh keeps the previous snapshot available but requires a successful reconnect before comparison or SQL generation. Refresh preserves migration selections for unchanged objects. New or changed objects are marked for review and deselected. SQL is cleared when metadata, selections, or direction changes.

**Sessions** opens the saved-session panel; startup shows the list without loading a session. **New Session** enables automatic saving of connection choices, search/status filters, and migration choices, including skipped-table detail choices. Names follow the source and target databases until renamed, with a numbered fallback. Sessions can be renamed or deleted. The app bar shows save status and a retry action if saving fails. Loading a session restores settings, obtains passwords from the open vault or manual password fields, loads fresh metadata, and compares it with the saved selection baseline. Passwords remain exclusively in the vault. Use **Filters** above the table list to show or hide status checkboxes; visibility never changes migration selection.

Each table shows mixed object counts: **D×** different, **R×** right only, and **L×** left only. Counts exclude equal objects and the table summary, so a changed column is counted once. They show the full comparison independently of visibility filters and migration selection.

All tables and detail objects start checked. A table checkbox includes or skips the whole table without changing its saved object selections. Rechecking a table restores those choices. **Check all** and **Uncheck all** in the explorer apply to every table, including hidden results, and preserve object selections. The same buttons above the detail grid apply to every object in the current table. Filters change visibility only. Click **Generate SQL** to review and download the script for the current selection. Any selection change clears the previous script. Comparing the same snapshots preserves choices. Connection edits clear the comparison; swapping sides starts a comparison in the new direction with a new selection.

For one-sided tables, selecting the whole table creates or drops it. Partial selections create only the selected source columns/constraints/indexes, or remove only selected target objects while keeping the table. If a retained object needs an excluded or removed dependency, generation explains what else must be selected. Required rebuilds of unchanged foreign keys, constraints, or indexes also require their inclusion. The app never executes the script.

## Desktop app

`src/SimpleDBDiff.Shared` is a Razor class library containing the workspace, Fluent UI components, selection reconciliation, vault, and session services. Both hosts use the same PostgreSQL reader and migration generator. `IWorkspaceStorage` is replaceable; the SQLite implementation takes its path from the host.

`src/SimpleDBDiff.Desktop` is the final MAUI Blazor Hybrid host for Windows and Mac Catalyst. It uses an embedded `BlazorWebView`, makes database connections directly from .NET, and stores `workspace.db` in `FileSystem.AppDataDirectory`. The shared vault protects passwords at rest. No web server is required. The native window starts at 1440×900 and has a 900×650 minimum; the shared layout adapts to smaller widths. Mac Catalyst has the outbound network entitlement for PostgreSQL access.

Build on Windows with a compatible .NET 11 SDK, MAUI Windows workload, Windows SDK, and WebView2:

```sh
dotnet workload install maui-windows
dotnet build src/SimpleDBDiff.Desktop -f net11.0-windows10.0.19041.0
dotnet build src/SimpleDBDiff.Desktop -t:Run -f net11.0-windows10.0.19041.0
```

On macOS, install the compatible Xcode version and `maui-maccatalyst` workload, then build/run with `-f net11.0-maccatalyst`. Package versions for MAUI are supplied by the installed .NET 11 workload. `SimpleDBDiff.Desktop.slnx` is separate from the portable solution so normal web/test builds remain available on Linux.

Native build and runtime verification are **pending**: the current Linux environment has no MAUI workloads and cannot run Windows or Mac Catalyst apps. See [verification notes](docs/verification.md) for completed checks and the native acceptance steps.

## Schema behavior

The PostgreSQL provider reads ordinary tables across non-system schemas, including columns and types, nullability, defaults, identity and generated expressions, primary/unique/foreign/check constraints, and indexes not owned by constraints. Provider-neutral models and comparison logic are in `SimpleDBDiff.Core`; PostgreSQL metadata access and SQL generation are in `SimpleDBDiff.Postgres`.

The script creates or drops tables, adds or removes columns, alters supported column properties, updates constraints and indexes, and uses a transaction. Dropping objects or changing a generated expression can remove data. Review the script, dependencies, target data, and backups before running it manually. Stage 1 does not compare sequences, views, triggers, stored procedures, extensions, or data. Partitioned and foreign tables cause a clear load error rather than an incomplete comparison. Defaults that depend on external objects, such as serial sequences, require those objects to exist on the target before running the script.

## Tests and development databases

Unit tests cover normalization, comparison status, one-sided objects, and source/target direction. Integration tests exercise real PostgreSQL metadata loading, generated SQL, identity/constraints/indexes, and connection/unsupported-metadata errors. Set `SIMPLEDBDIFF_TEST_LEFT` and `SIMPLEDBDIFF_TEST_RIGHT` to connection strings for two disposable PostgreSQL databases, then run `dotnet test SimpleDBDiff.sln`. No credentials are stored in the repository. Without both connection settings, integration tests are explicitly skipped.

The dedicated local databases `simpledbdiff_source_test` and `simpledbdiff_target_test` contain intentionally different `demo` schemas seeded from `tests/fixtures/demo-source.sql` and `tests/fixtures/demo-target.sql`. Integration tests use a separate `sdd_it` schema and do not alter the demo pair.
