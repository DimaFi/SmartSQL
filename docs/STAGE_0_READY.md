# Stage 0 Readiness Report

> Historical snapshot of the initial UniData foundation. Subsequent decisions renamed the product and assemblies to SmartSQL, selected Avalonia 12, and added a minimal desktop shell. See `DECISIONS.md` and `README.md` for current status.

## Status

Stage 0 - Project Foundation is prepared.

The repository contains a small, clean solution foundation for the future UniData application. The current stage intentionally avoids implementing business entities, the Universal Data Model, database providers, UI, import/export, backup, and AI functionality.

## Created Structure

```text
UniData/
  src/
    UniData.Domain/
    UniData.Application/
    UniData.Infrastructure/
    UniData.Desktop/
  tests/
    UniData.Domain.Tests/
    UniData.Application.Tests/
    UniData.Infrastructure.Tests/
  docs/
    PROJECT_GOALS.md
    ARCHITECTURE.md
    ROADMAP.md
    DECISIONS.md
    STAGE_0_READY.md
  AGENTS.md
  README.md
  .gitignore
  UniData.sln
```

## Projects

### UniData.Domain

Minimal independent project targeting `net10.0`.

No references to infrastructure, UI, SQLite, Dapper, or AI packages.

### UniData.Application

Minimal application-layer project targeting `net10.0`.

References:

- `UniData.Domain`

### UniData.Infrastructure

Minimal infrastructure-layer project targeting `net10.0`.

References:

- `UniData.Application`
- `UniData.Domain`

No concrete database provider is implemented yet.

### UniData.Desktop

Minimal placeholder project targeting `net10.0`.

References:

- `UniData.Application`
- `UniData.Domain`

The desktop UI framework is intentionally not selected in Stage 0. This avoids prematurely committing to WPF, Avalonia, WinUI, MAUI, or another UI technology.

## Test Projects

Created minimal test project placeholders:

- `UniData.Domain.Tests`
- `UniData.Application.Tests`
- `UniData.Infrastructure.Tests`

They are included in the solution and reference the corresponding production projects. No test framework packages were added yet, because Stage 0 avoids unnecessary dependencies.

## Documentation

Created documentation:

- `PROJECT_GOALS.md` - project problem, goal, user scenarios, universal scope, local-first approach, natural-language direction, portability, and diploma validation scenario.
- `ARCHITECTURE.md` - high-level layered architecture and future AI pipeline.
- `ROADMAP.md` - preliminary staged roadmap without fixed deadlines.
- `DECISIONS.md` - initial architecture decision log, D001-D009.
- `AGENTS.md` - repository rules for future AI development agents.
- `README.md` - short project overview, status, structure, requirements, build, and test commands.

## Dependency Direction

Current dependency direction:

```text
UniData.Domain
  referenced by UniData.Application
  referenced by UniData.Desktop
  referenced by UniData.Infrastructure

UniData.Application
  referenced by UniData.Infrastructure
  referenced by UniData.Desktop

UniData.Infrastructure
  not referenced by Domain or Application

UniData.Desktop
  not referenced by Domain, Application, or Infrastructure
```

No cyclic references were introduced.

## Packages

No `PackageReference` entries are present.

Not added:

- SQLite
- Dapper
- Entity Framework
- AI/LLM SDKs
- import/export libraries
- UI frameworks
- backup libraries

## Verification Performed

Commands executed:

```powershell
dotnet --info
dotnet --list-sdks
dotnet sln UniData.sln list
rg "<TargetFramework>|<ProjectReference|<PackageReference" -n .
dotnet build UniData.sln
dotnet test UniData.sln
```

Installed SDKs:

```text
8.0.403
8.0.406
```

Build result:

```text
Failed with NETSDK1045 because the installed .NET SDK 8.0 does not support targeting net10.0.
```

Test result:

```text
Failed with NETSDK1045 for the same reason.
```

The target framework was not silently downgraded to .NET 8. The project remains aligned with the accepted .NET 10 LTS decision.

## Intentional Non-Goals for Stage 0

Not implemented:

- Universal Data Model classes.
- `UniversalEntity`.
- `UniversalField`.
- SQLite provider.
- Dapper repositories.
- SQL execution layer.
- AI assistant.
- LLM integration.
- migrations.
- import/export engines.
- backup engine.
- desktop UI.
- UI framework selection.

## Known Environment Requirement

To build and test the solution successfully, install a .NET SDK that supports `net10.0`.

After installing the correct SDK, run:

```powershell
dotnet build UniData.sln
dotnet test UniData.sln
```

## Next Logical Stage

Stage 1 - Universal Data Model.

This stage should define the first provider-independent model for structured data. It should be designed carefully and separately from database provider implementations.
