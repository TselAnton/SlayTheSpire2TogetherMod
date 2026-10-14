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

## Visual-audit correction (2026-10-14)

Later author decision supersedes remote activity support: demo is now combat and
post-combat rewards only. Identical source/projection fixtures did not establish
correctness across independent players in randomized event rooms. Deny unsupported
contexts before serialization, not just before rendering; otherwise a Kind=None
packet can still carry event card options. Disable activity publishing and reject
incoming Activity observations. Require a combat-room context for reward screens.
Combat eyes now share the fixed player-row anchor with reward eyes; native row
controller focus and five-point rectangular mouse hitboxes are verified in the lab.

Follow-up after the author's screenshot review: the native `NCommonBanner`
must be treated as a composed installed UI component, not only its captured
text/texture children. Native rich text has separate bold/italic size keys.
Source Z values must order primitives within the projection, not move them below
its backdrop. Reward icons use the native Icon container's reference size.
`NCard.Size` is zero despite a visible centred card face, so exit-eye layout
must account for the art footprint. `NTargetManager` targets a creature's VFX
anchor; the old fixture's arbitrary target above the enemy was not a valid oracle.

The shared observed top bar keeps native local map/settings buttons and opens
the observed player's deck. A copied HP control needs unique-owner rebinding;
Gold cannot be copied with lifecycle scripts because GoldPopup belongs outside
its subtree. Scriptless gold/potion-container displays avoid copying gameplay
subscriptions. Native unusable potion holders preserve slot visuals. Final
isolated verification: 87 checks, different friend gold/potions/decks, 59 fresh
screenshots in a separate directory. No independent Steam-client verification.
Both merchant variants are now blocked (author-approved fallback); a friend's
map overlay preserves the preceding activity. See VISUAL_FIXES_1.2.2.md for the
current candidate; older results below are historical. No community PR opened.

Native node-count/network assertions are not visual parity evidence. Paired native
screenshots exposed missing map-node icons, merchant scene composition, outer
tooltip layers and the friend's top bar outside combat. See
`docs/VISUAL_AUDIT_1.2.2.md`; the visual release gate remains failed.
Preserve the detached rewards panel's original anchors/offsets (FullRect stretches
its stone frame). Locale substitution can wrap fonts in runtime FontVariation
objects with empty resource paths: resolve the installed BaseFont, and transport
text alignment, wrapping and outline instead of relying on renderer defaults.
Native upgrade/enchant previews require a source card with a Pile/CardScope;
owner-only detached display cards are insufficient test fixtures. Use cards from
the isolated lab deck to open previews, never confirm/apply changes.

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

## 1.2.1 controller input ownership (2026-10-14)

The native game distinguishes `MegaInput.select` (focused-control selection, Xbox A) from
`MegaInput.accept` (global confirmation, Xbox Y / local End Turn). `NInputManager` translates
the current controller mapping late in `_UnhandledInput`; indiscriminately swallowing raw
joypad events in an observer's `_Input` prevents remapping from working. The run-scoped observer
now owns only its contextual actions and reads the native mapping when routing a physical
controller event. Unrelated input passes to the game. Cancel wins over selection for an active
demo; one press and its matching release get one owner, including release after a remap.

Clone the installed inspect tickbox's `%ControllerIcon` control, get its texture using
`NInputManager.GetHotkeyIcon`, and preserve its native dimensions/filtering. Use eye
`SelfModulate` rather than `Modulate`: otherwise the green eye incorrectly tints its child glyph.
Poll the current glyph for remaps and device changes, without installing custom global bindings.
Target selection/card-play guards are checked before showing A and accepting an entry.

Fixture gotchas: `ProcessFrame` fires before the UI's next `_Process`; wait two frames before
asserting a fresh glyph. Switching to PlayStation changes native mapping type and can reset the
input-map dictionary; keep that change in-memory in the lab and restore both mapping and type.
The isolated lab uses native controller-config glyphs and synthetic action events, never OS input.
Physical controllers and actual Steam Input remain a manual release gate.

## 1.2.2: local UI metadata instead of image streaming

1. **Hand at the origin after exit:** replaying `NPlayerHand.AnimIn` changes root geometry,
   not just opacity. Refresh its native layout and preserve its root transform; apply a
   mod-owned alpha transition only. Test a nonzero hand position, not just the usual scene.
2. **Stale rightmost reward focus:** nested holder/hitbox iteration is not option order.
   Match `NGridCardHolder.CardModel` to the offered models. Send normalized cursor/layout
   anchors and compute mouse hover on the observer; use model-mapped focus only in controller mode.
3. **Live reward factories are not display factories:** `Reward.FromSerializable` can add
   cards, populate rewards or consume RNG. Use display-only DTOs. Detach the unready native
   reward-window branch and populate `NRewardButton` controls without live Reward objects.
   FullRect layout avoids a misplaced native ribbon/scrollbar. Never add the scripted screen root.
4. **Prediction fog is not a Visible flag:** the native `NCrystalSphereMask` uses a shader
   with 121 `gridFadeParams`. Underlying item sprites remain visible in the tree. Omit wholly
   hidden item identities and clip partial item visuals to revealed cells. Preserve shader
   parameters with their types, including PackedVector3/4 and Color arrays.
5. **Text/atlas appearance:** generic RichTextLabel shows native gold/sine/fly tags literally.
   Use the installed MegaRichTextLabel effects with required font overrides and fixed font sizes.
   Capture AtlasTexture's underlying texture/region even if its own resource path looks usable.
6. **Avoid repeated workload:** no GPU readback/JPEG in the mod; Brotli DTOs contain only
   installed asset paths, text, finite geometry and allowed uniforms. Apply changed elements,
   remove vanished ones and resync periodically. Gate expensive capture on presence; use tiny
   availability messages otherwise. Retain bounds before decoding and reject private paths,
   external images, invalid sequences and missing delta bases. Off-window mouse positions must
   be clamped before publishing to avoid rejecting the sender's own packets.
7. **Static targeting arrow:** preserve native art, transmit endpoints rather than animated
   segment transforms, compute the curve locally and cache it until endpoints/resolution change.
   Do not instantiate native targeting logic on the observer (it can alter native input mode).

Verification: pure packet/metadata tests and an isolated native game with synthetic transport,
cursor order 0→2→1→0 against stale focus 2, unchanged gameplay checks, native-window screenshots,
prediction hidden/partial-state checks and measured individual compressed states. Exact results
and candidate hash are recorded in RELEASE_CANDIDATE.md. Steam transport, PC↔Deck FPS/CPU,
four players and long sessions remain manual. The projection is not pixel-identical: arbitrary
GPU/Spine/custom drawing and dynamic map-drawing surfaces are not replicated. No image fallback.

Reference: [Godot input propagation](https://docs.godotengine.org/en/stable/tutorials/inputs/inputevent.html).
