namespace SmartSQL.Infrastructure;

public static class AtomicFileExport
{
    /// <summary>
    /// Writes an export beside its destination and publishes it only after writing succeeds.
    /// An existing destination is never overwritten. The writer must leave the stream open.
    /// </summary>
    public static void Create(string destinationPath, Action<Stream> write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(write);

        var destination = Path.GetFullPath(destinationPath);
        if (File.Exists(destination))
            throw new IOException($"Файл уже существует: {destination}");

        var temporary = Path.Combine(Path.GetDirectoryName(destination)!,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        var ownsTemporary = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            if (ownsTemporary)
                File.Delete(temporary);
        }
    }
}
