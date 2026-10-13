using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Saves;
using Sts2Demo.Core;

namespace Sts2Demo;

/// <summary>Only select a directory; never copy, merge, read or write save files.</summary>
[HarmonyPatch(typeof(UserDataPathProvider), nameof(UserDataPathProvider.GetProfileDir))]
internal static class DefaultProfilePatch
{
    private static ProfileSelection _selection = new();
    static void Postfix(int profileId, ref string __result)
    {
        var mods = ModManager.Mods.Select(m => new ProfileMod(m.manifest?.id,
            m.state == ModLoadState.Loaded, m.errors?.Count > 0, m.manifest?.affectsGameplay ?? true)).ToArray();
        if (_selection.UseDefault(ModEntry.Initialized && ModManager.State == ModManagerState.Initialized, mods, ModEntry.Id))
            __result = $"profile{profileId}";
        // All other configurations retain the game's original result and protection.
        // IsRunningModded stays true: do not disguise patches from metrics/debug reporting.
    }
}
