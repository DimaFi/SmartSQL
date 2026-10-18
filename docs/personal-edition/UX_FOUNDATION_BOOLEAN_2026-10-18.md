# UX Foundation — managed Boolean choices (UF09-05)

**Status: implementation candidate, not accepted.** PR branch `feat/ux-managed-boolean-choices-20261018`, based on `main` (18 October 2026). C# code/tests pushed. No build/xUnit/Windows UI has been executed in the remote GitHub editing environment.

## User problem and observed code path

User's preview.9 feedback: newly-created Boolean («Да / Нет») fields have no user-friendly choice editor.

Current managed editor `MainWindow.axaml.cs` builds a `DataGridTextColumn` for every field, including `FieldType.Boolean`. `ForDisplay` maps stored `"1"/"0"` to `"Да"/"Нет"`. `EditableCell.Value` invokes the existing `IDataWorkspace.UpdateCell`, while `FieldValue.Normalize` maps human input to `"1"/"0"` and `null` to `null`. No storage/API change required.

## Candidate implementation

- In **managed table only**, Boolean column uses `DataGridTemplateColumn` with a `ComboBox` and explicit options **Да**, **Нет**, **Не задано**.
- Nullable/unset is a real `null`, never interpreted as false or empty text. Original managed workspace accepts missing Boolean values.
- The change still calls existing `EditableCell.Value` → `UpdateCell`. A failed persistence operation retains old cell value and restores the prior dropdown choice. No new SQL, automatic retry, schema mutation, or data conversion.
- Existing numeric/date/text fields keep their current editor. Native SQLite and PostgreSQL Boolean editors are **not** altered by this PR; they have their own provider-specific capability/permissions.
- Option model `ManagedBooleanChoices` refuses to map unknown stored/display values to NULL/false.
- Added xUnit tests for true/false/null, unknown, option order, save count and save-failure preservation.

**Files:** `src/SmartSQL.Desktop/Views/MainWindow.axaml.cs`; new `src/SmartSQL.Desktop/Views/ManagedBooleanChoices.cs`; new `tests/SmartSQL.Desktop.Tests/ManagedBooleanChoicesTests.cs`.

## Required local verification (NOT run here)

1. In `D:\Code\SmartSQL`, inspect branch status, build with `scripts/Build.ps1`, run `scripts/Test.ps1`; include real synthetic managed SQLite fixtures. Do not modify actual user DB or D: root policy.
2. Create a Boolean field; add a row with unspecified value. ComboBox must show **Не задано**; select **Да** → reopen → **Да** / stored `1`; choose **Нет** → reopen → stored `0`; choose **Не задано** → reopen → actual SQL NULL.
3. Try repeated selection without change: no new update/undo. Test induced write failure/readonly/no space, ensure dropdown shows prior value and error remains visible.
4. Undo after each value including NULL; confirm original value and no lost data. Test sort, filtering, paging, column reorder and recycling virtualized rows; choice should always belong to the correct row/field, never index of another table.
5. Test mouse/keyboard/tab focus, minimum window, Light/Gray, 100/150/200% scaling and screen reader naming; multiple table tabs and Simple/Advanced toggling do not change data/permissions.
6. Confirm native PostgreSQL/SQLite and non-Boolean fields unchanged; no data corruption. If C# compilation or UI bug occurs, fix in branch and retest before merge.

## Limitations / follow-up

- This is the **managed** Boolean part of UF09-05; native Boolean choices and native row insertion UI are separate provider-specific slices.
- No evidence for full UF09-05 acceptance until actual compiled app UI is tested by a user. Do not mark it closed in USER_FEEDBACK or WORK_PLAN based on documentation/PR alone.
- Current `MainWindow` still has some old default DB onboarding behavior. No changes to M01.04/M01.05 storage, root selection, profiles, or schema here.
