# Project storage on D:

The main checkout remains `D:\Code\SmartSQL`. The independent experimental local-assistant source is `D:\Code\SmartSQL-model`. Source/test directory layouts are unchanged; installed Codex, .NET SDK, Windows and other system tools remain installed where they were.

Use each workspace's own helpers so project-created files stay in that workspace on D:

```powershell
./scripts/Build.ps1
./scripts/Test.ps1
./scripts/Run.ps1
```

For ML, run `scripts/Build.ps1` and `scripts/Test.ps1` from `D:\Code\SmartSQL-model`. The legacy main `-Model` switch delegates to the model-local initializer; it does not use the main package cache or config. The model repository is [DimaFi/SmartSQL-ML](https://github.com/DimaFi/SmartSQL-ML). Every model source/docs/script/data/cache/temporary/result stays inside its own folder.

Add `-Offline` to Build/Test/Run when the prepared package cache is sufficient and network access is unavailable. Only that explicit offline restore skips the online vulnerability feed. `-NoBuild -NoRestore` tests an already-built stage. `Run.ps1 -NoBuild -Detached` opens the built desktop executable; `-DataDirectory <absolute path>` selects an isolated data copy for UI checks.

| Contents | Location | Git |
|---|---|---|
| Main source, tests, docs, scripts | `D:\Code\SmartSQL` | Tracked |
| Default database, demo, adjacent automatic backups | `D:\Code\SmartSQL\data\local` | Ignored |
| NuGet packages/HTTP/scratch cache, .NET CLI state | `D:\Code\SmartSQL\.workspace` | Ignored |
| Main test data, temporaries, TRX results, UI copies | `D:\Code\SmartSQL\.workspace` | Ignored |
| Model source and tests | `D:\Code\SmartSQL-model\src`, `tests`, `cases` | Separate Git repository |
| Model test results, temporaries and migration backups | `D:\Code\SmartSQL-model\.workspace` | Ignored |
| Model NuGet packages/HTTP/scratch cache and .NET CLI state | `D:\Code\SmartSQL-model\.workspace` | Ignored; independent of main cache |
| Future local model caches and weights | `D:\Code\SmartSQL-model\local`, `models` | Ignored |
| Compilation outputs | Existing `bin`/`obj` beside each project on D: | Ignored |

The scripts set process-local `TEMP`, `TMP`, `TMPDIR`, `DOTNET_CLI_HOME`, bundle extraction, NuGet directories and test locations. They disable .NET/Avalonia build telemetry and first-run certificate/global-tools changes. No machine environment or user profile is changed.

Direct desktop launches locate `SmartSQL.sln` above the executable and use `data/local` there. A packaged app without a checkout uses `data/local` beside its executable. `SMARTSQL_DATA_DIRECTORY` and `SMARTSQL_TEMP_DIRECTORY` can select absolute paths; invalid/unwritable configuration fails instead of reverting to C:.

## Copy-first migration

The previous `%LOCALAPPDATA%\SmartSQL` files were copied into `data/local` with original filenames. Prior SmartSQL-only Temp artifacts were copied into `.workspace/legacy-local` under their original directory names. All 270 copied files were verified against SHA-256 before/after copy; five databases passed read-only SQLite integrity checks without changing their hashes. The originals on C: remain as recovery copies and are no longer default working files. NuGet package versions needed by the solution were copied into the D: cache.

`.workspace/legacy-local/copy-manifest.json` records source/destination hashes. The reusable `scripts/Copy-LegacyData.ps1` never overwrites differing destination files or deletes source files; it refuses active SQLite journals. Database copies require read-only integrity verification before use.

The incomplete model directory on D: was supplemented from the staging source after backing up differing target files. Its verified manifest and previous versions are in `D:\Code\SmartSQL-model\.workspace`. The old main `_model_build` copy is now archived under `D:\Code\SmartSQL-model\.workspace\migration-backup\legacy-main-staging`; all 15 files retain verified SHA-256 hashes. Its local manifest is `.workspace/legacy-main-staging-manifest.json` in the model folder. Active work uses the separate model repository, not the archive. No model weights were found, and no language model was connected to the desktop.
