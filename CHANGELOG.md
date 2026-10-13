# Changelog

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
