# SimpleDBDiff

A .NET 11 Blazor Web App for comparing PostgreSQL table schemas. The entire UI uses interactive server rendering and Fluent UI Blazor. The left connection is the source; generated SQL changes the right connection to match it. SQL is displayed for review and is never executed by the app.

## Run

Install a .NET 11 SDK, then run:

```sh
dotnet restore SimpleDBDiff.sln
dotnet run --project src/SimpleDBDiff.Web
```

The local launch profile uses HTTP and the app has no authentication, as requested for Stage 1. Bind it to localhost and avoid exposing it to other machines. Enter both database connections in the UI. Profiles are saved in `src/SimpleDBDiff.Web/.data/profiles.db`, which Git ignores. Saving a password is optional; when enabled, it is encrypted with AES-GCM using a key derived from the vault passphrase. The passphrase is never persisted and is needed to unlock a saved password. Blazor's own data-protection keys also stay in this ignored `.data` directory.

## Workspace

Configure the left source and right target using the expandable connection panels. Load a schema to browse its table list, then use **Compare schemas** to refresh both sides and compare them. SQL is generated only when you click **Generate SQL**.

Each table shows mixed object counts: **D×** different, **R×** right only, and **L×** left only. Counts exclude equal objects and the table summary, so a changed column is counted once. They show the full comparison independently of visibility filters and migration selection.

All tables and detail objects start checked. A table checkbox includes or skips the whole table without changing its saved object selections. Rechecking a table restores those choices. **Check all** and **Uncheck all** in the explorer apply to every table, including hidden results, and preserve object selections. The same buttons above the detail grid apply to every object in the current table. Filters change visibility only. Click **Generate SQL** to review and download the script for the current selection. Any selection change clears the previous script; comparison, connection edits, and swapping sides reset the selection and SQL.

For one-sided tables, selecting the whole table creates or drops it. Partial selections create only the selected source columns/constraints/indexes, or remove only selected target objects while keeping the table. If a retained object needs an excluded or removed dependency, generation explains what else must be selected. Required rebuilds of unchanged foreign keys, constraints, or indexes also require their inclusion. The app never executes the script.

## Stage 1 behavior

The PostgreSQL provider reads ordinary tables across non-system schemas, including columns and types, nullability, defaults, identity and generated expressions, primary/unique/foreign/check constraints, and indexes not owned by constraints. Provider-neutral models and comparison logic are in `SimpleDBDiff.Core`; PostgreSQL metadata access and SQL generation are in `SimpleDBDiff.Postgres`.

The script creates or drops tables, adds or removes columns, alters supported column properties, updates constraints and indexes, and uses a transaction. Dropping objects or changing a generated expression can remove data. Review the script, dependencies, target data, and backups before running it manually. Stage 1 does not compare sequences, views, triggers, stored procedures, extensions, or data. Partitioned and foreign tables cause a clear load error rather than an incomplete comparison. Defaults that depend on external objects, such as serial sequences, require those objects to exist on the target before running the script.

## Tests and development databases

Unit tests cover normalization, comparison status, one-sided objects, and source/target direction. Integration tests exercise real PostgreSQL metadata loading, generated SQL, identity/constraints/indexes, and connection/unsupported-metadata errors. Set `SIMPLEDBDIFF_TEST_LEFT` and `SIMPLEDBDIFF_TEST_RIGHT` to connection strings for two disposable PostgreSQL databases, then run `dotnet test SimpleDBDiff.sln`. No credentials are stored in the repository.

The dedicated local databases `simpledbdiff_source_test` and `simpledbdiff_target_test` contain intentionally different `demo` schemas seeded from `tests/fixtures/demo-source.sql` and `tests/fixtures/demo-target.sql`. Integration tests use a separate `sdd_it` schema and do not alter the demo pair.
