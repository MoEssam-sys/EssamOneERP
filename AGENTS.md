# Repository Guidelines

## Project Structure & Module Organization

`One.sln` targets .NET 10. `One.Domain/` contains entities, DTOs, attributes, and contracts. `One.Application/` owns repositories and services; `One.Infrastructure/` contains Dapper, EF Core, and integrations; `One.API/` hosts HTTP endpoints. Routable Razor pages, layouts, client services, and translations belong in `One.Shared/`. `One.MAUI/` and `One.Web/` are thin platform hosts. `OneSourceGen/` contains the Roslyn generator.

## Build, Test, and Development Commands

Run from the repository root:

```powershell
dotnet restore One.sln
dotnet build One.sln
dotnet test One.sln
dotnet run --project One.API/One.API.csproj --launch-profile https
dotnet run --project One.Web/One.Web.csproj --launch-profile https
dotnet build One.MAUI/One.MAUI.ERP.csproj -f net10.0-windows10.0.19041.0
dotnet format One.sln --verify-no-changes
```

In Visual Studio, select the `Web.API` solution launch profile to start the API and WebAssembly host together.

## New Master Screen Workflow

Repository code is the source of truth. Inspect the current Branches backend and Brands frontend first; if their pattern changes, follow it and update this guide.

1. Confirm fields, relationships, uniqueness, authorization, and delete/deactivate behavior.
2. Add a versioned table script under `Database/Scripts/`. Business tables use Dapper through `MainConnection`; use EF migrations only for Identity/company schema changes.
3. Add `One.Domain/Entities/<Area>/<Entity>.cs`, inheriting `BaseEntity<Guid>`. Apply `[DbTable]`, `[InsertField]`, and `[UpdateField]` deliberately.
4. Add separate create, update, active, and filter DTOs under `One.Domain/DTOs/<Area>/`. Filters inherit `BaseFilter` and define `[MatchOn]`/`[MatchDictionary]`.
5. Add `One.Application/Repos/<Area>/<Entity>Repo.cs`, inheriting `BaseRepository<TEntity, Guid>`. Generic repositories are discovered automatically; do not register them manually.
6. Add an authorized controller under `One.API/Controllers/<Area>/`. Inherit `BaseApiController`, use `IDynamicMappingService`, and return results with `ToActionResult`/`ToPagedActionResult`.
7. Add matching keys to both translation files, then build `One.Shared/Components/<Area>/<Entity>Master.razor`. Follow Brands for localized CRUD, cancellation, debounced search, paging, confirmation, and responsive RTL layout. Use the shared HTTP result extensions.
8. Add the localized navigation route and verify SQL, API status codes, validation, CRUD, search, translations, desktop, Android, and WebAssembly behavior.

## Localization

Translations live in `One.Shared/Resources/ar.yml` and `en-US.yml`. Keep identical `PascalCase` keys and placeholders. Razor uses generated `IKeysAccessor` members or `ITranslator`; never hard-code user-facing text. `LocalizeScope.razor` refreshes language changes, and `MainLayout.razor` controls culture and RTL state.

## Local Secrets & Configuration

Keep API development secrets outside the repository with .NET User Secrets owned by `One.API`; the Windows store is `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`. Tracked appsettings files contain only safe defaults or placeholders. `One.Web`, `One.MAUI`, and `One.Shared` must never contain database, SMTP, or signing secrets. Deployment uses environment variables such as `ConnectionStrings__MainConnection` and `Jwt__Secret`. Never print secret values in logs or examples.

## Improvement Plan

`SYSTEM_IMPROVEMENT_PLAN.md` is the source of truth for the current remediation sequence. Complete one numbered section at a time, update its status and verification evidence, and revise this guide whenever repository, service, localization, authorization, or page-creation conventions change.

## Style, Tests, and Contributions

Follow `.editorconfig`: four spaces, CRLF, nullable references, braces, and `using` directives outside namespaces. Use `PascalCase` for public symbols, `I` prefixes for interfaces, and `camelCase` for locals. Add xUnit projects as `One.<Layer>.Tests` and name tests `Method_Scenario_ExpectedResult`. Use focused imperative commits such as `feat: add cities master`. Pull requests need an impact summary, linked issue, verification notes, UI screenshots, and migration warnings. Never commit credentials or tokens.
