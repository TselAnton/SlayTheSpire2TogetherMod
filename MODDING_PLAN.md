# Friend View

- Target: Slay the Spire 2 (Steam 2868840), local v0.107.1 / 59260271.
- Engine: Godot 4.5.1.m.12 custom build, Windows x64, managed .NET 9 game logic in sts2.dll.
- Scan: no anti-cheat or third-party loader detected in the install.
- Route: built-in JSON + DLL mod loader, ModInitializer, runtime Harmony UI hooks. No external loader, no patched game assemblies.
- UI: separate NCards on display-only mutable copies; NEnergyCounter for the observed player; runtime eye/relic/power textures. No bundled game assets.
- Networking: supported INetMessage extension, cosmetic presence and pose messages. Include RunLocation and sequence numbers; reject old locations, old sequences, invalid sizes/poses and unknown/disconnected senders. Require matching mod version in the existing lobby compatibility check.
- Input: no clickable card holders in the mirrored hand; local hand shortcuts blocked only while demo is active. Native end turn remains available for the local player. Exit before required local choices.
- Saves/config/logs: native user-data paths; no save-file IO or serialization changes. In 0.1.1, a user-authorized profile-directory hook selects the normal profile only when Friend View is the sole successfully loaded cosmetic mod. Other startup configurations retain isolation; selection is latched for the process. MODDED reporting stays true; no copying/merging.
- Lab: full independent game copy in ignored artifacts/lab. Correct setting is application/config/custom_user_dir_name=Sts2TogetherDemoLab. Fail-closed GDScript checks OS.GetUserDataDir before game scene load; --force-steam off disables Cloud. Synthetic three-player run has shouldSave=false.
- Backups: original game files have not been modified. The independent lab copy retains original game resources. Installer backs up only a prior mods/Sts2Demo folder before an update.
- Verification: managed wire/state, Linux bootstrap/profile tests, runtime lab fingerprints/path-only tests and screenshots. Actual Deck loading and PC/Deck Steam co-op remain pending. Full pre-release gate is docs/PRE_RELEASE_TESTS.md; 0.1.1 was published with explicit author permission despite pending manual tests.
