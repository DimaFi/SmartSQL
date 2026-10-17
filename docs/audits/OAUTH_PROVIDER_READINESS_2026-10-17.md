# OAuth/backup readiness: Google Drive и Яндекс Диск, 17.10.2026

> **Интеграционная пометка 17.10.2026:** reference/proposal, не новая принятая архитектура и не реализованные функции. Использование, перепроверка источников и ограничения — [реестр интеграции](BRANCH_INTEGRATION_2026-10-17.md). WORK_PLAN / DECISIONS остаются источниками текущего порядка и решений.

> **Independent official-doc verification**, без реальной регистрации OAuth-клиентов, без пользовательских токенов, без запросов к чужим данным. Вердикты: **CONFIRMED_FROM_DOCS**, **PROPOSED**, **UNVERIFIED_LIVE**. Это дополнительное исследование к M06/EXT-OAUTH; внедрение и требования DEC-KEYS вне scope.

## 1. Текущие факты и критический вопрос Яндекса

| Тема | Статус | Основание |
| --- | --- | --- |
| Google Desktop app → системный браузер + loopback redirect, PKCE S256 | **CONFIRMED_FROM_DOCS** | [Google Native Apps](https://developers.google.com/identity/protocols/oauth2/native-app) |
| Google Desktop apps являются public clients; зашитый client secret нельзя считать защищённым | **CONFIRMED_FROM_DOCS** | Google Native Apps, раздел Credentials |
| Google drive.file scope — non-sensitive, app-owned/opened файлы, не весь Drive | **CONFIRMED_FROM_DOCS** | [Drive scopes](https://developers.google.com/workspace/drive/api/guides/api-specific-auth) |
| Google appDataFolder скрытый и удаляется при удалении приложения из My Drive | **CONFIRMED_FROM_DOCS** | [Drive appdata](https://developers.google.com/workspace/drive/api/guides/appdata) |
| Google resumable 308/status Range/expired 404; sha256Checksum may be available | **CONFIRMED_FROM_DOCS** | [upload](https://developers.google.com/workspace/drive/api/guides/manage-uploads), [files resource](https://developers.google.com/workspace/drive/api/reference/rest/v3/files) |
| Yandex Disk OAuth app-specific token and cloud_api:disk.app_folder scope | **CONFIRMED_FROM_DOCS** | [Disk API quickstart](https://yandex.com/dev/disk-api/doc/en/concepts/quickstart) |
| Yandex OAuth authorization code permits code_challenge/code_challenge_method/state | **CONFIRMED_FROM_DOCS** | [Yandex auth code](https://yandex.ru/dev/id/doc/ru/codes/code-url) |
| Yandex refresh_token API и поле refresh_token в ответе (rotation не доказана) | **CONFIRMED_FROM_DOCS** | [Yandex refresh](https://yandex.ru/dev/id/doc/ru/tokens/refresh-client) |
| Yandex desktop app may safely refresh without embedded client_secret | **UNVERIFIED_LIVE — MUST RESOLVE** | Refresh instructions refer to sending client ID and password, while parameter tables mark ID/secret optional. **Do not infer a safe distributed-desktop client from the optional syntax alone.** |
| Yandex app:/ list/upload/download/delete + old copies after reinstall under restricted scope | **UNVERIFIED_LIVE** | Scope named in official docs; actual complete workflow not tested |
| Yandex upload GET upload URL, PUT, overwrite=false and asynchronous/pending lifecycle | **RECHECK_REQUIRED**: исходная upload-страница не открылась при интеграции; two-step/overwrite и completion перепроверить до реализации | [Yandex upload](https://yandex.com/dev/disk-api/doc/en/reference/upload) |
| Google/Yandex downloadable backup safely restorable on *new PC* | **UNVERIFIED_LIVE** | Requires new PC/profile + remote listing + hash + full SQLite restore, not only upload |

## 2. Яндекс: отдельный blocking acceptance для native client

Документация регистрации различает приложения «для авторизации пользователей» и «для доступа к API», причём последнее описано как работающее с данными разработчика, не чужих пользователей. См. [регистрацию](https://yandex.ru/dev/id/doc/ru/register-client). **Выбрать/проверить пользовательский тип** при регистрации. Поля redirect URI, тестовые users/permissions и продуктовую модерацию проверять на фактической консоли — не обещать автоматически поддерживаемый localhost redirect для всех типов.

[Обновление токена](https://yandex.ru/dev/id/doc/ru/tokens/refresh-client) показывает формулировку о пароле приложения, при этом client_id/client_secret находятся среди optional request parameters. Это создаёт реальную неопределённость для обычного Windows distributed app:
- **Нельзя** скрыть общий долгоживущий секрет в EXE и заявлять безопасность.
- **Нельзя** самостоятельно заменить refresh-token flow на Google protocol, не проверив Яндекс.
- **Нужно** на добровольном тестовом аккаунте и зарегистрированном desktop/public client воспроизвести authorization code + PKCE, refresh **без секрета**, revoke, reinstall и app-folder rights.
- Если такой flow не поддержан официально и в протокольном test: ограничить поддержку, согласовать документированную альтернативу, либо отложить Яндекс cloud до явного решения владельца. Не блокировать независимые M05 local snapshots.

## 3. Google native flow: проверяемая минимальная конфигурация

1. В своём Google Cloud project включить Drive API; настроить OAuth consent, тип audience/test users и App Verification по фактическому статусу scope.
2. Зарегистрировать OAuth Client **Desktop app**; отметить публичность client secret; system browser, loopback 127.0.0.1 random port, PKCE S256 и уникальный state. Нельзя Embedded WebView, OOB redirect, захардкоженный порт без проверки.
3. Использовать **drive.file** и пользовательски видимые **app-created versions**, не полный drive scope; отдельные новые UUID names, no overwrite, не общий latest.db. Исключение: существующие файлы вне app scope требуют реального user grant (Picker/API), не текстового пути.
4. Пройти upload creation/resumable, после ответа и отдельного read verify size/sha256Checksum если поле есть. Если strong hash отсутствует, скачать и сравнить SHA-256.
5. Имитация сети: 308+Range, 503/retry, expired 404, cancelled browser, revoked account; повторный вход и restore old objects после clean reinstall.
6. Только после проверки согласовать public distribution (consent test/publishing, privacy/security, разные Google Workspace policies). Здесь нет ни client ID, ни зарегистрированного проекта.

**Важно:** Google public Desktop app документирован и имеет ясный путь реализации, но работающий SDK/библиотека/.NET package и лицензии не были в этом аудите протестированы.

## 4. Яндекс: рекомендуемый scope реального protocol spike

1. Зарегистрировать приложение, предназначенное для авторизации **пользователей**, выбрать платформу/redirect и app-folder scope. Проверить что разрешено в UI кабинета, не использовать отладочный verification_code как производственный native redirect.
2. Test user входит через системный браузер: authorization code, PKCE S256, state, timeout/cancel, invalid verifier.
3. Обмен кода, refresh/rotation, logout/revoke и новая авторизация после restart: зафиксировать наличие/отсутствие требования client_secret у **этого конкретного типа приложения**, не публикуя настоящие токены.
4. Ровно с scope cloud_api:disk.app_folder: create/list/upload/read/download/delete versioned object в namespace app:/, нет возможности читать произвольный disk:/.
5. Верифицировать upload URL lifetime и поведение 202/pending, overwrite=false collision, 507/quota, 401/403, malformed path и доступный checksum. Если SHA-256 не даётся, контрольное скачивание.
6. Clean reinstall/another device: тот же account+app ID, нет локального каталога/старых токенов. Remote enumeration, скачать проверенные копии, открыть recovered SQLite. Провести wrong account A/B тест.
7. Только после этого формализовать support guarantee и реальный product authorization UX.

## 5. Контракт учётных записей и секретов (proposal)

- Persistent app profile хранит только providerId, anonymized/stable account handle, granted scope labels, connection status и ссылку на OS-secure credential slot. Сам OAuth token, refresh_token, PKCE verifier, upload URL — **не** в общем JSON, Гите, bootstrap, telemetry, crash logs, cloud backup, ML corpus.
- Login and account switch никогда не автоматически меняют destination чужого snapshot; при token revoked статус AUTH_REQUIRED и local outbox intact. Перенос root между ПК не переносит авторизацию.
- OAuth callback state/nonce/pkce secret — одноразовые и bounded; внешние redirect URI не разрешают отправить Authorization на чужой host, TLS validation не отключать. Никакого хранения паролей Google/Яндекса.
- Для корпоративного PostgreSQL наличие прав SELECT не означает права на личную cloud backup; явный consent scope данного источника до first upload.

## 6. Граница безопасности, тесты и критерий готовности

**O01–O10 acceptance:** native PKCE/state; cancel; no embedded password; scope restricted; refresh/revoke/re-auth; expired/pending upload URL; exact remote hash vs download; wrong account; reinstall restore; no tokens in exported metadata/logs.

Отчёт Codex должен включить: date/URI official sources, exact client type/scope, synthetic payload SHA only, ошибку и ответ сервера с **редактированными** credentials, result pass/fail/unknown. **Никакая** принадлежность Google Drive connector в чате не заменяет OAuth-клиент SmartSQL.

**На дату проверки 17.10.2026:** Google flow документально определён; Яндекс **native secretless refresh + scope-complete restore** должны быть доказаны до реализации обещанной автоматической фоновой авторизации. Это не новая архитектура, а устранение двух сомнительных предпосылок.

Интеграционное уточнение: optional client_id/client_secret в теле refresh-запроса могут означать передачу тех же credentials через Basic header; это не доказательство отсутствия client authentication. Наличие refresh_token в response не доказывает rotation. Upload URL остаётся исходной ссылкой/provenance, не повторно подтверждённым endpoint-контрактом. См. интеграционный реестр.
