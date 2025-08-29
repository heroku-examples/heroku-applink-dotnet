# Repository Guidelines

## Project Structure & Module Organization

- `Heroku.Applink.sln`: Solution root.
- `Heroku.Applink/`: .NET 8 class library.
  - `ApplinkAuth.cs`: Resolves AppLink auth using env vars.
  - `Models/`: `Org`, `User` domain types.
  - `Data/`: `DataApi` and DTOs for REST (CRUD, SOQL, composite graph).
  - `Bulk/`: `BulkApi` for Bulk v2 ingest/query and CSV helpers.
  - `DataCloud/`: `DataCloudApi` for Data Cloud queries/ingest.
- `README.md`: Usage and required environment variables.

## Build, Test, and Development Commands

- `dotnet restore`: Restore NuGet packages.
- `dotnet build -c Debug`: Compile the library.
- `dotnet pack -c Release -o ./nupkg`: Create a NuGet package.
- `dotnet test`: Run tests (add a test project first).
- `dotnet format`: Apply .NET code style/formatting.

## Coding Style & Naming Conventions

- C#: nullable reference types and implicit usings enabled.
- Indentation: 4 spaces; braces on new lines (match existing files).
- Names: PascalCase for public types/members; camelCase for locals; `_camelCase` for private fields.
- Namespaces: `Heroku.Applink.*`. Prefer one public type per file.
- JSON: use `JsonSerializer` with `JsonSerializerDefaults.Web`; avoid dynamic runtime types.

## Testing Guidelines

- Framework: xUnit recommended. Create `Heroku.Applink.Tests` beside the library.
- Naming: files end with `*Tests.cs`; methods like `Method_Condition_Result`.
- Running: `dotnet test`. Add focused unit tests around `ApplinkAuth`, `DataApi`, and `BulkApi` helpers.

## Commit & Pull Request Guidelines

- Commits: concise, imperative subject (≤72 chars) with rationale in body. Conventional Commits (`feat:`, `fix:`, `chore:`) encouraged.
- Branches: `feature/<short-name>` or `fix/<short-name>`.
- PRs: include summary, linked issues, validation steps, and any required env vars (`HEROKU_APP_ID`, `HEROKU_APPLINK_*`). Add code snippets or logs where useful.

## Security & Configuration Tips

- Never commit tokens or org URLs. Configure via environment variables:
  - `HEROKU_APP_ID`
  - `<ATTACHMENT>_API_URL` and `<ATTACHMENT>_TOKEN`, or `HEROKU_APPLINK_<COLOR>_API_URL` and `HEROKU_APPLINK_<COLOR>_TOKEN`.
- Local example:
  - `export HEROKU_APP_ID=...`
  - `export HEROKU_APPLINK_PURPLE_API_URL=https://...`
  - `export HEROKU_APPLINK_PURPLE_TOKEN=...`
- Quick check: `await ApplinkAuth.GetAuthorizationAsync("<devname>")` then use `org.DataApi`/`org.BulkApi`.

