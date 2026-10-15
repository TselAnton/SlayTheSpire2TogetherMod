namespace Sts2Demo.Core;

/// <summary>Explicit allowlist: never broadcast personal menus or unknown mod screens.</summary>
public static class DemoScreenPolicy
{
    public static bool Allowed(string? screen, bool restSite, bool combat, bool ancientRelicChoice = false, bool allActivities = false)
    {
        if (screen == null) return false;
        if (allActivities) return KnownActivity(screen);
        if (combat) return false;
        if (restSite || ancientRelicChoice) return false;
        // Map is only a local utility overlay; the publisher retains the previous reward.
        return screen is "NRewardsScreen" or "NCardRewardSelectionScreen" or "NMapScreen";
    }

    public static bool KnownActivity(string? screen) => screen is
        "NRewardsScreen" or "NCardRewardSelectionScreen" or "NMapScreen" or "NMapRoom"
        or "NEventRoom" or "NMerchantRoom" or "NMerchantInventory" or "NFakeMerchant" or "NFakeMerchantInventory"
        or "NCrystalSphereScreen" or "NRestSiteRoom"
        or "NChooseARelicSelection" or "NChooseACardSelectionScreen" or "NChooseABundleSelectionScreen"
        or "NDeckCardSelectScreen" or "NSimpleCardSelectScreen" or "NDeckUpgradeSelectScreen"
        or "NDeckTransformSelectScreen" or "NDeckEnchantSelectScreen" or "NCombatPileCardSelectScreen"
        or "NDeckViewScreen" or "NSimpleCardsViewScreen" or "NInspectCardScreen" or "NInspectRelicScreen"
        or "NCardPileScreen";
}
