Project brief
Build a database comparison and migration application using .NET 11 Blazor with global interactive server rendering. Use MudBlazor as the primary UI library and use its components wherever suitable. The initial application will have no HTTPS configuration or authentication.
Purpose and scope
The application will compare two databases and help make the right database (target) match the left database (source). Stage 1 covers schema comparison and migration script generation. Data comparison and synchronization will come later.
PostgreSQL is the first supported database. Keep provider-specific database access separate from normalized schema models and comparison logic so SQL Server, MySQL, and SQLite providers can be added later.
Stage 1 features
1. Connections: Configure left and right PostgreSQL connections. Choose a simple, reliable embedded store for saved connection profiles. Saving passwords must be optional; protect any saved password with an appropriate encryption or data-protection library. Do not commit credentials.
2. Schema loading: Load table lists and table schema details from both databases. Stored procedures are for a later stage.
3. Comparison: Normalize and compare the schemas. Show results side by side using suitable MudBlazor components. Allow users to independently show or hide equal, different, left only, and right only results. Provide a way to swap source and target before comparing.
4. Migration script: Generate PostgreSQL SQL for schema changes needed to make the right database match the left. Show the script for review. Executing migrations and synchronizing data are outside Stage 1.
Engineering and testing
Add structured logging, clear error handling, and reasonable performance measures for loading and comparing schemas.
Create appropriate automated tests for the project. Cover normalization and comparison rules with unit tests, and verify PostgreSQL schema loading and migration script generation with integration tests. Include cases for equal schemas, changed objects, objects present on only one side, source/target swapping, and relevant connection or metadata errors. Keep tests focused on observable behavior rather than duplicating implementation logic.
Use the local PostgreSQL server at localhost:5432 (postgres / postgres) to create two test databases with representative schema differences. Keep these development credentials out of committed configuration.
Initialize Git if needed and make a separate commit for each meaningful implementation milestone.
Proposed AGENTS.md
# Workspace instructions

## Workspace boundary

Only access files and directories within this repository. Do not follow
symlinks outside it. Ask the user before accessing any external path.

## Project conventions

- Build with .NET 11 Blazor and global interactive server rendering.
- Use MudBlazor as the primary UI component library.
- Keep database-provider code separate from normalized schema models and
  comparison logic.
- Treat the left connection as the source and the right connection as the
  target. Recheck direction when generating migration SQL, including after
  the user swaps sides.
- Do not commit database passwords or other secrets.
- Add useful logging and handle errors clearly.
- Add focused automated tests for comparison behavior, PostgreSQL access,
  and migration script generation. Use integration tests where real database
  behavior matters.
- Verify relevant changes against the local PostgreSQL test databases.
- Make a focused Git commit for each completed implementation milestone.
Before implementation, the main scope choice is which schema details Stage 1 should compare and migrate: columns and types alone, or also defaults, keys, constraints, and indexes.

Begin implementation based on above instruction.