namespace Sts2Demo.Core;

/// <summary>Pure UI state; no game model or filesystem access.</summary>
public sealed class DemoSession
{
    public ulong? Target { get; private set; }
    public bool Active => Target.HasValue;
    public bool TryEnter(ulong local, ulong target, bool connected, bool combat, bool busy)
    {
        if (local == target || !connected || !combat || busy) return false;
        Target = target;
        return true;
    }
    public void Exit() => Target = null;
    public static bool Newer(uint incoming, uint previous) => unchecked((int)(incoming - previous)) > 0;
}
