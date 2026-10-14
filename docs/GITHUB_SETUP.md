# Публикация SmartSQL в GitHub

15 октября 2026 публикация выполнена через консоль: Git отправил полную историю SmartSQL без force, PowerShell API создал [общий план](https://github.com/DimaFi/SmartSQL/issues/1) и 25 задач. [Индекс с реальными ссылками](GITHUB_TASKS.md); номера/маркеры сохранены в `docs/github-issues.json`. Новая авторизация для этого этапа не понадобилась.

## Порядок последующих публикаций

1. Проверить локальный Git status/историю и удалённые ветки. Доступ плагина и обычного Git — разные проверки. `gh` не установлен; для работы достаточно существующих Git и PowerShell API.
2. Использовать сохранённый вход Git под `DimaFi`. Новый вход выполнять только если текущий перестал работать. В этом случае код устройства пользователь подтверждает сам на [GitHub](https://github.com/login/device); предыдущий код не использовать.
3. Проверить ветки, отправить локальные коммиты обычным `git push` без force, сравнить удалённый SHA с локальным.
4. Обновлять существующие Issues по сохранённому номеру/маркеру T-ID. Только отсутствующие задачи создавать заново; сохранять реальные номера и URL. Ручная UI-приёмка остаётся открытой независимо от публикации кода.

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
