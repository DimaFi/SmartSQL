using System.Security.Cryptography;
using System.Text;

namespace SmartSQL.Desktop.Storage;

public sealed class StorageRuntimeLease : IDisposable
{
    private static readonly object ActiveRootsGate = new();
    private static readonly HashSet<string> ActiveRoots = new(StringComparer.Ordinal);

    private readonly Mutex _mutex;
    private readonly string _activeRootKey;
    private readonly int _ownerThreadId;
    private bool _disposed;

    internal StorageRuntimeLease(string dataDirectory, string temporaryDirectory,
        StorageSelectionKind kind, Mutex mutex, string activeRootKey)
    {
        DataDirectory = dataDirectory;
        TemporaryDirectory = temporaryDirectory;
        Kind = kind;
        _mutex = mutex;
        _activeRootKey = activeRootKey;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public string DataDirectory { get; }
    public string TemporaryDirectory { get; }
    public StorageSelectionKind Kind { get; }

    internal static (Mutex Mutex, string Key) AcquireRoot(string dataDirectory)
    {
        var key = CanonicalKey(dataDirectory);
        lock (ActiveRootsGate)
        {
            if (!ActiveRoots.Add(key))
                throw new StorageRootSelectionException(
                    "Выбранное хранилище уже используется другим экземпляром SmartSQL в этом процессе.");
        }

        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: false, MutexName("Root", key));
            if (!TryAcquire(mutex, TimeSpan.Zero))
                throw new StorageRootSelectionException(
                    "Выбранное хранилище уже используется другим экземпляром SmartSQL.");
            return (mutex, key);
        }
        catch
        {
            mutex?.Dispose();
            ReleaseFailedAcquisition(key);
            throw;
        }
    }

    internal static Mutex AcquireSelection(string installedBaseDirectory)
    {
        var mutex = new Mutex(initiallyOwned: false,
            MutexName("Selection", CanonicalKey(installedBaseDirectory)));
        try
        {
            if (!TryAcquire(mutex, TimeSpan.FromSeconds(10)))
                throw new StorageRootSelectionException(
                    "Выбор установленного хранилища уже выполняется другим экземпляром SmartSQL.");
            return mutex;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    internal static void ReleaseSelection(Mutex mutex)
    {
        mutex.ReleaseMutex();
        mutex.Dispose();
    }

    internal static void ReleaseFailedAcquisition(string key)
    {
        lock (ActiveRootsGate)
            ActiveRoots.Remove(key);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                "StorageRuntimeLease must be disposed on the thread that opened the storage root.");
        _disposed = true;
        try
        {
            _mutex.ReleaseMutex();
        }
        finally
        {
            _mutex.Dispose();
            ReleaseFailedAcquisition(_activeRootKey);
        }
    }

    private static bool TryAcquire(Mutex mutex, TimeSpan timeout)
    {
        try { return mutex.WaitOne(timeout); }
        catch (AbandonedMutexException) { return true; }
    }

    private static string CanonicalKey(string path)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return OperatingSystem.IsWindows() ? canonical.ToUpperInvariant() : canonical;
    }

    private static string MutexName(string purpose, string canonicalKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalKey)));
        // The same root must not be initialized twice across Windows/RDP sessions.
        var prefix = OperatingSystem.IsWindows() ? @"Global\" : string.Empty;
        return $"{prefix}SmartSQL.Storage.{purpose}.{hash}";
    }
}
