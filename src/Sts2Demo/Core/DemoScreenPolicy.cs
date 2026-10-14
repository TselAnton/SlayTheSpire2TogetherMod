namespace Sts2Demo.Core;

/// <summary>Explicit allowlist: never broadcast personal menus or unknown mod screens.</summary>
public static class DemoScreenPolicy
{
    public static bool Allowed(string? screen, bool restSite, bool combat, bool ancientRelicChoice = false)
    {
        if (screen == null || restSite || combat || ancientRelicChoice) return false;
        if (screen is "NRewardsScreen" or "NMapScreen" or "NMapRoom" or "NEventRoom"
            or "NMerchantRoom" or "NMerchantInventory" or "NFakeMerchant" or "NFakeMerchantInventory"
            or "NDeckViewScreen" or "NInspectCardScreen" or "NSimpleCardsViewScreen"
            or "NCardRewardSelectionScreen" or "NDeckCardSelectScreen" or "NDeckUpgradeSelectScreen"
            or "NDeckTransformSelectScreen" or "NDeckEnchantSelectScreen" or "NSimpleCardSelectScreen"
            or "NChooseACardSelectionScreen" or "NChooseABundleSelectionScreen"
            or "NCrystalSphereScreen") return true;
        return false;
    }
}
