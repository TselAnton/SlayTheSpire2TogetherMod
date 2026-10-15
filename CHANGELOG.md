# 1.4.0 (local candidate)

- Native watched-player combat HUD, Osty for any character, Regent stars and Defect orbs.
- Player-specific enemy intent/stolen-card visuals and target-aware card previews.
- Independent pile/deck inspection, settings preservation, avatar exit through card selectors, map-vote visual restoration and watched relic tips.
- Cross observation publishes underlying own state; treasure observation disabled; transition covers observer buttons.
- Five localized settings, separate eyes enabled by default, automatic turn/room exit and next-player observation, optional independent hand interaction.
- Correct merchant-hand parent exclusion; native power tooltips replace the invalid custom formatting path found in supplied logs.
- Investigated early startup failures; root cause remains unproven. See docs/RELEASE_1.4.0.md for tested scope and limits.

# Changelog

## 1.3.1 — 2026-10-14

- Replace completed reward panels with the waiting battlefield, including battle/rewards-only mode.
- Bind native draw, discard and exhaust widgets to the watched player; restore local bindings and subscriptions on exit.
- Preserve visual nodes and animate presentation changes locally. Card-list and dialogue hover use native neutral geometry plus the existing cursor stream, independent of the 2 Hz window snapshots.
- Omit the decorative merchant hand in observation.
- Hide native duplicate targeting lines without fighting their alpha tween or tinting them black.
- Give an active observation priority on avatar click, including the shared map.
- Add in-game dynamic regression scenarios and bump visual/reward protocols to 4; all participants must use 1.3.1.

## 1.3.0 — 2026-10-14

- Observe supported native events, dialogue, shops, predictions, rest sites, treasure, relic choices and card selection screens. Unknown screens close observation.
- Send bounded asset/text/geometry state through the game's reliable transport, with changed elements at most twice per second and an explicit full-state recovery request. No screen images are transmitted.
- Open the observer's native shared map when the watched player opens the map.
- Add persistent native settings: player-row entry by default or a separate eye button; all supported activities by default or legacy combat/rewards only.
- Use concise English/Russian setting captions, native right-hand checkboxes and mouse/controller-focus descriptions.
- Align smaller eyes in one column beyond the widest health bar. Show/hide the whole column together; keep unavailable peers disabled, and highlight pending entry while requesting their state. Controller hints sit to the right.
- Show the watched player's native header and relics, with an icon-only observation badge. Reward cards use the sender's choices and owner, including after switching friends.
- Resolve card/potion targeting by creature or player identity. Used potions disappear from the observed header. Preserve local hand input and layout after exiting.
- Exercise native window projection, settings, ownership, delayed peers, map routing and potion use in an isolated game lab. See docs/RELEASE_1.3.0.md for evidence and outstanding release checks.

## 1.2.2 — 2026-10-14

- Preserve the local hand layout and exit crossfade when leaving demo.
- Compute reward-card hover from the teammate's cursor; render post-combat reward descriptions and icons locally without screen-image streaming.
- Add the observed player's native top bar: portrait with observation badge, HP, gold, potion slots and deck count. Map/settings open local screens; the deck button opens the teammate's deck.
- Restore native localized selection banners, text sizes and reward-icon sizing; enlarge and centre the reward-card exit eye.
- Render a static targeting arrow using native art and the enemy's target anchor, without elastic hover animation.
- Put combat and reward-list eyes beside each teammate's status, with stable rectangular hitboxes, green toggle state and remappable controller actions.
- Limit demo to combat and post-combat rewards. Events, dialogue, predictions, shops, rest sites and other activities are unavailable; their activity state is not published.
- Send detailed hand/reward updates only while someone is watching; keep the previous observed screen when a teammate opens the map.

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
