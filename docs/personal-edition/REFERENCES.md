# Источники, метод сравнения и оригинальности

Сверено по публичным официальным страницам **18 октября 2026** (часть страниц может меняться без предупреждения). Это ссылки на **функции и принципы**, а не лицензии на копирование дизайна, proprietary source, assets или торговых названий. При реальной реализации проверь текущую редакцию каждого источника.

## DBeaver Lite — базовая функциональная контрольная точка

- [Lite Edition — официальная документация](https://dbeaver.com/docs/dbeaver/Lite-Edition/) — индивидуальная редакция, data editing, filter/sort, chart, ERD, Excel, SQL AI, builder, history, security, drivers.
- [Сравнение редакций](https://dbeaver.com/edition/) — изолировать Lite scope от Enterprise и Team: schema/data compare, Git project sync, real-time collaboration, shared connections относятся не к Lite.
- [Lite release notes 26.2](https://dbeaver.com/dbeaver-lite-release-notes/) — релиз 7 сентября 2026; новые опции/поведение. Это датированная контрольная точка, не гарантия будущих функций.
- [Data Editor](https://dbeaver.com/docs/dbeaver/Data-Editor/) — табличный режим, metadata/action roles, navigation, value views.
- [SQL Editor](https://dbeaver.com/docs/dbeaver/SQL-Editor/) — autocomplete, formatter, plans, scripts, parameters.
- [Visual Query Builder](https://dbeaver.com/docs/dbeaver/Visual-Query-Builder/) — визуальная выборка с генерацией SQL; complex SQL round-trip нельзя обещать по одному короткому примеру.
- [ER Diagrams](https://dbeaver.com/docs/dbeaver/ER-Diagrams/) — read-only graph, scope/metadata.
- [Managing charts](https://dbeaver.com/docs/dbeaver/Managing-Charts/) — аналитические графики.
- [Query manager](https://dbeaver.com/docs/dbeaver/Query-Manager/) — поиск/срок хранения истории, distinct from Undo.
- [Task management](https://dbeaver.com/docs/dbeaver/Task-Management/) — расписания задач в Lite, исследовать позже после защитного storage/scheduler.

### Важная граница

Официальное сравнение **не** подтверждает, что Lite содержит все функции Enterprise, Ultimate или Team. Наша матрица содержит дополнительные «Advanced/Personal+» идеи, но не называет их обязательным Lite parity. Некоторые vendor capabilities зависят от лицензированного драйвера и конкретного engine/version; без матрицы тестов не заявлять «все СУБД». DBeaver Lite коммерческий, DBeaver Community (Apache-2.0) — другой продукт с иными лицензионными правами; коммерческий код и ресурсы не заимствовать.

## UX-референсы: изучаем принципы, не внешний вид

- [Microsoft Fluent 2 — Principles](https://fluent2.microsoft.design/design-principles) — focus, familiarity, inclusive work.
- [Microsoft Fluent 2 — Accessibility](https://fluent2.microsoft.design/accessibility) — structure, focus, navigation and WCAG.
- [Microsoft Fluent 2 — Layout](https://fluent2.microsoft.design/layout) — proximity/spacing/hierarchy.
- [Apple Human Interface Guidelines — Design Principles](https://developer.apple.com/design/human-interface-guidelines/design-principles) — purpose, simplicity, flexibility, agency, craft.
- [VS Code User Interface](https://code.visualstudio.com/docs/editing/getting-started/userinterface) — ubiquitous explorer/tabs/commands/accessibility patterns, not license to copy branded layout.
- [Linear — A calmer interface for a product in motion](https://linear.app/now/behind-the-latest-design-refresh) — lower-contrast navigation, visible priorities, density without noise.
- [Notion — Views, filters, sorts and groups](https://www.notion.com/help/views-filters-and-sorts) — clear table affordances, filters/views/columns and user-friendly labelling.

## Наше независимое проектирование

1. Перевести наблюдение в собственный **user job**: например «открыть внешнюю БД и за 30 секунд найти нужную таблицу», а не «повторить окно DBeaver».
2. Указать существующее SmartSQL решение и **критерий исхода**: где данные, права, источник, операция, ошибки, восстановление.
3. Отдельно нарисовать свои wireframes в UX_DESIGN, сохранить зелёный SmartSQL бренд, типографику и tokens.
4. Реализовать C# через текущие контракты и официальные .NET drivers, лицензии библиотек перепроверять.
5. Проверять user testing и matrix feature × provider × version × OS, а не сравнение пикселей с чужим продуктом.

Детальные вопросы авторского права, лицензирования и торговых марок перед коммерческим выпуском подлежат отдельному юридическому review; этот документ — техническая политика добросовестной независимой реализации, не юридическое заключение.
