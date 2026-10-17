# DEC-KEYS: защита будущих облачных копий и восстановление после потери ПК

> **Интеграционная пометка 17.10.2026:** reference/proposal, не новая принятая архитектура и не реализованные функции. Использование, перепроверка источников и ограничения — [реестр интеграции](BRANCH_INTEGRATION_2026-10-17.md). WORK_PLAN / DECISIONS остаются источниками текущего порядка и решений.

> **Независимое threat-model review. Не принятая архитектура и не реализованное шифрование.** Относится к M05.07/M06/M07; не блокирует безопасную **локальную** M05 snapshot-разработку. Не генерировать настоящие ключи/пароли в задачах Codex или Git. DEC-KEYS требует отдельного решения владельца после проверки библиотек, поддержки целевых ОС и лицензий.

## Угроза и граница обещания

| Противник/отказ | Что можно защитить | Чего этот дизайн не обещает |
| --- | --- | --- |
| Кто-то получил файл на Google/Яндекс | Confidentiality и integrity encrypted payload при правильном AEAD/ключе | Скрыть, что копия существует, её объём/время и аккаунт |
| Изменили manifest, ciphertext или вложенный файл | Detection до восстановления в активный проект | Невозможность отката к **старой валидной** копии без trusted anti-rollback anchor |
| Потерян ПК с локальными OS credentials | Restore при наличии переносимого отдельного ключа/пароля | Восстановить без единственного оставшегося recovery secret |
| Забытый password/recovery code | Предупреждение и проверка восстановления заранее | Магическое восстановление зашифрованных данных |
| Украден OAuth refresh token | Revocation/re-auth, ограниченные provider scopes и обнаружение чужих операций | Защита plaintext внутри уже захваченного запущенного ПК |
| Злоумышленник завладел живой сессией SmartSQL | Ограничить токены/действия, audit/confirmation | Защиту от полного захвата процесса/ввода пользователя |
| Два устройства поочерёдно копируют проект | Отдельные immutable snapshot IDs/generations, учет account+source | Автоматическое слияние SQLite и транзакционная совместная работа |
| Data/manifest corrupted, disk full, interrupted download | Fail closed, новый target, source untouched | Проверенную копию, если provider её удалил или остался один плохой экземпляр |

## Альтернативы (не окончательный выбор)

| Кандидат | Плюсы | Минусы / verification gates |
| --- | --- | --- |
| V1: ключ восстановления как отдельный offline secret; vetted .NET AES-GCM и **проверенное chunk framing** | Windows/.NET built-in AEAD, переносимый секрет; можно сначала сделать понятный restore UX | AES-GCM сам по себе не задаёт безопасный file/container protocol: nonce uniqueness, chunk order, final length, metadata authenticity, KDF и version bounds нужно проектно подтвердить/review. **Нельзя вручную склеить «простое AES-GCM» и объявить security-ready** |
| V2: libsodium secretstream с поддерживаемой .NET обёрткой, offline key | Потоковый authenticated envelope со встроенной проверенной моделью чанков | Проверить active maintenance/license/native packaging для Windows/Linux/macOS, pinned version и libsodium binary distribution; secretstream не решает отдельно recovery key UX |
| V3: зрелая сторонняя backup/encrypted-container библиотека | Может дать уже проверенный протокол хранения и совместимость | Не принимать без аудита формата, maintenance, license, key export, size limits и контрольного восстановления; лишняя dependency без конкретной необходимости запрещена |

**Рекомендация для уточнения DEC-KEYS:** first-user flow должен ясно выдавать/импортировать **переносимый recovery secret отдельно от OS token store**; нельзя сохранять его в том же облаке рядом с копиями без дополнительной независимой защиты. Реальная схема защиты/парольная KDF, security review и выбор библиотеки — отдельная gate перед cloud encryption claims. Offline user may initially keep only verified local snapshots (не выдавать их за encrypted).

## Минимальные свойства будущего envelope

- Versioned magic, strict bounded lengths, encryption scheme identifier, KDF parameters bounded against resource exhaustion, opaque keyId, nonce/stream header; source kind/format and ciphertext metadata **authenticity** обеспечены до restore.
- Explicit association payload and manifest: snapshotId, source scope, bytes count/hash and version должны аутентифицироваться; нельзя отдельно «подменить JSON manifest» и пройти check с тем же ciphertext. Никаких local absolute paths/DB credentials в удалённом manifest.
- Cryptographic tag/final-chunk marker/completeness checked before opening a new target. A truncated sequence or wrong key fails without publishing output. Duplicate nonce/stream header with same key must be prevented/tested in chosen approved construction.
- Stable salt/key derivation design vetted; offline recovery key generation/rotation and printed/exported instructions tested; clear distinction changing login password vs re-encrypting existing copies.
- Future format changes: reject unknown versions or flags before allocating large memory; never silently downgrade to plaintext.
- No automatic deletion old encrypted archives during rotation; verify at least one restore path with both old and new keys before retention.
- OS-secure token container used for OAuth (credential cache); it is NOT the only decryption route for files on remote provider.

## Decision gates перед upload sensitive data

1. **DEC-KEYS-A:** разрешено ли в первом облачном релизе хранить user-selected database snapshots *без клиентского шифрования* с ясным раскрытием? Если нет, cloud upload ждёт security verified encryption, но M05 local snapshot не блокируется.
2. **DEC-KEYS-B:** UX offline recovery: отдельный random recovery key или passphrase с проверенной KDF; какой канал выдачи/проверки и невозможность восстановления после потери **всех** секретов.
3. **DEC-KEYS-C:** vetted library, licensing, availability on .NET 10/Avalonia platforms, versioned envelope, algorithm tests, unit + adversarial + clean-PC recovery.
4. **DEC-KEYS-D:** backup integrity vs authenticity/rollback threat, независимый trusted anchor для защиты от целенаправленного отката; если такого anchor нет, честное ограничение.

## Будущие security acceptance cases S01–S12

| ID | Проверка | Ожидаемое |
| --- | --- | --- |
| S01 | Plaintext/sensitive DB policy not accepted | No unconsented cloud upload |
| S02 | Different PC, empty OS profile, only cloud copy + offline recovery secret | Restore into new target works |
| S03 | Wrong/missing key | Clear unrecoverable/auth failure, source untouched |
| S04 | Payload one-byte tamper / truncated final chunk | Reject with no partial published DB |
| S05 | Manifest sourceKind/size/snapshotId substituted | Authenticated metadata rejects |
| S06 | Oversized declared header/KDF rounds/chunk | Bounded reject, no OOM/freeze |
| S07 | Reused same key/nonce input / concurrent encrypt | Collision prevention/invariant test |
| S08 | Old valid snapshot replay | Warn trusted-anchor limitation; never present as newest simply by timestamp |
| S09 | Key rotated; old remote backup retained | Restore both with required key material |
| S10 | OAuth token revoked, recovery secret available | New login, key kept separate; no data-loss by token invalidation |
| S11 | Account A/B swapped, wrong user | Cannot reuse wrong account object as correct snapshot |
| S12 | Secret scan source/logs/config/output | No tokens, recovery keys or identifiable DB data |

Официальные технологические ориентиры (проверено чтением страниц 17.10.2026; **не** означает performance/security-аудит конкретной реализации): [.NET 10 AesGcm](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm?view=net-10.0), [libsodium secretstream](https://doc.libsodium.org/secret-key_cryptography/secretstream), [libsodium password hashing](https://doc.libsodium.org/password_hashing/default_phf), [OAuth for Native Apps RFC 8252](https://www.rfc-editor.org/rfc/rfc8252).

**Codex action:** оформить traceable DEC-KEYS proposal + тестовый план из синтетических данных, проверить поддержку/лицензии выбранной библиотеки; **не** включать encrypted feature flag или cloud upload без принятого контракта/интеграционных тестов.
