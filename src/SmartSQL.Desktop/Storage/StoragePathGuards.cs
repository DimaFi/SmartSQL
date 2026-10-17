namespace SmartSQL.Desktop.Storage;

internal static class StoragePathGuards
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string RequireDirectoryPath(string? value, string label, bool mustExist, bool developmentMode)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new StorageRootSelectionException($"{label}: требуется непустой абсолютный путь.");
        string path;
        try
        {
            if (!Path.IsPathFullyQualified(value) || IsWindowsNetworkOrDevicePath(value))
                throw new StorageRootSelectionException($"{label}: поддерживается только абсолютный локальный путь.");
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new StorageRootSelectionException($"{label}: путь некорректен.", exception);
        }

        var volumeRoot = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path) ?? string.Empty);
        if (string.IsNullOrEmpty(volumeRoot) || string.Equals(path, volumeRoot, PathComparison))
            throw new StorageRootSelectionException($"{label}: корень тома использовать нельзя.");
        if (developmentMode && OperatingSystem.IsWindows() &&
            !string.Equals(Path.GetPathRoot(path), @"D:\", StringComparison.OrdinalIgnoreCase))
            throw new StorageRootSelectionException($"{label}: development-данные разрешены только на D:.");

        RequireReadyLocalVolume(path, label);
        ValidateExistingChain(path, label);
        if (mustExist && !DirectoryExists(path, label))
            throw new StorageRootSelectionException($"{label}: каталог не существует.");
        return path;
    }

    public static void RequireNoAppContainment(string dataDirectory, string executableDirectory, string label)
    {
        if (Contains(dataDirectory, executableDirectory) || Contains(executableDirectory, dataDirectory))
            throw new StorageRootSelectionException(
                $"{label}: хранилище и каталог программы не могут содержать друг друга.");
    }

    public static bool HasAnyEntry(string path, string label)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new StorageRootSelectionException($"{label}: reparse point не поддерживается.");
            if ((attributes & FileAttributes.Directory) == 0)
                return true;
            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            return entries.MoveNext();
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (StorageRootSelectionException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StorageRootSelectionException($"{label}: содержимое каталога недоступно.", exception);
        }
    }

    public static bool RequireOrdinaryFileIfPresent(string path, string label)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new StorageRootSelectionException(
                    $"{label}: требуется обычный файл, reparse point или каталог не поддерживается.");
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (StorageRootSelectionException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StorageRootSelectionException($"{label}: файл невозможно проверить.", exception);
        }
    }

    public static void ProbeWritableDirectory(string path, string label, bool create)
    {
        try
        {
            if (create)
                Directory.CreateDirectory(path);
            else if (!DirectoryExists(path, label))
                throw new StorageRootSelectionException($"{label}: каталог не существует.");
            var probe = Path.Combine(path, $".smartsql-write-check-{Guid.NewGuid():N}.tmp");
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 1, FileOptions.DeleteOnClose | FileOptions.WriteThrough);
            stream.WriteByte(0);
            stream.Flush(flushToDisk: true);
        }
        catch (StorageRootSelectionException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StorageRootSelectionException($"{label}: каталог недоступен для записи.", exception);
        }
    }

    public static bool DirectoryExists(string path, string label)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new StorageRootSelectionException($"{label}: reparse point не поддерживается.");
            if ((attributes & FileAttributes.Directory) == 0)
                throw new StorageRootSelectionException($"{label}: ожидается каталог, найден файл.");
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (StorageRootSelectionException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StorageRootSelectionException($"{label}: каталог невозможно проверить.", exception);
        }
    }

    private static void ValidateExistingChain(string path, string label)
    {
        var current = path;
        var foundExistingDirectory = false;
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new StorageRootSelectionException(
                        $"{label}: reparse point не поддерживается для product storage.");
                if ((attributes & FileAttributes.Directory) == 0)
                    throw new StorageRootSelectionException($"{label}: компонент пути является файлом.");
                foundExistingDirectory = true;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (StorageRootSelectionException) { throw; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new StorageRootSelectionException($"{label}: физический путь невозможно проверить.", exception);
            }
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, PathComparison))
                break;
            current = parent;
        }
        if (!foundExistingDirectory)
            throw new StorageRootSelectionException($"{label}: не найден существующий родительский каталог.");
    }

    private static void RequireReadyLocalVolume(string path, string label)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                throw new StorageRootSelectionException($"{label}: том определить не удалось.");
            var drive = new DriveInfo(root);
            if (!drive.IsReady)
                throw new StorageRootSelectionException($"{label}: том недоступен.");
            if (drive.DriveType == DriveType.Network)
                throw new StorageRootSelectionException($"{label}: сетевой том не поддерживается.");
        }
        catch (StorageRootSelectionException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            throw new StorageRootSelectionException($"{label}: локальный том невозможно проверить.", exception);
        }
    }

    private static bool Contains(string candidate, string parent)
    {
        var child = Path.TrimEndingDirectorySeparator(candidate);
        var container = Path.TrimEndingDirectorySeparator(parent);
        return string.Equals(child, container, PathComparison) ||
               child.StartsWith(container + Path.DirectorySeparatorChar, PathComparison) ||
               child.StartsWith(container + Path.AltDirectorySeparatorChar, PathComparison);
    }

    private static bool IsWindowsNetworkOrDevicePath(string path) => OperatingSystem.IsWindows() &&
        (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal));
}
