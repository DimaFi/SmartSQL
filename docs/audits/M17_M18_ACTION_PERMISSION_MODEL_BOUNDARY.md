# M17 → M18: Action/Permission и локальный помощник — безопасная граница

> **Security-oriented design proposal, NOT implementation.** Не изменяет D007/D008 и [WORK_PLAN M17/M18](../WORK_PLAN.md). Action/Permission отсутствует в работающей main версии, локальная модель не встроена, обучение и реальные eval не заявляются. Уже существующий ручной read-only SQL T11 — отдельный пользовательский инструмент, **не** разрешение на unrestricted SQL от модели.

## 1. Почему «LLM → SQL → Execute» запрещено

Промпт/распознанный текст модели нельзя считать trusted tool call. Контекст может содержать вредоносные инструкции в названиях таблиц, строках БД, комментариях, импортированном Excel и SQL history. Даже уверенный и синтаксически валидный план не предоставляет прав. DB grant + UI mode + assistant intent — три разных слоя; только provider принимает окончательное permission decision.

## 2. Минимальная boundary

```text
User intent / optional local model
   ↓ proposed structured action (UNTRUSTED)
Schema/type validator + available capability registry
   ↓ safe, bounded, target-qualified preview
User confirmation of exact action + target + expected scope
   ↓ short-lived confirmation token scoped to immutable preview
Fresh metadata/DB permission/context revalidation
   ↓ existing tested Application operation (no arbitrary SQL channel)
Result: completed | refused | conflict | cancelled | unknown-commit
   ↓ privacy-minimized audit (NO credentials / row dumps)
```

- **Allowlist** типов, например `ReadTable, FilterTable, CreateManagedProject, ExportResult, CreateVerifiedBackup` — добавлять строго по фактической реализации. `UpdateCell, InsertRow, DeleteRow, SchemaChange` только после своих capabilities/guards и отдельного подтверждения; deny unknown.
- Все targets явно квалифицированы: connection instance identity, database/schema/table/object IDs, key/row handle bound to exact loaded snapshot and policy, field ID; никакого выбора «первая таблица с похожим названием».
- Mutation preview сообщает SQL/provider tool effect как **объяснение**, не исполняемый AI SQL; затрагиваемую запись/схему, число строк если подтверждено, limits/backup/permission, warning potential irreversible changes.
- Review/check after preview: если изменились schema/row handles/roles/connection/session, confirmation must be invalidated. UI mode Advanced не превращает запретное в разрешённое.
- User confirmation is scoped and short-lived. Отмена до исполнения не запускает транзакцию; после отправленной записи/cancellation/unknown COMMIT не повторять самостоятельно.
- Audit captures action IDs, source provider/target opaque handle, timestamp, actual resolved capabilities, allow/deny reason, outcome and recovery link. Не содержит raw row values, секреты, SQL history, OAuth session URLs или модели user text целиком.

## 3. Разделить рекомендации и исполнение

- Локальная модель может предложить форму действия и задать уточнение, но не вызывать Infrastructure/provider напрямую.
- Для натурального языка в существующей простой таблице детерминированный parser/поиск может оставаться без модели.
- Offline/no model/timeouts: весь обычный GUI SQL/CRUD/import/export работает самостоятельно.
- Synthetic training candidates/holdout (D054) отражают реальные capability facts, а не создают permission grants. Любой export знаний после кодового среза — только allowlisted PUBLIC docs/source snapshots pinned to clean product commit; ML workspace D:\Code\SmartSQL-model независим. Не включать реальные схемы пользователей/историю/секреты и не запускать training/weights без отдельного разрешения.
- Image/voice/remote AI and automatic SQL execution — **не** включать по умолчанию из-за названия «SmartSQL».

## 4. 14 security gates (ACT01–ACT14)

| ID | Test |
| --- | --- |
| ACT01 | Unknown action/type/field/provider ⇒ deny before any DB call |
| ACT02 | Malicious instruction in cell/comment/table name ignored as data |
| ACT03 | Model proposes raw SQL/DDL ⇒ invalid structured action (no unrestricted executor) |
| ACT04 | Same table name in two connections ⇒ exact target required |
| ACT05 | Stale metadata/row identity between preview/confirm ⇒ fresh validation, no mutation |
| ACT06 | Role SELECT-only but model requests UPDATE ⇒ provider permission denial |
| ACT07 | UI Simple vs Advanced ⇒ same underlying authorization |
| ACT08 | User declines confirmation ⇒ no DB mutation/audit records declined |
| ACT09 | Cancel during operation ⇒ provider-defined rollback/unknown separated |
| ACT10 | Commit success but refresh fails ⇒ return committed-with-refresh-failure, no retry |
| ACT11 | Unknown COMMIT / lost network result ⇒ no automatic write retry |
| ACT12 | Audit logs/exports and model corpus scanned: no passwords/tokens/real row dumps |
| ACT13 | Disconnected session / destroyed tab during action ⇒ action invalidated, no stale executor |
| ACT14 | No local model installed or model times out ⇒ desktop workflows continue unaffected |

## 5. Decision gates and stages

**M17.01** typed action vocabulary and validation tests in Application, no AI dependency. **M17.02** provider capabilities/DB permissions integration with existing stable operations. **M17.03** bounded preview/confirmation UI. **M17.04/.05** secure executor/audit+adversarial tests. **M18.01–.03** independent isolated knowledge/evals and *measured* local model choice only after permission layer, then **M18.04–.06** action proposals through M17.

Не добавлять лишний workflow engine, cloud AI server, vector database or tool platform без подтверждённого узкого use case; не менять core dependency direction. Security acceptance требует проверенной runtime версии и настоящих отказов; этот документ — исходный design only.
