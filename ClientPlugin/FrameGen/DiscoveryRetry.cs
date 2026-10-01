using System;
using System.Threading;

namespace ClientPlugin.FrameGen;

/// <summary>Retry missing optional APIs on assembly load or after a bounded delay.</summary>
internal sealed class DiscoveryRetry
{
    private static int _assemblyVersion;
    private int _seenVersion = -1;
    private int _lastAttemptTick;

    static DiscoveryRetry()
    {
        AppDomain.CurrentDomain.AssemblyLoad += (_, _) => Interlocked.Increment(ref _assemblyVersion);
    }

    internal bool ShouldRetry()
    {
        var version = Volatile.Read(ref _assemblyVersion);
        var now = Environment.TickCount;
        if (_seenVersion == version && unchecked(now - _lastAttemptTick) < 2000)
            return false;
        _seenVersion = version;
        _lastAttemptTick = now;
        return true;
    }

    internal void Reset() => _seenVersion = -1;
}
