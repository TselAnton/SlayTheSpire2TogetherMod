# Friend View 1.3.0

Observe a friend's co-op play in Slay the Spire 2: combat, reward choices, shops, dialogue, predictions, rest sites, treasure, relic choices and supported card screens. All players need matching game and mod versions. Build target: v0.107.1, Godot 4.5.1 / .NET 9.

Click a friend in the left player list to observe. If DEMO is unavailable, the original player-details action is preserved. Click again or use Esc / native controller cancel to return. The Friend View settings tab offers separate small eye buttons instead of default player-row entry, and all supported activities instead of legacy combat/rewards only. Eyes show/hide together: purple means loading, green means active, disabled means unavailable.

Settings use native right-hand checkboxes with English/Russian captions and hover/focus descriptions. **Show Friend View Button** enables separate eyes. **Battle and Rewards Only** restricts supported activities. Both are off by default.

Controller actions and glyphs follow the game's current remapping. Select enters or switches; cancel exits; confirm previews upgraded reward cards. In combat, confirm still belongs to your own End Turn action. Hints appear to the right of the eye.

The header shows the observed player's portrait with an icon-only badge, HP, gold, potions, relics and deck count. The deck button opens their deck. Map and settings use your own native screens. A watched player opening the map opens your shared native map. Card/potion arrows resolve the actual target; consumed potions disappear after the game's state update.

Observation cannot play cards, buy items, claim rewards or use the friend's potions. Activity rendering uses bounded asset/text/geometry state only while watched, with changed elements at most twice per second. No screen images or desktop capture are sent. Unknown windows close DEMO. Existing windows adapt to supported visual elements; new drawing methods require validation after game updates. Decorative effects can differ.

## Install and remove

Subscribe and enable Friend View in the built-in loader, or copy this folder into the game's mods directory while the game is closed. No external loader is needed. Internal mod ID remains Sts2Demo; avoid duplicate manual and Workshop installations. Disable/unsubscribe or remove only this mod's folder to uninstall.

The normal profile is used only when Friend View is the sole successfully loaded cosmetic mod. Other mods or load errors retain native modded-profile isolation. This choice is fixed until restart. No save copying or merging is performed; MODDED remains visible. Settings live in friend-view.cfg in the game's data directory.

## Validation and credits

Release 1.3.0. The repository's docs/RELEASE_1.3.0.md records actual tests and remaining checks, including real Steam co-op and physical Steam Deck/controller validation.

Own code: MIT, Anton-PC; developed with OpenAI Codex. Analysis and packaging: Universal Modder and ILSpy; Harmony from the installation and Godot.NET.Sdk. Native loader reference: https://github.com/jiegec/STS2FirstMod. Game resources belong to Mega Crit and are resolved from the installation, never redistributed. No AI-generated in-game art/audio. The existing Workshop cover was generated separately with OpenAI imagegen.
