# UniData

Universal intelligent structured data management system with an adaptive natural-language interface.

## Status

Stage 0 - Project Foundation.

The repository currently contains only the solution structure, minimal projects, and documentation. Universal Data Model, providers, SQLite, Dapper, AI, import/export, backups, and UI are not implemented yet.

## Solution Structure

```text
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
```

## Requirements

- .NET 10 SDK

## Build

```powershell
dotnet build UniData.sln
```

## Test

```powershell
dotnet test UniData.sln
```
