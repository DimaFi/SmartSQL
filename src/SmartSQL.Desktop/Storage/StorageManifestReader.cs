using System.Text;

namespace SmartSQL.Desktop.Storage;

/// <summary>
/// Bounded read-only adapter for the three D056 JSON manifests. A missing optional
/// file is distinct from an unreadable, oversized or malformed one. The caller must
/// separately validate filesystem identity, parent reparse points and write safety.
/// </summary>
public static class StorageManifestReader
{
    public const int MaximumManifestBytes = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Reads an optional manifest without creating any path or default storage.
    /// The exact filename is enforced to avoid treating an arbitrary user file as metadata.
    /// </summary>
    public static StorageManifestReadResult ReadOptional(string absoluteFilePath, StorageManifestKind kind)
    {
        string filePath = ValidateManifestPath(absoluteFilePath, kind);
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: MaximumManifestBytes, FileOptions.SequentialScan);

            // Never allocate according to untrusted file length.
            if (stream.Length > MaximumManifestBytes)
                throw new StorageRootSelectionException("Файл конфигурации хранилища слишком велик.");

            var buffer = new byte[MaximumManifestBytes + 1];
            var total = 0;
            int count;
            while ((count = stream.Read(buffer, total, buffer.Length - total)) > 0)
            {
                total += count;
                if (total > MaximumManifestBytes)
                    throw new StorageRootSelectionException("Файл конфигурации хранилища слишком велик.");
            }

            // The policy parser requires a JSON object. Reject an empty file before
            // treating it as a possible bootstrap.
            if (total == 0)
                throw new StorageRootSelectionException("Файл конфигурации хранилища пуст.");

            // A strict UTF-8 contract avoids silent substitution of damaged bytes.
            if (total >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                throw new StorageRootSelectionException("Файл конфигурации должен быть UTF-8 без BOM.");

            try
            {
                return new StorageManifestReadResult(true, StrictUtf8.GetString(buffer, 0, total));
            }
            catch (DecoderFallbackException exception)
            {
                throw new StorageRootSelectionException(
                    "Файл конфигурации содержит некорректные байты UTF-8.", exception);
            }
        }
        catch (FileNotFoundException)
        {
            return new StorageManifestReadResult(false, null);
        }
        catch (DirectoryNotFoundException)
        {
            return new StorageManifestReadResult(false, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Do not downgrade any I/O or permission error into "first use".
            throw new StorageRootSelectionException(
                "Не удалось прочитать конфигурацию хранилища. Путь и права доступа требуют проверки.",
                exception);
        }
    }

    public static string ReadRequired(string absoluteFilePath, StorageManifestKind kind)
    {
        var result = ReadOptional(absoluteFilePath, kind);
        if (!result.IsPresent)
            throw new StorageRootSelectionException("Обязательный файл идентичности хранилища отсутствует.");
        return result.Json!;
    }

    private static string ValidateManifestPath(string? path, StorageManifestKind kind)
    {
        var expectedName = kind switch
        {
            StorageManifestKind.Portable => "smartsql.portable.json",
            StorageManifestKind.InstalledBootstrap => "storage.json",
            StorageManifestKind.RootIdentity => "storage-root.json",
            _ => throw new StorageRootSelectionException("Неизвестный тип файла хранилища.")
        };

        if (string.IsNullOrWhiteSpace(path))
            throw new StorageRootSelectionException("Не задан абсолютный путь к конфигурации хранилища.");

        try
        {
            // Do not let a caller turn the read-only preflight into an SMB/device
            // access. Mapped drives and reparse points require the later physical
            // filesystem stage, but Windows UNC and device namespaces are safely
            // recognizable before opening a handle.
            if (OperatingSystem.IsWindows() &&
                (path.StartsWith(@"\\", StringComparison.Ordinal) ||
                 path.StartsWith("//", StringComparison.Ordinal)))
                throw new StorageRootSelectionException(
                    "Путь к конфигурации хранилища должен быть локальным и не может использовать UNC/device namespace.");
            if (!Path.IsPathFullyQualified(path))
                throw new StorageRootSelectionException("Путь к конфигурации хранилища должен быть абсолютным.");
            var fullPath = Path.GetFullPath(path);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(Path.GetFileName(fullPath), expectedName, comparison))
                throw new StorageRootSelectionException(
                    $"Ожидается файл {expectedName}, другой файл конфигурации не принимается.");
            return fullPath;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new StorageRootSelectionException("Недопустимый путь к файлу конфигурации.", exception);
        }
    }
}

public enum StorageManifestKind
{
    Portable,
    InstalledBootstrap,
    RootIdentity
}

public readonly record struct StorageManifestReadResult(bool IsPresent, string? Json);
