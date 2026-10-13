namespace Sts2Demo.Core;

internal readonly record struct ProfileMod(string? Id, bool Loaded, bool HasErrors, bool AffectsGameplay);

internal static class ProfilePolicy
{
    public static bool UseDefault(bool initializationComplete, IReadOnlyList<ProfileMod> mods, string ownId)
        => initializationComplete && mods.Count == 1 && mods[0].Id == ownId
            && mods[0].Loaded && !mods[0].HasErrors && !mods[0].AffectsGameplay;
}

/// <summary>Latch once after startup; runtime Workshop detection must not change save paths mid-run.</summary>
internal sealed class ProfileSelection
{
    private bool? _decision;
    public bool UseDefault(bool initializationComplete, IReadOnlyList<ProfileMod> mods, string ownId)
    {
        if (!initializationComplete) return false;
        _decision ??= ProfilePolicy.UseDefault(true, mods, ownId);
        return _decision.Value;
    }
}
