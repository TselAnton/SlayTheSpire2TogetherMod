namespace Sts2Demo.Core;

internal static class DemoPresenceSequence
{
    private static uint _value;
    public static uint Next() => ++_value;
}
