namespace Vakage;

/// <summary>
/// One running copy at a time. A second copy exits immediately instead of
/// opening a second window onto the same library.
/// </summary>
internal static class SingleInstance
{
    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, @"Local\Vakage.SingleInstance", out var createdNew);
        if (createdNew) return true;
        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    public static void Release()
    {
        try
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch
        {
            // Releasing on shutdown is best effort.
        }
        _mutex = null;
    }
}
