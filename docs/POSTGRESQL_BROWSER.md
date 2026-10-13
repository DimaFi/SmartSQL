# PostgreSQL: просмотр существующих баз

Откройте **База → Подключения к СУБД…**, укажите сервер, порт, базу, пользователя и режим TLS. Сохранение профиля необязательно. Пароль вводится для текущего подключения и не сохраняется; профили находятся в игнорируемом `data/local/connections.json` на D:.

Отдельное окно показывает доступные схемы, таблицы и представления. **Данные** — страницы по 100 записей, **Структура** — исходные типы, nullable и порядок полей составного первичного ключа. NULL показан приглушённым курсивом; пустая строка и текст `∅` сохраняют отдельное значение. Загрузка отменяется кнопкой или закрытием окна.

## Границы этапа

- Внешняя PostgreSQL-база доступна для чтения, без метаданных SmartSQL. Локальный SQLite-редактор остаётся отдельным рабочим процессом.
- Значения читаются как текст PostgreSQL без преобразования в `decimal`/.NET-типы. Большие числа, массивы, JSON, enum и binary сохраняют представление сервера. Timestamp with time zone показан в UTC.
- Каждая страница — отдельный снимок. Изменения базы могут сдвигать записи между страницами. Отсутствие подтверждённого ключа, наследование, RLS и прочие ограничения показываются явно.
- Тайм-аут подключения/команды — 15 секунд. Каталог ограничен 10 000 объектов, API — 500 строк. Значение свыше 1 млн символов или страница свыше 16 млн отклоняются явно без обрезания.
- Редактирование, произвольный SQL и обмен внешними данными не входят в эту первую вертикаль.
- Профили разных процессов пока сохраняются по правилу последней записи. Повреждённый JSON не сбрасывается автоматически; после ошибки загрузки сохранение/удаление блокируются.

## Интеграционные тесты

Обычный `scripts/Test.ps1` явно пропускает шесть PostgreSQL-фактов, пока не включено тестовое окружение. Они обращаются только к `127.0.0.1:15432`, базе `smartsql_browser_test`, пользователю `smartsql_browser_reader`. Фикстуры содержат синтетические данные; используйте отдельный новый кластер, а не рабочую базу.

```powershell
. ./scripts/Initialize-Workspace.ps1 -ForTests
$pgTestBin = 'C:\Program Files\PostgreSQL\17\bin'
$pgTestData = 'D:\Code\SmartSQL\.workspace\postgresql-tests'
# initdb применяется только к новому пустому каталогу.
& "$pgTestBin\initdb.exe" -D $pgTestData -U smartsql_test_admin --auth-local=trust --auth-host=trust --encoding=UTF8 --locale=C
& "$pgTestBin\pg_ctl.exe" -D $pgTestData -l "$pgTestData\server.log" -o '-p 15432 -h 127.0.0.1' -w start
& "$pgTestBin\psql.exe" -h 127.0.0.1 -p 15432 -U smartsql_test_admin -d postgres -v ON_ERROR_STOP=1 -f tests/SmartSQL.Infrastructure.Tests/Fixtures/PostgresCreate.sql
& "$pgTestBin\psql.exe" -h 127.0.0.1 -p 15432 -U smartsql_test_admin -d smartsql_browser_test -v ON_ERROR_STOP=1 -f tests/SmartSQL.Infrastructure.Tests/Fixtures/PostgresBrowser.sql
$env:SMARTSQL_POSTGRES_TEST_PORT = '15432'
./scripts/Test.ps1 -Offline
& "$pgTestBin\pg_ctl.exe" -D $pgTestData -w stop
Remove-Item Env:SMARTSQL_POSTGRES_TEST_PORT
```

Trust используется только в одноразовом loopback-кластере. Существующий сервис PostgreSQL не меняется; установленный runtime остаётся на своём месте, все данные/журналы/результаты на D: и вне Git. Проверяются права, составной ключ, страницы/граница, 60-значное число, NULL/пустота, native-типы, кавычки/Unicode, режим чтения, отмена/освобождение и слишком большие значения.
