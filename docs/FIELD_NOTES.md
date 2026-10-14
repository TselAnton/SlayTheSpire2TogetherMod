---
kind: game
title: "Read-only co-op hand and reward observation in Slay the Spire 2"
game: "Slay the Spire 2"
games_also: []
game_version: "v0.107.1, commit 59260271, Steam"
platform: windows
engine: godot
route: managed-patch
tools: ["Universal Modder 0.2.0", "ILSpy 11.1.0.9782", "Harmony", "Godot.NET.Sdk 4.5.1", ".NET SDK 10.0.401"]
anti_cheat: "none detected; isolated offline co-op lab only"
status: in-progress
agents: ["OpenAI Codex"]
humans: []
date: 2026-10-14
links: ["https://github.com/jiegec/STS2FirstMod"]
tags: [coop, spectator, ui, rewards, godot, dotnet]
---

# Read-only co-op hand and reward observation in Slay the Spire 2

Together Demo uses the game's native loader plus Harmony to display detached hand/reward UI, not transfer player control. A real-game synthetic three-player oracle verifies read-only state, own end turn, local death, target switches, and reward exit. Actual Steam co-op remains unverified.

## Setup

Windows x64, game v0.107.1 commit 59260271, custom Godot 4.5.1.m.12, .NET 9. Runtime 9 and .NET SDK 10.0.401 installed. Universal Modder 0.2.0 scans the installation; ILSpy 11.1.0.9782 reads managed symbols privately outside the repository. Example loader usage: [STS2FirstMod](https://github.com/jiegec/STS2FirstMod).

## Route and why

Native `ModInitializer`, JSON manifest and DLL, with Harmony patches. No executable/assembly modifications or PCK, no external loader. Reusing installed scenes at runtime avoids distributing game resources.

## How the game works (what we had to learn)

`NPlayerHand` owns playable input and selection. Detached `NCard` nodes can mirror copies of actual replicated teammate models, while pose messages provide normalized UI transforms and selected-holder state. `NMultiplayerPlayerState` is the persistent avatar list; its fixed root size is larger than the visible status, whereas the native Hitbox follows status width.

`NCardRewardSelectionScreen.RefreshOptions` exposes actual generated options. Detached reward models can be reconstructed with `CardModel.FromSerializable` without invoking reward claims, RNG or deck insertion. `NRewardsScreen` exposes the reward list. Skip is transmitted as an alternative index but deliberately not drawn as a control in demo.

The native network message registry discovers mod types reflectively. Cosmetic mods otherwise allow mismatches; adding the visual protocol's version to the native compatibility list prevents silently communicating mismatched message types. This path requires a real two-client validation before release.

## Build steps

1. Own/install the supported game; restore/build `src/Sts2Demo` with its `GameDir` property.
2. Run `tests/Sts2Demo.Tests` for pure state/network checks.
3. Use a copied game and guarded custom user directory for laboratory testing, Steam off. Never include `Sts2DemoLab` in a release.
4. Run `scripts/Build.ps1`; place only `dist/Sts2Demo` in the game's `mods` directory after closing the game.
5. All co-op participants need exactly the same mod and game versions.

## Verification

24 managed assertions pass. The real Godot game runs with synthetic packets serialized before delivery. Native end turn affects only local readiness; card/deck fingerprints remain unchanged by observation. After local death the combat controller remains visible. Three reward candidates are rendered without Skip or duplicate eyes; eye/ESC exits preserve the original local choices. Switching targets 2 -> 3 -> 2 moves active tint and display state. Screenshots were visually inspected. See `VERIFICATION.md` for exact limitations: Steam network, human mouse/controller input, additional resolutions and full reward-to-map transition are not confirmed.

## Gotchas

1. Wrong Godot isolation key silently uses ordinary user data. Use `application/config/custom_user_dir_name`, and check `OS.GetUserDataDir()` before entering the game scene. An initial offline bootstrap incident is documented in the journal; never claim total save isolation before validating this guard.
2. `NTopBarHp` duplication cannot resolve `%HpLabel` if native scene ownership is retained. Duplicate script nodes, rebind unique child ownership, then initialize with the observed player. Hide original HP via transparency to preserve HBox layout.
3. A controller under combat UI disappears on local death. Attach it as a sibling under the combat room instead.
4. Fixed avatar-row root width places eye far from status. Position using native Hitbox's current width, aligned with the avatar.
5. Separate combat and reward presence counters can overwrite each other. Use one sequence source for the run's observation presence.
6. Lab label counts must exclude native text inside card scenes. Count observer content labels, not all Label descendants.
7. `uvx um` installs an unrelated package. Use `uvx --from universal-modder um` or the installed Universal Modder executable.
8. `QueueFree` waits until frame end, briefly leaving old hand nodes during immediate target swaps. Detach all old/fading hand children immediately and bind removal tweens to their card nodes.

## Assets

Eye texture, cards and energy scenes resolved from the user's installed game at runtime. No generated art, extracted textures or game DLLs included in the package.

## Open questions

Steam two-client protocol and native mod mismatch checks; real selected-card/cursor changes; UI-mod conflicts; Workshop uploader details for the publishing game version. This note is local only, not contributed publicly.

## 1.2.0 update: native presentation without replaying actions

1. **Hover jitter / tiny clickable spot.** Do not scale the input TextureButton, hide/show it on a timer,
   or anchor it to NMultiplayerPlayerState.Hitbox (the latter animates its size). Keep a fixed rectangular
   hitbox with no bitmap click mask. Anchor outside-combat eyes to NHealthBar.HpBarContainer, not the
   much larger root control. Combat eyes use the creature/intent top so they do not overlap its mini card.
2. **Card reward hover stuck on card zero.** Controller default focus can persist while the mouse moves.
   Check the mouse hitbox first; use focused holder only in controller mode.
3. **Arrow differs from local play.** NTargetingArrow has 19 sprites plus a tip. The local renderer is
   the geometry oracle: send the actual transforms and tint, not an approximate line or copied formula.
   Resolve textures in the installed game. Suppress the observed peer's NRemoteTargetingIndicator
   cosmetically; never call StartDrawingFrom in production, because it changes Input.MouseMode.
4. **Native upgraded previews.** Clone the inspect screen's NUpgradePreviewTickbox subtree without
   its screen/signal connections, rebind unique child ownership, and localize its existing label.
   Preview upgrades only on detached FromSerializable models with UpgradePreviewType.Deck.
5. **Full event/shop/minigame interfaces.** Reconstructing these with live native screen controllers
   can subscribe to rewards, RNG and commands. A bounded on-demand game-viewport stream avoids
   executing any of those handlers at the observer. CrystalSphereScreen uses NetScreenType.None,
   so snapshot its actual viewport and normalized cursor, not the regular screen-type cursor mapping.
6. **Native player order differs by client.** NMultiplayerPlayerStateContainer puts the local player first.
   Exclude source rows/eyes from activity images and keep the observer's real local player list above
   the read-only surface. Reward snapshots crop the Rewards subtree, excluding its sibling Proceed.
7. **Exit can race a new entry.** Kill pending fades, restore the local hand's original alpha, then build
   the next view. Do not let an old exit callback clear a newly selected player's cards.
8. **Canonical event lab fixture.** EnterRoomDebug clones canonical EventModel instances per player;
   a mutable event causes MutableModelException. This differs from the combat encounter fixture.

Verification: Godot native lab exercises corner/centre mouse events, three players, own End Turn,
death, native-renderer arrow transforms, upgrades, reward crop, production image publisher/decoder,
predictions, shop/event/map and forbidden screens. Protocol tests bound fragments and JPEG dimensions.
Real Steam transport, Deck 1.2.0 load/performance and controller behavior remain separate release gates.
The user confirmed Deck startup of the previous 0.1.1 only.

Input test reference: [Godot 4.5 Viewport.push_input](https://docs.godotengine.org/en/4.5/classes/class_viewport.html#class-viewport-method-push-input).
The test uses viewport coordinates and the real GUI event path, not just emitting a button's Pressed signal.
