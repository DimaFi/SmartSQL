# Из Personal в Team — отдельная вторая программа развития

**Это следующий этап, не часть первой персональной поставки.** SmartSQL Personal остаётся работоспособным без сервера, учётной записи и интернета. Главный принцип: Team не превращает desktop в «тонкий клиент» и не заменяет существующие СУБД.

## 1. Чего сейчас не делать

Не создавать `SmartSQL.TeamServer`, web shell, multitenant identity DB, push-sync SQLite или облачную авторизацию только ради красивой схемы. Не заставлять пользователей Personal передавать данные/SQL в чужой backend. Не обещать «командное редактирование», если несколько клиентов пишут в SQLite файл через sync-папку: нужна настоящая серверная СУБД/контролируемый доступ и concurrency policy.

## 2. Что можно заранее сохранить, не реализуя Team

- Источники данных имеют стабильную явную идентичность, не display name.
- Application use cases отделены от Avalonia event handlers и native SQL.
- Операции возвращают различимые `completed/conflict/unknown/failed` состояния.
- Versioned local nonsecret profiles и snapshot manifest, безопасная миграция.
- Политика Action/Permission и Audit будет работать для локального человека/локального ИИ, и только позднее станет точкой командной интеграции.
- Внешняя СУБД всегда защищена своими нативными ролями; Team RBAC — дополнительное ограничение, не замена GRANT/RLS/TLS.

Это **тонкие швы**, не заглушки сервера или заранее огромные абстракции.

## 3. Кандидатная архитектура Team — проектировать отдельно

```text
SmartSQL Desktop (Personal functions + optional team identity)
                   │
               HTTPS API
                   │
SmartSQL Team Server (ASP.NET Core)
  Identity/OIDC/SAML | RBAC | connection/resource registry
  team scripts       | audit | scheduling | collaboration
  vault integration  | policy | bounded job orchestration
                   │
       server metadata DB (e.g. PostgreSQL)
                   │
      permitted access to existing DBMS targets

SmartSQL Web (separate browser client for permitted jobs)
```

Desktop продолжает локальную работу, если Team недоступен; политика организации может отдельно запретить определённые team-controlled операции offline. Серверная metadata DB хранит пользователей/разрешения/состояния общих ресурсов, **не** автоматически дублирует произвольные пользовательские БД. Доступ к target DB должен быть явно выбран: direct desktop connection либо audited team gateway, с отдельной моделью сетевых маршрутов/секретов.

## 4. Team epics

| ID | Epic | Ограничения / Definition of done |
| --- | --- | --- |
| TEAM-01 | Team server и versioned API | auth, health, migrations, backup/restore, deploy, API compatibility |
| TEAM-02 | Identity & teams | invitations, OIDC, role/tenant boundary, deactivate, sessions |
| TEAM-03 | RBAC per resource | server-side deny-by-default, native DBMS grants still apply |
| TEAM-04 | Shared connection profiles | centrally encrypted secrets, users never see ungranted credentials |
| TEAM-05 | Shared SQL resources | revisions, conflicts, approval, audit, private vs shared scripts |
| TEAM-06 | Collaboration | presence & changes, no last-writer-wins, conflict reconciliation |
| TEAM-07 | Web | same contract semantics, least privilege, CSRF/CSP/XSS, safe result streaming |
| TEAM-08 | Scheduler/jobs | durable queue, lease/idempotency, timeout/cancel, restricted executor |
| TEAM-09 | Enterprise auth & security | SSO/SAML/Kerberos as needed; secrets rotation, audit retention |
| TEAM-10 | Observability and scale | backups, HA tests, rate limits, isolation/quotas, upgrades |
| TEAM-11 | Deployment | containers/on-prem, TLS, upgrade rollback, OS support |
| TEAM-12 | Acceptance | two organizations/users, role matrix, offline client, real DB permissions |

Все новые Team epics пока **НЕ добавлены** к исполнению M01–M18. До открытия Team phase нужен отдельный ADR: data residency, deployment model, threat model, SSO providers, API boundaries, pricing/license decisions, tenant isolation, UI clients, test accounts and rollback path.

## 5. Когда переход оправдан

Personal достиг подтверждённой стабильности и «first-use» сценариев; пользователь освоил табличный/SQL/ERD/импорт без помощи; provider permissions/backup/transaction model проверены; есть реальный спрос на совместные скрипты и источники. Тогда Team строится **над**, а не **вместо** зрелого локального приложения.
