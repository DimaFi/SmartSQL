using System.Text.Json;
using SmartSQL.Application;

namespace SmartSQL.Infrastructure;

/// <summary>Small, strictly validated preferences with atomic same-directory publication.</summary>
public sealed class JsonWorkspacePreferencesStore : IWorkspacePreferencesStore
{
    public const int MaxFileBytes = 4096;
    private const string Format = "SmartSQL.WorkspacePreferences.v1";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonWorkspacePreferencesStore(string absoluteFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);
        if (!Path.IsPathFullyQualified(absoluteFilePath))
            throw new ArgumentException("Путь к настройкам рабочего пространства должен быть абсолютным.", nameof(absoluteFilePath));
        FilePath = Path.GetFullPath(absoluteFilePath);
        if (string.IsNullOrEmpty(Path.GetFileName(FilePath)))
            throw new ArgumentException("Укажите путь к файлу настроек рабочего пространства.", nameof(absoluteFilePath));
    }

    public string FilePath { get; }

    public async Task<WorkspacePresentationMode?> LoadModeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveModeAsync(WorkspacePresentationMode mode, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), "Неизвестный режим представления.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { format = Format, mode = mode.ToString() });
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporary = Path.Combine(Path.GetDirectoryName(FilePath)!, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
        var ownsTemporary = false;
        try
        {
            // Preserve damaged or newer-format settings instead of silently replacing them.
            await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                ownsTemporary = true;
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (ownsTemporary)
                    File.Delete(temporary);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private async Task<WorkspacePresentationMode?> LoadCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileStream stream;
        try
        {
            stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        await using (stream)
        {
            if (stream.Length > MaxFileBytes)
                throw new FormatException("Файл настроек рабочего пространства превышает предел 4 КБ.");
            using var content = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (content.Length + count > MaxFileBytes)
                    throw new FormatException("Файл настроек рабочего пространства превышает предел 4 КБ.");
                content.Write(buffer, 0, count);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return ReadMode(content.ToArray());
        }
    }

    private static WorkspacePresentationMode ReadMode(byte[] content)
    {
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new FormatException("Неверная структура настроек рабочего пространства.");
            var properties = root.EnumerateObject().Select(property => property.Name).ToArray();
            if (properties.Length != 2 || properties.Distinct(StringComparer.Ordinal).Count() != 2 ||
                !properties.Contains("format", StringComparer.Ordinal) || !properties.Contains("mode", StringComparer.Ordinal))
                throw new FormatException("Настройки содержат отсутствующие, неизвестные или повторяющиеся свойства.");
            var format = root.GetProperty("format");
            if (format.ValueKind != JsonValueKind.String || format.GetString() != Format)
                throw new FormatException("Неподдерживаемая версия настроек рабочего пространства.");
            var mode = root.GetProperty("mode");
            if (mode.ValueKind != JsonValueKind.String)
                throw new FormatException("Неверный режим представления.");
            return mode.GetString() switch
            {
                nameof(WorkspacePresentationMode.Simple) => WorkspacePresentationMode.Simple,
                nameof(WorkspacePresentationMode.Advanced) => WorkspacePresentationMode.Advanced,
                _ => throw new FormatException("Неизвестный режим представления.")
            };
        }
        catch (JsonException exception)
        {
            throw new FormatException("Файл настроек рабочего пространства повреждён.", exception);
        }
    }
}
