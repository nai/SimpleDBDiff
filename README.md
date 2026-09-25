# SimpleDBDiff

A .NET 11 Blazor Web App for comparing PostgreSQL table schemas. The entire UI uses interactive server rendering and MudBlazor. The left connection is the source; generated SQL changes the right connection to match it. SQL is displayed for review and is never executed by the app.

## Run

Install a .NET 11 SDK, then run:

```sh
dotnet restore SimpleDBDiff.sln
dotnet run --project src/SimpleDBDiff.Web
```

The local launch profile uses HTTP and the app has no authentication, as requested for Stage 1. Bind it to localhost and avoid exposing it to other machines. Enter both database connections in the UI. Profiles are saved in `src/SimpleDBDiff.Web/.data/profiles.db`, which Git ignores. Saving a password is optional; when enabled, it is encrypted with AES-GCM using a key derived from the vault passphrase. The passphrase is never persisted and is needed to unlock a saved password. Blazor's own data-protection keys also stay in this ignored `.data` directory.

## Stage 1 behavior

The PostgreSQL provider reads ordinary tables across non-system schemas, including columns and types, nullability, defaults, identity and generated expressions, primary/unique/foreign/check constraints, and indexes not owned by constraints. Provider-neutral models and comparison logic are in `SimpleDBDiff.Core`; PostgreSQL metadata access and SQL generation are in `SimpleDBDiff.Postgres`.

The script creates or drops tables, adds or removes columns, alters supported column properties, updates constraints and indexes, and uses a transaction. Dropping objects or changing a generated expression can remove data. Review the script, dependencies, target data, and backups before running it manually. Stage 1 does not compare sequences, views, triggers, stored procedures, extensions, or data. Partitioned and foreign tables cause a clear load error rather than an incomplete comparison. Defaults that depend on external objects, such as serial sequences, require those objects to exist on the target before running the script.

## Tests and development databases

Unit tests cover normalization, comparison status, one-sided objects, and source/target direction. Integration tests exercise real PostgreSQL metadata loading, generated SQL, identity/constraints/indexes, and connection/unsupported-metadata errors. Set `SIMPLEDBDIFF_TEST_LEFT` and `SIMPLEDBDIFF_TEST_RIGHT` to connection strings for two disposable PostgreSQL databases, then run `dotnet test SimpleDBDiff.sln`. No credentials are stored in the repository.

The dedicated local databases `simpledbdiff_source_test` and `simpledbdiff_target_test` contain intentionally different `demo` schemas seeded from `tests/fixtures/demo-source.sql` and `tests/fixtures/demo-target.sql`. Integration tests use a separate `sdd_it` schema and do not alter the demo pair.
