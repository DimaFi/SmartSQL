# Индекс задач GitHub

Полный канонический план: [WORK_PLAN.md](WORK_PLAN.md), 18 этапов и 123 подзадачи; следующий M01.02, M01.01 код/автотесты готов, UI открыт. Краткий статус: [TASKS.md](TASKS.md); ограничения/проверки: [CAPABILITY_MATRIX.md](CAPABILITY_MATRIX.md), [WORKSPACE_UI_ACCEPTANCE.md](WORKSPACE_UI_ACCEPTANCE.md). Номера T01–T25 и реальные issue URL сохраняются, дубликаты не создаются.

Репозиторий: [DimaFi/SmartSQL](https://github.com/DimaFi/SmartSQL). [Общий план #1](https://github.com/DimaFi/SmartSQL/issues/1). Источник для инструментов: [github-issues.json](github-issues.json). 25 задач + PLAN = 26 записей. Состояния issues при обновлении плана не закрываются/не открываются автоматически.

Последняя проверка кода — M01.01: 638 tests (1/129/469/39), 60 PG facts, без warnings/errors/skips. Независимое final review не выполнено после agent usage limit; UI открыт. Cloud/onboarding/новые provider slices не реализованы только от публикации плана. [Проверенные EXE/ZIP и receipts](DELIVERY.md); source commit текущего приложения не обязательно HEAD документации.

| ID | GitHub | Задача | Этапы исполнения |
| --- | --- | --- | --- |
| T01 | [#2](https://github.com/DimaFi/SmartSQL/issues/2) | Добавление одной строки PostgreSQL | M02.01, M09.06 |
| T02 | [#3](https://github.com/DimaFi/SmartSQL/issues/3) | Просмотр обычного SQLite | M02, M03, M09 |
| T03 | [#4](https://github.com/DimaFi/SmartSQL/issues/4) | Общий обозреватель и форма новой строки | M02, M10 |
| T04 | [#5](https://github.com/DimaFi/SmartSQL/issues/5) | Ручная приёмка новых PostgreSQL/SQLite сценариев | M02, M10, M11 |
| T05 | [#6](https://github.com/DimaFi/SmartSQL/issues/6) | Публикация плана и передачи работы в GitHub | Раздел 8 WORK_PLAN, T25 |
| T06 | [#7](https://github.com/DimaFi/SmartSQL/issues/7) | Безопасное удаление строки PostgreSQL | M02.01, M16.01, M16.05 |
| T07 | [#8](https://github.com/DimaFi/SmartSQL/issues/8) | Главное окно с деревом подключений и вкладками | M02.02, M03, M10 |
| T08 | [#9](https://github.com/DimaFi/SmartSQL/issues/9) | Выбор режима при первом запуске и постоянный переключатель | M02.09, M03, M10 |
| T09 | [#10](https://github.com/DimaFi/SmartSQL/issues/10) | Фильтры по типам и нативная сортировка | M02.03, M09, M12 |
| T10 | [#11](https://github.com/DimaFi/SmartSQL/issues/11) | Изменения обычного SQLite по подтверждённым ключам | M02.04, M16.04 |
| T11 | [#12](https://github.com/DimaFi/SmartSQL/issues/12) | Ручной SQL сначала для чтения | M02.05–M02.07, M10; optional M16.08 |
| T12 | [#13](https://github.com/DimaFi/SmartSQL/issues/13) | Нативный инспектор PK/FK/индексов/ограничений | M02.08, M16.01 |
| T13 | [#14](https://github.com/DimaFi/SmartSQL/issues/14) | Экспорт внешней таблицы/результата | M09.01–M09.04 |
| T14 | [#15](https://github.com/DimaFi/SmartSQL/issues/15) | Импорт/вставка Excel во внешние таблицы | M09.05–M09.08; M04 reusable managed wizard |
| T15 | [#16](https://github.com/DimaFi/SmartSQL/issues/16) | Быстрые расчёты и простые графики | M12 |
| T16 | [#17](https://github.com/DimaFi/SmartSQL/issues/17) | Полный английский интерфейс | M13 |
| T17 | [#18](https://github.com/DimaFi/SmartSQL/issues/18) | MySQL/MariaDB | M14 |
| T18 | [#19](https://github.com/DimaFi/SmartSQL/issues/19) | SQL Server | M15 |
| T19 | [#20](https://github.com/DimaFi/SmartSQL/issues/20) | Связи, смена типов, структура и перенос | M16 |
| T20 | [#21](https://github.com/DimaFi/SmartSQL/issues/21) | Копии на облачных дисках и личных серверах | M05–M08 |
| T21 | [#22](https://github.com/DimaFi/SmartSQL/issues/22) | Action/Permission и аудит | M17 |
| T22 | [#23](https://github.com/DimaFi/SmartSQL/issues/23) | Локальный помощник | M18 |
| T23 | [#24](https://github.com/DimaFi/SmartSQL/issues/24) | интерфейс и доступность | M01/M03/M04/M07/M10 и каждый UI-срез |
| T24 | [#25](https://github.com/DimaFi/SmartSQL/issues/25) | диплом и поставка | M11, M01, M03–M08 recovery/release gates |
| T25 | [#26](https://github.com/DimaFi/SmartSQL/issues/26) | качество и хранение | Разделы 7–9 WORK_PLAN; каждый этап |

Фактическое подтверждение последней публикации хранится в github-issues.json; pending не означает опубликовано. Полный текст плана находится в WORK_PLAN, в issues — связанный краткий scope и evidence.

M01.01 preview.7: чистая self-contained Windows x64 поставка `0.7.0-preview.7/win-x64-self-contained-verified-m0101-74fe89e` из опубликованного `74fe89e7cb1d71234926d870443ca561d9f7ccbd` (sourceDirty=false). Все 237 payload SHA/размеров и состав ZIP проверены: 78 756 941 байт, SHA256 `f30ed3a4c01e44080b55c189b109eb386700077ad6960f831ad80958c2c65de4`. Runtime-данных/модели нет. Debug 638 tests (1/129/469/39), 60 real PG facts, 0 warnings/errors/skips; owned PG stopped. Новый EXE на экране не запускался: computer-use initialization по-прежнему kernel assets/path/os error3. Installer не собран, M01.01/T04/T11/T12/T23 ручная приёмка открыта. Следующий кодовый срез M01.02.
