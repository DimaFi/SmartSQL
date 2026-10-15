using System.Globalization;
using Microsoft.Data.Sqlite;
using SmartSQL.Application;

namespace SmartSQL.Infrastructure;

public sealed partial class NativeSqliteDatabaseBrowser
{
    private static string ValidateBackupDirectory(string sourcePath, string backupDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        if (!Path.IsPathFullyQualified(sourcePath) || !Path.IsPathFullyQualified(backupDirectory))
            throw new ArgumentException("SQLite-файл и каталог копий должны иметь абсолютные пути.");
        var source = Path.GetFullPath(sourcePath);
        var backup = Path.GetFullPath(backupDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!OperatingSystem.IsWindows() || !string.Equals(Path.GetPathRoot(backup), "D:\\", StringComparison.OrdinalIgnoreCase) || backup.Equals("D:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Копии этого рабочего окружения сохраняются только в отдельном именованном каталоге на D:.", nameof(backupDirectory));
        foreach (var protectedPath in new[] { source, source + "-wal", source + "-shm", source + "-journal" })
            if (backup.Equals(protectedPath, StringComparison.OrdinalIgnoreCase) || backup.StartsWith(protectedPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Каталог копий пересекается с SQLite-файлом или его служебными файлами.", nameof(backupDirectory));
        RejectReparseAncestors(source);
        RejectReparseAncestors(backup);
        return backup;
    }

    private static void RejectReparseAncestors(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Редактирование и резервное копирование через ссылки/reparse points не поддерживаются.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private string CreateNativeEditBackup(Context writer, string expectedFingerprint)
    {
        string? temporary = null;
        var ownsTemporary = false;
        try
        {
            writer.Token.ThrowIfCancellationRequested();
            var directory = ValidateBackupDirectory(_path, _backupDirectory!);
            Directory.CreateDirectory(directory);
            RejectReparseAncestors(directory);
            // Backup may adjust the destination schema cookie; compare the actual schema separately.
            if (ReadSchema(writer).Fingerprint != expectedFingerprint)
                throw new DatabaseBrowserException("edit_schema_changed", "Структура изменилась перед резервным копированием.");
            var expectedSchema = ReadSchema(writer, includeVersion: false).Fingerprint;
            long userVersion;
            using (var command = writer.Command("PRAGMA main.user_version"))
                userVersion = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            var name = "native-sqlite-before-update-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N") + ".db";
            var destinationPath = Path.Combine(directory, name);
            temporary = Path.Combine(directory, "." + name + ".tmp");
            using (var reservation = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                ownsTemporary = true;
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 3
            }.ToString()))
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = temporary, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 3
            }.ToString()))
            {
                source.Open();
                destination.Open();
                source.EnableExtensions(false);
                destination.EnableExtensions(false);
                using var interrupt = writer.Token.Register(() =>
                {
                    SQLitePCL.raw.sqlite3_interrupt(source.Handle);
                    SQLitePCL.raw.sqlite3_interrupt(destination.Handle);
                });
                using (var settings = source.CreateCommand())
                {
                    settings.CommandTimeout = 3;
                    settings.CommandText = "PRAGMA query_only=ON; PRAGMA trusted_schema=OFF; PRAGMA temp_store=MEMORY";
                    settings.ExecuteNonQuery();
                }
                using (var settings = destination.CreateCommand())
                {
                    settings.CommandTimeout = 3;
                    settings.CommandText = "PRAGMA trusted_schema=OFF; PRAGMA temp_store=MEMORY; PRAGMA synchronous=FULL";
                    settings.ExecuteNonQuery();
                }
                // The writer holds BEGIN IMMEDIATE but has not changed data. This separate source
                // can read the committed pre-write state, including WAL, without SQLITE_LOCKED.
                using (var backup = SQLitePCL.raw.sqlite3_backup_init(destination.Handle, "main", source.Handle, "main"))
                {
                    if (backup is null || backup.IsInvalid)
                        throw new DatabaseBrowserException("edit_backup_failed", "SQLite не смог начать обязательное резервное копирование.");
                    while (true)
                    {
                        writer.Token.ThrowIfCancellationRequested();
                        var status = SQLitePCL.raw.sqlite3_backup_step(backup, 64);
                        if (status == 101) break; // SQLITE_DONE
                        if (status == 0) continue;
                        if (status is 5 or 6) { writer.Token.WaitHandle.WaitOne(10); continue; }
                        writer.Token.ThrowIfCancellationRequested();
                        throw new DatabaseBrowserException("edit_backup_failed", "SQLite не смог завершить обязательное резервное копирование.");
                    }
                    if (SQLitePCL.raw.sqlite3_backup_finish(backup) != 0)
                        throw new DatabaseBrowserException("edit_backup_failed", "SQLite не подтвердил завершение резервной копии.");
                }
                writer.Token.ThrowIfCancellationRequested();
                using var inspection = destination.BeginTransaction(deferred: true);
                var context = new Context(destination, inspection, writer.Token, writer.TextEncoding);
                using (var check = context.Command("PRAGMA main.quick_check"))
                using (var reader = check.ExecuteReader())
                {
                    if (!reader.Read() || reader.GetString(0) != "ok" || reader.Read())
                        throw new DatabaseBrowserException("edit_backup_failed", "Резервная копия не прошла quick_check. Запись не выполнялась.");
                }
                writer.Token.ThrowIfCancellationRequested();
                if (ReadSchema(context, includeVersion: false).Fingerprint != expectedSchema)
                    throw new DatabaseBrowserException("edit_backup_failed", "Структура резервной копии не совпадает с исходной базой.");
                using (var check = context.Command("PRAGMA main.user_version"))
                    if (Convert.ToInt64(check.ExecuteScalar(), CultureInfo.InvariantCulture) != userVersion)
                        throw new DatabaseBrowserException("edit_backup_failed", "Версия исходного файла не сохранилась в резервной копии.");
                inspection.Rollback();
            }
            writer.Token.ThrowIfCancellationRequested();
            RejectReparseAncestors(directory);
            File.Move(temporary, destinationPath, overwrite: false);
            ownsTemporary = false;
            writer.Token.ThrowIfCancellationRequested();
            return destinationPath;
        }
        catch (OperationCanceledException) { throw; }
        catch (DatabaseBrowserException) { throw; }
        catch (Exception)
        {
            writer.Token.ThrowIfCancellationRequested();
            throw new DatabaseBrowserException("edit_backup_failed", "Не удалось создать и проверить обязательную копию на D:. Исходная база не изменена.");
        }
        finally
        {
            if (ownsTemporary && temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
