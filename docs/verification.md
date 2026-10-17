# Verification

The portable .NET 11 workspace builds successfully. Automated checks cover comparison and migration logic, shared-vault encryption and locking, SQLite session persistence, password exclusion from session DTOs, loaded-snapshot comparison, connection-edit invalidation, refresh reconciliation, and reporting successful save with failed connection.

The layout revision passed all 29 automated tests, including the seven real PostgreSQL integration cases using the prepared local source and target test databases. The portable web build succeeded with zero warnings and zero errors.

Headless Chromium checked the anchored Sessions popup with 30 saved sessions: closed on startup without loading a session, keyboard opening, initial focus, Escape dismissal, focus return, rename focus, a scrollable list, and session-name deduplication. It also loaded a saved session against real PostgreSQL, compared fresh schemas, generated SQL on demand, and confirmed that status filtering and searching changed neither migration choices nor generated SQL. The single vault button changed between Open Vault and Close Vault, and closing kept loaded results visible.

Rendered layout checks passed at widths of 1440, 1100, 900, 700, 640, 600, and 375 pixels: app-bar controls remained visible without horizontal overflow, search and Filters shared one row, the popup fit the viewport, and page scrolling worked. These checks used the shared UI in the web host, not a native MAUI WebView.

The unchanged connection editor's Fluent number input attempts to load IMask from an external CDN that was unavailable in this environment; opening that editor could therefore interrupt the Blazor circuit. The final layout checks left connection editors closed and exercised database loading through the session flow. This existing dependency remains outside the layout revision.

The MAUI build was attempted and stopped with NETSDK1147 (missing workloads). The Linux host has no installed MAUI workload and cannot run Windows or Mac Catalyst. Native verification must be completed on a supported desktop before treating the desktop milestone as validated.

## Native acceptance steps

1. Restore/build the desktop host on Windows or macOS using the commands in README.md. Launch the native app and check the Fluent controls, table grid, sessions panel, and connection editor at the initial and minimum window sizes.
2. Use two disposable PostgreSQL databases seeded with the fixture SQL. Test Connection, Connect, Save and Connect, Refresh Schema, Compare, Swap sides, and Generate SQL. Verify generated SQL always changes the right target toward the left source.
3. Create/open the shared vault and save passwords for both profiles. Close it: loaded results must remain, passwords must clear, retrieval/saving must be blocked, and manual connection must remain possible. Restart and test wrong and correct passphrases. Inspect the app data database for the absence of plaintext database passwords.
4. Create, rename, and delete sessions. Change filters and migration choices, restart, verify no session is automatically loaded, then load a session using vault and manual passwords. Verify fresh metadata and preserved unchanged choices.
5. Change a database object externally, refresh its schema, and verify new/changed choices are cleared and flagged, unchanged choices persist, removed objects disappear, and previous SQL is cleared. Edit connection details and verify reconnect is required.
6. Simulate failed database connection after a successful profile save and a local-storage write failure. Confirm both save/connection outcomes and session save errors/retry remain readable.
7. Run `dotnet test SimpleDBDiff.sln` with both integration connection variables set against the disposable databases.
