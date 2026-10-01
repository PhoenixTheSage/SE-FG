namespace ClientPlugin.FrameGen;

/// <summary>Tracks color-history validity and failures of the complete insertion.</summary>
internal sealed class FrameInsertionState
{
    internal bool NeedsReset { get; private set; } = true;
    internal int ConsecutiveFailures { get; private set; }
    internal bool Blocked => ConsecutiveFailures >= 3;

    internal void RequireReset() => NeedsReset = true;
    internal void GenerationSucceeded() => NeedsReset = false;

    internal void Failed()
    {
        RequireReset();
        if (!Blocked)
            ConsecutiveFailures++;
    }

    internal void Presented() => ConsecutiveFailures = 0;

    internal void Reset()
    {
        RequireReset();
        ConsecutiveFailures = 0;
    }
}
