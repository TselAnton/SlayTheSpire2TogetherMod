# Friend View

Follow your friends' co-op play in Slay the Spire 2 with a read-only view of their hand, energy, health, powers, relics, cursor, animated card movements and reward choices.

Click the eye beside a friend's status in the player list. The active friend's eye and avatar turn green; click the active eye again or press Esc to return to your own view. In card reward view, the exit eye is to the right of the cards. To change friends there, exit first and choose another friend in the player list.

You cannot play or select cards or claim rewards while observing. The native End Turn button still ends your own turn. Observation works after your character dies while a teammate is still playing.

## Requirements and installation

- Slay the Spire 2; build target v0.107.1, .NET 9. Linux/SteamDeck startup prepares the system unwind provider before Harmony.
- All co-op players must use the same game and Friend View versions.
- Subscribe through Steam Workshop and enable Friend View in the built-in mod loader. No external loader or extra library is required.
- For a manual install, copy this folder into the game's `mods` folder while the game is closed.
- Disable the mod in the game, or unsubscribe, to remove it. Do not delete or edit your saves.

Version: 0.1.1. Internal mod ID and DLL name remain `Sts2Demo` for compatibility. Do not keep a second manually installed copy alongside the Workshop copy.

The mod focuses on combat hands, reward lists and card reward choices, not shops, events or separate relic-selection screens. It does not change card effects, combat rules or save serialization. When Friend View is the only successfully loaded cosmetic mod, the native game uses your normal profile. With any other mod or a loading error, normal modded-profile isolation remains. This choice is fixed until restart. Existing normal and modded data are never copied or merged; the game itself continues to save the selected profile. Back up your saves before testing a new version. The MODDED indicator remains visible.

## Troubleshooting

After a game update, check that every lobby member has matching versions. Game logs contain the prefix `[Friend View]` (0.1.0: `[Together:Demo]`). If the observer encounters an incompatible UI, it closes the view and restores your interface.

## Credits and license

Own code: MIT, Anton-PC. Built with assistance from OpenAI Codex. Universal Modder was used for analysis and packaging checks; ILSpy for private inspection of the installed game's API; Harmony from the game installation and Godot.NET.Sdk for the mod. Native loader reference: https://github.com/jiegec/STS2FirstMod.

Game scenes and textures are resolved from the user's installed game at runtime, not redistributed. Slay the Spire 2 and its resources belong to Mega Crit. No AI-generated in-game art or audio is included. Workshop cover proposals were generated with OpenAI imagegen; the cover is separate from the playable mod.
