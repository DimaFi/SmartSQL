# Публикация SmartSQL в GitHub

Пользователь отложил публикацию: сейчас работаем локально. Эти шаги выполняются после нового запроса попробовать вход/публикацию. Задачи пока подготовлены в `docs/github-issues.json`, а не созданы в GitHub.

## Что ещё требуется

1. Доступ GitHub-плагина к `DimaFi/SmartSQL` пользователь добавил. Проверить чтение и запись после восстановления сервиса; при работе с моделью отдельно проверить доступ к `DimaFi/SmartSQL-ML`.
2. Войти обычным Git под `DimaFi`. Доступ плагина не авторизует Git Credential Manager автоматически. Вход по коду устройства пользователь подтверждает сам на [GitHub](https://github.com/login/device); для новой попытки нужен свежий код, а не код предыдущего сеанса.
3. Проверить ветки, отправить локальные коммиты обычным `git push` без force, сравнить удалённый SHA с локальным.
4. Найти/создать общий план и T01–T25 без дубликатов; сохранить реальные номера и URL в документации и manifest. Ручная UI-приёмка остаётся открытой независимо от публикации кода.

## Вход без изменения глобальных настроек

В рабочем сеансе Codex стандартный Windows credential store не был доступен. [Git Credential Manager](https://github.com/git-ecosystem/git-credential-manager/blob/main/docs/credstores.md#dpapi-protected-files) поддерживает зашифрованное DPAPI-хранилище. Параметры задаются только текущему процессу; хранилище остаётся в игнорируемой папке на D:.

```powershell
Set-Location -LiteralPath D:\Code\SmartSQL
. ./scripts/Initialize-Workspace.ps1 | Out-Null
$env:GCM_CREDENTIAL_STORE = 'dpapi'
$env:GCM_DPAPI_STORE_PATH = 'D:\Code\SmartSQL\.workspace\credentials\gcm'
git credential-manager github login --username DimaFi --device --no-ui
```

После успешного входа в том же сеансе проверить `git push --dry-run origin main:main`, затем обычный push и `git ls-remote origin refs/heads/main`. Для сравнения локального коммита использовать `git rev-parse HEAD`. Не отправлять пароли/токены в чат, не добавлять хранилище авторизации в Git и не печатать вывод `git credential fill`.

У модели отдельный Git-репозиторий. Её source/config/data/scripts/cache/results и проектное хранилище авторизации должны оставаться внутри `D:\Code\SmartSQL-model`; публикация модельной истории выполняется из её папки и по отдельному подтверждённому состоянию. Модель пока не интегрируется в Desktop.
