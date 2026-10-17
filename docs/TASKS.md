# Задачи SmartSQL: краткий реестр

**Единственный полный порядок, подзадачи и acceptance — [WORK_PLAN.md](WORK_PLAN.md): 18 этапов M01–M18, 123 конкретные подзадачи.** T01–T25 сохраняют прежние номера/URL; это соответствие, не другая очередь. Текущая следующая реализация — **M01.04** (M01.03/D056 storage decision готов, runtime/миграция и UI открыты); M02/manual acceptance остаётся открытой.

Последний кодовый срез M01.02: Debug 0 warnings/errors; 676 passed / 0 skipped (Domain 1 / Application 144 / Infrastructure 492 / Desktop 39), включая 60 real PostgreSQL facts. TRX *2026-10-17_11_55_01*.trx и *2026-10-17_11_55_02*.trx; owned PostgreSQL 17 штатно остановлен. 23 новых infrastructure cases и 15 application cases. Независимый read-only review Luna: блокирующих замечаний нет. Экранная инициализация снова отказала (kernel assets/path/os error 3); новая UI-приёмка не заявляется. Подробнее: [контракт](MANAGED_PROJECT_OPENING.md). [Матрица](CAPABILITY_MATRIX.md), [UI evidence](WORKSPACE_UI_ACCEPTANCE.md), [HANDOFF](HANDOFF.md).

| ID / GitHub | Задача | Этапы | Фактический статус |
| --- | --- | --- | --- |
| [T01](https://github.com/DimaFi/SmartSQL/issues/2) | Добавление одной строки PostgreSQL | M02.01, M09.06 | Код/тесты single-row INSERT есть; ручная приёмка открыта |
| [T02](https://github.com/DimaFi/SmartSQL/issues/3) | Просмотр обычного SQLite | M02, M03, M09 | Код/тесты ordinary SQLite есть; остаток UI открыт |
| [T03](https://github.com/DimaFi/SmartSQL/issues/4) | Общий обозреватель и форма новой строки | M02, M10 | Общий native browser и insert form реализованы; UI открыт |
| [T04](https://github.com/DimaFi/SmartSQL/issues/5) | Ручная приёмка новых PostgreSQL/SQLite сценариев | M02, M10, M11 | Ручная приёмка частичная, остаётся открытой |
| [T05](https://github.com/DimaFi/SmartSQL/issues/6) | Публикация плана и передачи работы в GitHub | Раздел 8 WORK_PLAN, T25 | Первоначальная публикация выполнена; актуализация постоянна |
| [T06](https://github.com/DimaFi/SmartSQL/issues/7) | Безопасное удаление строки PostgreSQL | M02.01, M16.01, M16.05 | Первый guarded DELETE срез есть; все incoming FK запрещены |
| [T07](https://github.com/DimaFi/SmartSQL/issues/8) | Главное окно с деревом подключений и вкладками | M02.02, M03, M10 | Workspace/tree/tabs есть; полный UX/lifecycle не принят |
| [T08](https://github.com/DimaFi/SmartSQL/issues/9) | Выбор режима при первом запуске и постоянный переключатель | M02.09, M03, M10 | Simple/Advanced и память есть; новый onboarding не реализован |
| [T09](https://github.com/DimaFi/SmartSQL/issues/10) | Фильтры по типам и нативная сортировка | M02.03, M09, M12 | Typed AND/native sorting реализованы; UI остаток |
| [T10](https://github.com/DimaFi/SmartSQL/issues/11) | Изменения обычного SQLite по подтверждённым ключам | M02.04, M16.04 | Только guarded SQLite single-cell UPDATE; UI частичный |
| [T11](https://github.com/DimaFi/SmartSQL/issues/12) | Ручной SQL сначала для чтения | M02.05–M02.07, M10; optional M16.08 | Read-only SQL/подсветка/lifecycle реализованы; UI открыт |
| [T12](https://github.com/DimaFi/SmartSQL/issues/13) | Нативный инспектор PK/FK/индексов/ограничений | M02.08, M16.01 | Первый object-owned inspector есть; UI открыт |
| [T13](https://github.com/DimaFi/SmartSQL/issues/14) | Экспорт внешней таблицы/результата | M09.01–M09.04 | Native export не реализован |
| [T14](https://github.com/DimaFi/SmartSQL/issues/15) | Импорт/вставка Excel во внешние таблицы | M09.05–M09.08; M04 reusable managed wizard | Native import/paste не реализованы |
| [T15](https://github.com/DimaFi/SmartSQL/issues/16) | Быстрые расчёты и простые графики | M12 | Расчёты/простые графики не реализованы |
| [T16](https://github.com/DimaFi/SmartSQL/issues/17) | Полный английский интерфейс | M13 | Полный English не реализован |
| [T17](https://github.com/DimaFi/SmartSQL/issues/18) | MySQL/MariaDB | M14 | MySQL/MariaDB не реализованы |
| [T18](https://github.com/DimaFi/SmartSQL/issues/19) | SQL Server | M15 | SQL Server не реализован |
| [T19](https://github.com/DimaFi/SmartSQL/issues/20) | Связи, смена типов, структура и перенос | M16 | Managed primitives есть; полный schema/migration scope не реализован |
| [T20](https://github.com/DimaFi/SmartSQL/issues/21) | Копии на облачных дисках и личных серверах | M05–M08 | Local backup primitives есть; Google/Яндекс/автоматика не реализованы |
| [T21](https://github.com/DimaFi/SmartSQL/issues/22) | Action/Permission и аудит | M17 | Action/Permission/audit не реализованы |
| [T22](https://github.com/DimaFi/SmartSQL/issues/23) | Локальный помощник | M18 | Stubs и подготовка knowledge/examples/holdout отдельно; реальной модели/интеграции нет |
| [T23](https://github.com/DimaFi/SmartSQL/issues/24) | интерфейс и доступность | M01/M03/M04/M07/M10 и каждый UI-срез | Часть UI принята; полный проход открыт |
| [T24](https://github.com/DimaFi/SmartSQL/issues/25) | диплом и поставка | M11, M01, M03–M08 recovery/release gates | EXE/ZIP preview.7 есть; installer/public release gates открыты |
| [T25](https://github.com/DimaFi/SmartSQL/issues/26) | качество и хранение | Разделы 7–9 WORK_PLAN; каждый этап | Постоянная обязанность |

T05 выполнен исторически; T25 ведётся постоянно. Входные решения/реальные OAuth/ISCC/screen blockers описаны в разделе 7 плана. Старые Stage/N/A-группировки не являются самостоятельной очередью.
