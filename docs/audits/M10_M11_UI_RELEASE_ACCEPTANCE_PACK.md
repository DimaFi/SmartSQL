# M10/M11 — интерфейс, доступность и воспроизводимая поставка: acceptance pack

> **Интеграционная пометка 17.10.2026:** reference/proposal, не новая принятая архитектура и не реализованные функции. Использование, перепроверка источников и ограничения — [реестр интеграции](BRANCH_INTEGRATION_2026-10-17.md). WORK_PLAN / DECISIONS остаются источниками текущего порядка и решений.

> **Только исследование и тест-план.** Не заявляет ни завершённую M10 UI-приёмку, ни installer, ни опубликованную новую версию. Связан с [WORK_PLAN](../WORK_PLAN.md), [DELIVERY](../DELIVERY.md), [WORKSPACE_UI_ACCEPTANCE](../WORKSPACE_UI_ACCEPTANCE.md). Реально существующие тесты M01.02 исторически 676; по этому пакету **новые тесты не выполнялись**.

## 1. Подтверждённая исходная точка из кода

- `scripts/Publish.ps1` делает **folder-based** `win-x64` self-contained deployment: явные `-p:PublishSingleFile=false` и `-p:PublishTrimmed=false`. **Не утверждать, что текущий SmartSQL single-file EXE bundle.** `SmartSQL.Desktop.exe` — входной EXE внутри папки с зависимостями.
- `scripts/Publish.ps1` пишет source SHA, sourceDirty, версию, SDK и payload SHA256, отдельный ZIP + SHA256; обнаруживает runtime data в payload; версионированную поставку создаёт под D:\Code\SmartSQL-builds без перезаписи.
- `scripts/Run-Published.ps1` сейчас отдельно выбирает `run-data/local`, а `WorkspacePaths` — `data/local`; это открытый P0 M01.04, **не** исправлять заменой installer recipe в обход одного resolver.
- `packaging/windows/SmartSQL.iss` имеет постоянный AppId и **не** содержит `[UninstallDelete]` для пользовательских данных. Это желательная стратегия, но **установку/переустановку/удаление не проверяли**. `Build-Installer.ps1` проверяет SHA и наличие ISCC; отсутствие ISCC остаётся blocker, нельзя произвольно устанавливать компилятор на пользовательский ПК.
- `App.axaml.cs` применяет ThemePreferences до создания WorkspaceWindow. `WorkspaceWindow` использует уже выбранные mode/theme, native tabs, разные sessions. Изменения M01.04 обязаны разрешить root до ThemePreferences.
- `WORKSPACE_UI_ACCEPTANCE.md` перечисляет ручные остатки T04/T09/T10/T11/T12/T23. Старые проверки на экране частичны, не переносить их на новую сборку как полную приёмку.

## 2. Product install/update/uninstall semantics (M11)

| Scenario | Test boundary / ожидаемое | Что не делать |
| --- | --- | --- |
| Direct exe, ZIP-extracted exe, Start Menu shortcut, installer launch | Один rootId/bootstrap при том же выбранном product mode; разделение binary/user storage | Разные default directories внутри version folder |
| Preview → update to new version | Прежний `smartsql.db`, preferences, SQL history, verified backup сохраняются; old external DB paths продолжают указывать туда же | Создавать новый default только потому, что версия другая |
| Portable ZIP move | Явный marker, relative data root moves together; no installed bootstrap write | ZIP само по себе не означает portable |
| Uninstall/reinstall | Удаляются только собственные бинарники/shortcut; root и внешние проекты остаются по default policy | Рекурсивное удаление неучтённых пользовательских файлов |
| Mismatch/corrupt/future storage marker | Fail-closed startup с указанием пути и возвратом, без создания пустой БД | «Починили» новым rootId без согласия |
| Locked file / power-loss / antivirus on installer | Изолированный failure proof и аккуратный retry/rollback где обещан | Объявлять успешный install только по факту появления setup.exe |
| User PC no D:, no SDK, no PostgreSQL | Продуктовая поставка открывает локальный SQLite в разрешённой чистой VM и не требует D: | Тестировать product policy записью в профиль реального разработчика |
| Offline/no admin install | Проверить реально заявленные per-user permissions и runtime packaging | Навязывать elevated install без подтверждённой необходимости |
| SourceDirty=true publication | Честная metadata; релиз с незафиксированным source запрещён release gate | Считать verified по существованию ZIP |

**Important .NET packaging nuance:** current `PublishSingleFile=false`. Официальные предупреждения про `DOTNET_BUNDLE_EXTRACT_BASE_DIR` относятся к **будущему возможному** single-file со self-extract, не доказанно существующему ZIP. Не включать `PublishSingleFile=true`/Trimming/AOT ради скорости без независимого совместимого теста Avalonia/native dependencies. Self-contained на .NET означает, что runtime включён, но не всё нативное/OS окружение; разные ОС требуют отдельного RID.

## 3. UI — четыре обязательных прохода

1. **Navigation & state:** Ctrl+Tab/Ctrl+W/Ctrl+Enter, двойной клик/Enter в дереве, refresh/close/disconnect другого соединения, SQL history, native inspector, file picker lifecycle и background cancellation. Текущие query text, caret/scroll/filter/sort/page/selection не исчезают от theme/mode. User SQL никогда не исполняется автоматически после открытия файла/восстановления history.
2. **Data semantics:** NULL vs `""` vs whitespace читаются текстом/иконкой, не только цветом; native unsupported write reasons доступны в Simple и Advanced. Full cell/long metadata view и copy не обрезают содержимое. INSERT/DELETE/UPDATE permissions не меняются от UI mode.
3. **Layout & visibility:** Gray/Light, 960×600 (сейчас declared minimal acceptance), 100/125/150/200% DPI, long Russian labels and Unicode paths, 15+ tabs, resizable splitter, tables with many columns and horizontal scrollbar, disabled controls and error/empty/loading states. Никаких clipped primary controls.
4. **Assistive UX:** Tab/Shift+Tab, Escape, Enter, Ctrl+Tab, visible focus, Narrator on actual Windows version, meaningful AutomationProperties.Name/HelpText for icon-only buttons, predictable dialog focus return and announcement of validation/status. Test tooltip != screen-reader name. Не объявлять accessibility complete по чистому screenshot.

Avalonia официально описывает automation peers, `AutomationProperties.Name`, `HelpText`, `LabeledBy` и Tab navigation; automation API подтверждает способ реализации, **не** фактическую пригодность SmartSQL для Narrator.

## 4. 18 release/UI scenarios (R01–R18)

| ID | Gate |
| --- | --- |
| R01 | Build/manifest/ZIP reproduce clean commit SHA and file hashes, zero user data in payload |
| R02 | Two consecutive version builds retain prior package and checksum, no overwrite |
| R03 | Direct EXE vs ordinary shortcut vs user launcher same root after M01.04 |
| R04 | Upgrade same selected user root + settings/history + backed-up DB preserved |
| R05 | Uninstall/reinstall does not delete default/portable/external user data |
| R06 | Corrupt/missing/future root ID error before any DB open, readable early startup |
| R07 | No SDK/no D:/no server isolated consumer VM, offline local SQLite works |
| R08 | Installer ISCC missing ⇒ exact blocker, no false installation badge |
| R09 | Install interrupted/locked destination, previous data and binaries unaffected or clear recoverable state |
| R10 | Shortcut/taskbar/dialog/Alt+Tab small DPI S logo and correct product name |
| R11 | Both themes + Simple/Advanced mode preserve SQL text/results/history and exact permissions |
| R12 | All active dialogs fit minimum 960×600 / 125–200% DPI, keyboard accessible |
| R13 | Narrator names icon-only actions and error text, no focus trap |
| R14 | Native SQL Execute/Cancel/Open/Save file modal closure doesn't dispose active session prematurely |
| R15 | PG/SQLite same table name in two connections retains independent page/filter/result |
| R16 | Native edit permission refusal, backup-required UPDATE, unknown commit and conflict status visible |
| R17 | M01.02 before-upgrade copy version/path reported in UI, open errors preserve active tab |
| R18 | Future backup status (M05–M07) distinguishes local/remote/restore, doesn't claim unimplemented cloud as completed |

## 5. Evidence template для Codex (не подменять)

Каждую проверку сохранять в `docs/WORKSPACE_UI_ACCEPTANCE.md` **после факта**, с build/commit ID, viewport + DPI, test DB synthetic path under D:, steps, actual observed result, expected result, pass/fail/not-run reason, no screenshot with passwords. Automated tests отдельно: command line, counts/failures/skips, TRX SHA/path in ignored `.workspace`.

Для release scope: sourceDirty, source commit, output root, manifest and ZIP checksums, installed compiler detected/missing, actual launch on clean VM, repair/update/uninstall tests. **Наличие manifest и ZIP сами по себе не доказывают доступность программы на другой ОС**.

## 6. Sources (17 октября 2026)

- [Microsoft .NET publish models](https://learn.microsoft.com/en-us/dotnet/core/deploying/), [single-file specifics](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview).
- [Inno Setup stable AppId](https://jrsoftware.org/ishelp/topic_setup_appid.htm): приложение с прежним AppId при обновлении сопоставляется с тем же uninstall log.
- [Avalonia Accessibility](https://docs.avaloniaui.net/docs/app-development/accessibility): accessibility properties/automation peers, не substitute for manual Windows validation.

Этот файл не предлагает менять иконку/палитру/движок UI без задачи, подписывать/автообновлять приложение без отдельного release decision или утверждать Linux/macOS готовыми.
