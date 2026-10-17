# M12 — расчёты и простые графики: точность, scope и guardrails

> **Design only, no implementation.** Канонический этап [WORK_PLAN M12](../WORK_PLAN.md), prerequisite M09 read/export contract, T09. Не добавлять BI framework или graph library без конкретной задачи. Привязка к текущему `SqlReadResult` (`Rows`, `Columns`, `Truncated`) и native table browse — прочитанные контракты, не гарантия новых возможностей.

## Принцип: scope → тип → вычисление → результат

**Scope:** уже показанный SQL-result (ограничение 1..500 строк, `Truncated`), текущая native страница, вся таблица/согласованная filtered selection через будущий M09 streaming. Не называть страницу «все данные». До расчёта показывать источник, фильтры, snapshot time, число включённых/пропущенных строк и факт усечения.

**Numeric:** считать `COUNT(rows)` отдельно от `COUNT(non-null column)`; `SUM/AVG` только для provider-confirmed numeric типов. PostgreSQL `numeric` может выходить за точность `.NET decimal`; Int64/SQLite INTEGER нельзя незаметно прогонять через `double`. Если нет точной bounded реализации для заявленного типа — либо provider-native агрегат через отдельный **заявленный read-only запрос с явным scope**, либо отказ с объяснением; ручной SQL result не перезапускать скрыто.

**Null/empty:** NULL пропускается в sum/avg и учитывается отдельно; пустая строка не трактуется как ноль, пробельная не преобразуется неявно. Нет чисел → «нет значений», а не 0 для AVG.

**Graph:** category/value only for a bounded set; long Cyrillic labels and 0/null encoded as text/accessible legend, negative values and repeated labels handled. Unbounded cardinality must be rejected/aggregated only with explicit choice, не выбрасывать хвост тихо. График read-only, не изменяет SQL/DB/правила фильтрации.

## 12 acceptance cases (CALC01–12)

| ID | Сценарий |
| --- | --- |
| CALC01 | 500-row SQL result Truncated=true → visible «только показанные строки», no full-DB claim |
| CALC02 | Page 2 vs full table has distinct scope and count |
| CALC03 | COUNT rows 10, COUNT(non-NULL) 7 verified distinctly |
| CALC04 | AVG with all NULL shows no numeric result, not NaN/0 |
| CALC05 | Unsupported domain/binary/date as number rejected explicitly |
| CALC06 | PostgreSQL high-precision numeric exact expected outcome, no float rounding |
| CALC07 | SQLite INTEGER64 beyond Excel safe integer preserved |
| CALC08 | Negative/zero/very large numbers chart axis/legend truthful |
| CALC09 | Repeated/empty category, Unicode long labels distinguishable |
| CALC10 | More categories than defined limit: explicit warn/refuse, no silent discard |
| CALC11 | Cancel/error/external writer during full-scope aggregate → no partial «complete» |
| CALC12 | Export graph/result with scope, filters, timestamp, limits; keyboard/Narrator/Gray/Light |

**Delivery slicing:** First safe slice uses *already materialized limited result* and descriptive count only; next typed sum/avg after numeric contract; then one accessible chart type and export. M12 doesn't need all M09 native writes / all DBMS future providers. Update M13 RU/EN parity after final M12 status.
