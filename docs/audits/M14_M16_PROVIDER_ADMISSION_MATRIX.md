# M14/M15 — provider admission: MySQL, MariaDB, SQL Server

> **Planned research checklist.** Эти DBMS **не** подключены в текущей реализации. Единственный план [WORK_PLAN M14/M15](../WORK_PLAN.md); начинать после реальной стабилизации PostgreSQL/SQLite, M09 и RU/EN условий по зависимостям. Никаких driver packages/server installs/credential tests не выполнялось в этом review. **Одна успешная MySQL test fixture не подтверждает MariaDB.**

## 1. Minimal capability admission contract

| Capability | Требуемые независимые доказательства |
| --- | --- |
| Connect | version/edition/driver/license/client/runtime packaging, TLS validation, auth mechanism, sandbox fixture, timeout/revoke; no stored passwords |
| Metadata | schemas, tables/views/partitions, quoted case-sensitive names, PK/FK/indexes/constraints, partial/unknown dependency coverage, permission-denied states |
| Native values | NULL/empty/whitespace, signed/unsigned ranges, DECIMAL precision, date/time/timestamp/timezone, binary/BLOB, JSON, generated/identity/defaults, collations, unsupported type reporting |
| Read/query | paged stable order, provider-confirmed filters/search/native sort, independent read-only statement parser/permissions, cancel/drain, no hidden SQL mutation |
| Write | independent guarded single-cell UPDATE/single-row INSERT/DELETE with actual DB rights, stable row identity, schema/data conflict, triggers/FKs, unknown COMMIT, explicit source-specific recovery |
| Import/export | M09 loss-aware scope+type mapping, consistent read, transactional batch when provider supports it, no false atomicity, rollback/restore |
| Backup/restore | *provider-specific* production or test scope explicitly defined; table CSV is not database backup |
| UI/release | Gray/Light, Simple/Advanced, RU/EN only when full accepted workflow, Windows package native dependencies, limits and supported version list |

## 2. Provider differences that must not be guessed away

- **MySQL and MariaDB** share protocol/history but have diverging versions, defaults, SQL dialect, grants, type/collation and native client behavior. Test separately with supported exact versions. No `SHOW ...`/information_schema assumption leaks into Application or Domain; provider-specific SQL in Infrastructure.
- **SQL Server** requires own supported auth/TLS/certificate policy, driver edition/license decision and actual DB rights; Windows integrated auth and SQL login are distinct modes. No acceptance by installing SSMS or SQL Server dev edition only; actual target must be covered.
- PostgreSQL existing `xmin`/guarded lock strategy is **not universal**, nor SQLite raw-value row compare. Capturing a PK does not authorize UPDATE/DELETE; unknown metadata/trigger/dependency means conservative refusal.
- Existing manual read-only SQL T11 is provider-native security boundary; app-level SQL regex/parser is not adequate permission enforcement. Stored procedures/user-defined functions, external effects, temp objects and permissions require separate admission policy.
- MySQL/MariaDB DATE/TIMESTAMP zero-values and unsigned ranges, SQL Server datetimeoffset/rowversion/decimal and collations may have distinct semantics; each exact behavior requires fixtures **before** declaring conversion support.

## 3. Fixtures and research tasks (12 future gates, PROV01–12)

| ID | Scenario |
| --- | --- |
| PROV01 | Exact supported product/version matrix and driver license/repackaging documented |
| PROV02 | TLS invalid cert fails; no global SSL disable; correct cert/path works |
| PROV03 | No password in connection profile, logs, Git or ML |
| PROV04 | Missing SELECT vs read-only connection vs denied metadata reflected honestly |
| PROV05 | Quoted/case-insensitive/confusable Unicode schema/table names work without object mixup |
| PROV06 | UINT/DECIMAL/huge NUMERIC/JSON/BLOB/datetimeoffset native values exact or refused |
| PROV07 | Independent browsing/typed filters/sort before pagination, stable session isolation |
| PROV08 | Read-only single-statement enforcement cannot be bypassed by comments, multi-statements, stored routine invocation |
| PROV09 | UPDATE/INSERT/DELETE permissions and complex dependencies separate, fail closed when unknown |
| PROV10 | Concurrent writer/schema DDL/trigger effects/unknown COMMIT not silently repeated |
| PROV11 | Export/import exactness/loss report and supported backup/restore path passed |
| PROV12 | Driver native packaging, startup, cancellation, locale/RU+EN/keyboard in claimed OS test |

**Implementation strategy:** M14.01 matrix/driver choice first, then read-only catalog/one object, then separate provider-native SQL, then guarded writes, then export/recovery and manual acceptance. SQL Server M15 only after accepted same workflow. Avoid adding providers prematurely simply to inflate feature count.

## 4. Relationships and M16 boundary

M16 may be implemented in small independent slices even before all DBMS providers: incoming FK metadata coverage, managed type conversion with backup, native SQLite INSERT/DELETE, PG incoming FK-delete protection, cross-provider migration after corresponding M09 import/export. A relationship visible in inspector does not grant permission to edit/delete foreign object. Unknown dependency **never** means no dependency. Schema DDL has different transaction guarantees by provider; no universal rollback claim.

### Future migration report minimum

Source/provider/version/type/precision/collation/timezone/identity/generated/default/constraints/index/sequence/ACL, chosen target mapping and loss table, backup/restoration strategy, row counts/hashes/incompatibilities before committing. Destination new, source remains unchanged; no forced conversion for unsupported columns. PG→SQLite cannot magically preserve all PG capabilities.

**No external provider version/security API claims in this design:** Exact supported versions must be sourced from official vendor documentation at *implementation time* and tested with isolated permissioned fixtures.
