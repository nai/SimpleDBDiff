# Verification

The portable .NET 11 workspace builds successfully. Automated checks cover comparison and migration logic, shared-vault encryption and locking, SQLite session persistence, password exclusion from session DTOs, loaded-snapshot comparison, connection-edit invalidation, refresh reconciliation, and reporting successful save with failed connection.

Headless Chromium checked New Session autosave, vault open/close, Filters expansion, saved-session listing after reload without automatically loading it, manual password collection on session load, and desktop widths of 1440 and 900 pixels. No page JavaScript errors or horizontal overflow occurred in those checks. These checks used the web host, not a native MAUI WebView.

The seven PostgreSQL integration cases require `SIMPLEDBDIFF_TEST_LEFT` and `SIMPLEDBDIFF_TEST_RIGHT`. This environment has neither setting and no running/cached local PostgreSQL container, so the cases were skipped. The existing fixture SQL is under `tests/fixtures`. No real-database verification is claimed for this run.

The MAUI build was attempted and stopped with NETSDK1147 (missing workloads). The Linux host has no installed MAUI workload and cannot run Windows or Mac Catalyst. Native verification must be completed on a supported desktop before treating the desktop milestone as validated.

## Native acceptance steps

1. Restore/build the desktop host on Windows or macOS using the commands in README.md. Launch the native app and check the Fluent controls, table grid, sessions panel, and connection editor at the initial and minimum window sizes.
2. Use two disposable PostgreSQL databases seeded with the fixture SQL. Test Connection, Connect, Save and Connect, Refresh Schema, Compare, Swap sides, and Generate SQL. Verify generated SQL always changes the right target toward the left source.
3. Create/open the shared vault and save passwords for both profiles. Close it: loaded results must remain, passwords must clear, retrieval/saving must be blocked, and manual connection must remain possible. Restart and test wrong and correct passphrases. Inspect the app data database for the absence of plaintext database passwords.
4. Create, rename, and delete sessions. Change filters and migration choices, restart, verify no session is automatically loaded, then load a session using vault and manual passwords. Verify fresh metadata and preserved unchanged choices.
5. Change a database object externally, refresh its schema, and verify new/changed choices are cleared and flagged, unchanged choices persist, removed objects disappear, and previous SQL is cleared. Edit connection details and verify reconnect is required.
6. Simulate failed database connection after a successful profile save and a local-storage write failure. Confirm both save/connection outcomes and session save errors/retry remain readable.
7. Run `dotnet test SimpleDBDiff.sln` with both integration connection variables set against the disposable databases.
