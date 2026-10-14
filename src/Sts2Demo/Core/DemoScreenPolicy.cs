namespace Sts2Demo.Core;

/// <summary>Explicit allowlist: never broadcast personal menus or unknown mod screens.</summary>
public static class DemoScreenPolicy
{
    public static bool Allowed(string? screen, bool restSite, bool combat, bool ancientRelicChoice = false)
    {
        if (screen == null || restSite || combat || ancientRelicChoice) return false;
        // Map is only a local utility overlay; the publisher retains the previous reward.
        return screen is "NRewardsScreen" or "NCardRewardSelectionScreen" or "NMapScreen";
    }
}
