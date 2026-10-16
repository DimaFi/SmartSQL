using System.Text.Json;
using Avalonia.Styling;

namespace SmartSQL.Desktop.Presentation;

public enum ApplicationTheme
{
    Gray,
    Light
}

/// <summary>Local presentation preference, independent of database mode, sessions and permissions.</summary>
public sealed class ThemePreferences
{
    public const int MaxFileBytes = 1024;
    private const string Format = "SmartSQL.ThemePreferences.v1";
    private readonly object _gate = new();

    public ThemePreferences(string absoluteFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);
        if (!Path.IsPathFullyQualified(absoluteFilePath))
            throw new ArgumentException("Путь к настройкам темы должен быть абсолютным.", nameof(absoluteFilePath));
        FilePath = Path.GetFullPath(absoluteFilePath);
        if (string.IsNullOrEmpty(Path.GetFileName(FilePath)))
            throw new ArgumentException("Укажите файл настроек темы.", nameof(absoluteFilePath));
        if (OperatingSystem.IsWindows() && !string.Equals(Path.GetPathRoot(FilePath), "D:\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Настройки темы этого рабочего окружения сохраняются на D:.", nameof(absoluteFilePath));
    }

    public string FilePath { get; }

    public static ThemePreferences CreateDefault() => new(Path.Combine(WorkspacePaths.DataDirectory, "theme-preferences.json"));

    /// <summary>Missing means Gray. Invalid existing preferences are reported, never reset on load.</summary>
    public ApplicationTheme Load()
    {
        lock (_gate)
        {
            FileStream stream;
            try { stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
            catch (FileNotFoundException) { return ApplicationTheme.Gray; }
            catch (DirectoryNotFoundException) { return ApplicationTheme.Gray; }
            using (stream)
            {
                if (stream.Length > MaxFileBytes)
                    throw new FormatException("Файл настроек темы превышает 1 КБ.");
                using var content = new MemoryStream();
                var buffer = new byte[MaxFileBytes + 1];
                int count;
                while ((count = stream.Read(buffer)) > 0)
                {
                    if (content.Length + count > MaxFileBytes)
                        throw new FormatException("Файл настроек темы превышает 1 КБ.");
                    content.Write(buffer, 0, count);
                }
                return Read(content.ToArray());
            }
        }
    }

    /// <summary>Explicit user selection replaces the preference atomically, including a damaged old setting.</summary>
    public void Save(ApplicationTheme theme)
    {
        Validate(theme);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { format = Format, theme = theme.ToString() });
        lock (_gate)
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
            var ownsTemporary = false;
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    ownsTemporary = true;
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, FilePath, overwrite: true);
            }
            finally
            {
                if (ownsTemporary) File.Delete(temporary);
            }
        }
    }

    /// <summary>Call on the UI thread. Dynamic resources update existing windows and popups.</summary>
    public static void Apply(Avalonia.Application application, ApplicationTheme theme)
    {
        ArgumentNullException.ThrowIfNull(application);
        Validate(theme);
        application.RequestedThemeVariant = theme == ApplicationTheme.Gray ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private static void Validate(ApplicationTheme theme)
    {
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme), "Неизвестная тема приложения.");
    }

    private static ApplicationTheme Read(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 3 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new FormatException("Неверная структура настроек темы.");
            var names = root.EnumerateObject().Select(property => property.Name).ToArray();
            if (names.Length != 2 || names.Distinct(StringComparer.Ordinal).Count() != 2 ||
                !names.Contains("format", StringComparer.Ordinal) || !names.Contains("theme", StringComparer.Ordinal))
                throw new FormatException("Настройки темы содержат отсутствующие, неизвестные или повторяющиеся свойства.");
            var format = root.GetProperty("format");
            var theme = root.GetProperty("theme");
            if (format.ValueKind != JsonValueKind.String || format.GetString() != Format || theme.ValueKind != JsonValueKind.String)
                throw new FormatException("Неподдерживаемая версия или структура настроек темы.");
            return theme.GetString() switch
            {
                nameof(ApplicationTheme.Gray) => ApplicationTheme.Gray,
                nameof(ApplicationTheme.Light) => ApplicationTheme.Light,
                _ => throw new FormatException("Неизвестная сохранённая тема приложения.")
            };
        }
        catch (JsonException exception)
        {
            throw new FormatException("Файл настроек темы повреждён. Можно выбрать тему заново.", exception);
        }
    }
}
