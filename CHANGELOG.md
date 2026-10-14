# Changelog

## 1.2.1 — 2026-10-14

- Native controller navigation: select a teammate's combat character or player-list row, then use the focused-selection action (default Xbox A) to enter, switch friends or toggle the green eye off.
- Native cancel (default B) exits from anywhere in demo. Restore reward-screen focus and consume the matching release so it cannot activate the restored local screen.
- Native confirm (default Y) toggles all detached reward-card upgrade previews regardless of focus. In combat the same action stays with the LOCAL End Turn button.
- Authentic installed-game glyph controls and textures, refreshed for controller type, mouse/controller mode and current in-game remappings; no hard-coded physical gamepad buttons.
- Single-owner press/release routing, including ambiguous aliased mappings and held buttons; normal card targeting and normal gameplay controls remain owned by the game.
- Expanded isolated controller fixtures and a physical-controller / Steam Deck release checklist.

## 1.2.0 — 2026-10-14

- Stable rectangular eye hitboxes: no hover scaling of the input control, periodic hide/show or animated-hitbox anchoring.
- Combat eyes above teammates, no player-list combat eyes; active green eye toggles observation off.
- Native targeting-arrow art and exact live segment transforms, including tip size, curve and target tint; suppress the observed teammate's duplicate remote line.
- Smooth exit crossfade restores the local hand and reward interface, including interrupted transitions.
- Mouse hover takes priority over stale controller focus in card rewards; native localized upgrade-preview tickbox updates detached display copies of all offered cards.
- Complete native reward window with frame and icons, without a duplicate Proceed/Skip control.
- On-demand, bounded read-only viewport snapshots for shops, events, predictions, maps and supported out-of-combat card screens. Player-list controls stay local.
- Rest sites and relic-choice screens disable demo and close an active view; personal menus and unknown mod screens are never broadcast.
- Expanded repeatable lab, packet/security assertions and pre-release checklist. Released with author approval for subsequent real-client validation.

## 0.1.1 — 2026-10-14

- Linux/SteamDeck startup: expose the system unwind provider globally before Harmony initializes, fixing MonoMod's missing `_Unwind_RaiseException` dependency without shipping native libraries or installing packages.
- Use the normal profile only when Friend View is the sole successfully loaded cosmetic mod. Keep native profile isolation with any other mod or load error; no save copying/merging and no hiding the MODDED state.
- Display name is Friend View; internal `Sts2Demo` ID remains stable for compatibility.
- Private diagnostic logs are excluded from builds, release packages and Git.

## 0.1.0
- Combat demo mode with teammate eye buttons, read-only animated card mirrors and the game's energy counter.
- Live relics, powers, health and block, remote mouse and selected-card transforms.
- One standalone eye near each friend's avatar-row status; green active eye toggles out, Escape/controller cancel; no world/presence eye duplicates, mode labels or return button.
- Native top-bar health display and relics starting at the left of the upper row.
- Native end-turn remains available for the observer's own character; no end-turn patch.
- Reward-list and card-reward observation with hover/chosen feedback and read-only input. Card-choice exit eye is right of cards; other exits use avatar rows. No mirrored Skip/alternative reward controls.
- Selected teammate avatar remains green in reward observation; switching friends restores the previous tint. Three-player target switches tested in combat and rewards.
- Immediately detach the previous hand when switching targets, including cards fading out.
- Run-independent combat overlay to keep observing after the local character dies.
- Automatic exit for local required choices, disconnect, overlays and combat end.
- Cosmetic manifest; co-op version compatibility requirement for the visual protocol.
