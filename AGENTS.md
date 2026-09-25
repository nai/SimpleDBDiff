# Workspace instructions

## Workspace boundary

Only access files and directories within this repository. Do not follow symlinks outside it. Ask the user before accessing any external path.

## Project conventions

- Build with .NET 11 Blazor and global interactive server rendering.
- Use MudBlazor as the primary UI component library.
- Keep database-provider code separate from normalized schema models and comparison logic.
- Treat the left connection as the source and the right connection as the target. Recheck direction when generating migration SQL, including after the user swaps sides.
- Do not commit database passwords or other secrets.
- Add useful logging and handle errors clearly.
- Add focused automated tests for comparison behavior, PostgreSQL access, and migration script generation. Use integration tests where real database behavior matters.
- Verify relevant changes against the local PostgreSQL test databases.
- Make a focused Git commit for each completed implementation milestone.
