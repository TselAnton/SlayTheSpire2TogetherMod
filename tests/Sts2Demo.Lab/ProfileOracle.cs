using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Saves;

namespace Sts2DemoLab;

/// <summary>Synchronous path-only checks in the isolated lab; no save IO or registry mutation.</summary>
internal static class ProfileOracle
{
    private static IReadOnlyList<Mod>? _visibleMods;
    private static bool OverrideRegistry(ref IReadOnlyList<Mod> __result)
    {
        if (_visibleMods == null) return true;
        __result = _visibleMods;
        return false;
    }

    public static void Run()
    {
        var realMods = ModManager.Mods;
        var own = realMods.Single(m => m.manifest?.id == "Sts2Demo");
        var getter = AccessTools.PropertyGetter(typeof(ModManager), nameof(ModManager.Mods));
        var prefix = AccessTools.Method(typeof(ProfileOracle), nameof(OverrideRegistry));
        var harmony = new Harmony("friendview.lab.profile-oracle");
        var patchType = AccessTools.TypeByName("Sts2Demo.DefaultProfilePatch");
        var selectionField = AccessTools.Field(patchType, "_selection");
        var originalSelection = selectionField.GetValue(null);
        harmony.Patch(getter, prefix: new HarmonyMethod(prefix));
        try
        {
            _visibleMods = [own];
            selectionField.SetValue(null, Activator.CreateInstance(selectionField.FieldType, nonPublic: true));
            bool ownUsesDefault = UserDataPathProvider.GetProfileDir(1) == "profile1"
                && UserDataPathProvider.GetProfileDir(3) == "profile3";
            _visibleMods = realMods;
            bool latched = UserDataPathProvider.GetProfileDir(1) == "profile1";
            selectionField.SetValue(null, Activator.CreateInstance(selectionField.FieldType, nonPublic: true));
            string protectedPath = UserDataPathProvider.GetProfileDir(1).Replace('\\', '/');
            bool otherModIsolated = protectedPath == "modded/profile1";
            bool stillModded = UserDataPathProvider.IsRunningModded && ModManager.IsRunningModded();
            if (ownUsesDefault && latched && otherModIsolated && stillModded)
                Log.Info("[DemoLab] PASS: native profile paths default with Friend View only; stable within session; isolated with another startup mod; MODDED reporting preserved. No save IO.");
            else Log.Error($"[DemoLab] FAIL: profile policy default={ownUsesDefault} other={protectedPath} stillModded={stillModded}.");
        }
        finally
        {
            _visibleMods = null;
            selectionField.SetValue(null, originalSelection);
            harmony.Unpatch(getter, prefix);
        }
    }
}
