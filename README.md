# KK_StoryNPCBridge

English | [简体中文](README.zh-CN.md)

A BepInEx 5 plugin that exposes native story-mode NPC state and control through a local API for Koikatu and Koikatsu Party. External applications can provide manual controls or AI-driven behavior. Model access, prompts, context management and decisions belong to the external application.

## Current scope

**The plugin currently injects one new bridge character. Only this injected character can be controlled.** Its actor ID is `bridge:main`, and its initial appearance uses the game's default female model, `ill_Default_Female`.

Existing story NPCs cannot currently be selected or taken over. The player is not a control target. When the bridge character is unclaimed, the game manages her with its native AI. Claiming her gives the controller control of her actions until release or disconnection.

## Planned NPC targeting

Future development will extend control to existing story NPCs and support targeting:

- One specific NPC.
- A selected set of NPCs.
- All NPCs.

These targeting modes are planned features, not capabilities of the current version.

## Features

- Read and modify character stats, expressions, native actions, animation and position.
- Schedule native actions immediately or queue the next tasks.
- Walk to the player using native navigation, including cross-map routes.
- Use native ADV dialogue with user input, external-response waiting and NPC-initiated invitations.
- Report native dialogue choices and distinguish touch from look while preserving original body reactions.
- Return activity reports directly when taking control; movement history records map changes only.
- Discover English ID descriptions and reuse returned catalog entries, keys, numeric IDs or request templates in calls.

Production integrations use API replies and events. In-game UI is limited to an optional developer panel and essential dialogue controls. The plugin does not add status popups or an activity-report interface.

## Requirements

- The original Japanese Windows release of Koikatu, or Koikatsu Party.
- Koikatsu Party requires purchasing the After Party DLC and installing the patch from the official website before using this plugin.
- BepInEx 5.
- Python 3 for the optional standard-library client.

KKAPI and CheatTools are not required. Game files and BepInEx binaries are not included.

## Installation

Download `KK_StoryNPCBridge.dll` from [Releases](https://github.com/qiqi20020612/KK_StoryNPCBridge/releases/latest). The ZIP contains the same DLL in its installation path and the MIT license; it can be extracted into the game directory. `SHA256SUMS.txt` provides checksums for both downloads.

Copy the single plugin DLL directly into the game's plugin directory:

```text
<Koikatu>/BepInEx/plugins/KK_StoryNPCBridge.dll
```

No plugin subdirectory is needed. On first launch, the plugin creates:

- `BepInEx/config/KK_StoryNPCBridge.cfg`: configuration and the local authentication token.
- `BepInEx/config/KK_StoryNPCBridge/`: persistent bridge-character data and the activity journal.

## UI language

The plugin UI defaults to English. To use Simplified Chinese, edit the cfg and restart the game:

```ini
[UI]
Language = zh-CN
```

Use `Language = en` for English. API descriptions and summaries remain English in either mode. Native names, character names and actual dialogue preserve their original content.

**Left Ctrl + Left Shift + F10** opens the developer panel. The panel is closed by default. F8 remains a game shortcut.

## Quick start

Load a story save and enter free roaming. From this project's directory, provide the game directory explicitly:

```powershell
python -X utf8 tools/bridge_client.py --game-root "<path-to-Koikatu>"
```

The client reads the port and token from the game's cfg. In its manual shell:

```text
list
use 1
summon
get
report
release
quit
```

`use 1` claims the injected bridge character and prints the complete handover report. `summon` makes her walk near the player; it does not teleport. Keep the controller connected while tasks run. Closing the controller releases control to native AI.

Applications can also set `KOIKATU_GAME_ROOT` or pass `game_root` to `BridgeClient`:

```python
from tools.bridge_client import BridgeClient

with BridgeClient(game_root="<path-to-Koikatu>") as bridge:
    actor = bridge.request("status")["actors"][0]
    target = {"target": actor["id"], "generation": actor["generation"]}
    claimed = bridge.request("claim", **target)
    report = claimed["activityReport"]  # Complete entries; no second request.
    catalog = bridge.request("catalog")
    # Use the returned references in further calls while this connection stays open.
    bridge.request("release_all")
```

## Documentation

- [API reference](API.md) ([简体中文](API.zh-CN.md)): transport, commands, ownership, returned values, activity reports and dialogue events.
- [ID reference](docs/REFERENCE.md) ([简体中文](docs/REFERENCE.zh-CN.md)): actions, maps, facial patterns and Animator states.
- `catalog`: the authoritative runtime catalog for the loaded game and character model.

Documentation defaults to English, with Simplified Chinese editions available through the language links. API response descriptions, command names, field names and IDs remain English regardless of documentation or UI language.

## Build from source

Install the .NET SDK selected by `global.json`, then run:

```powershell
dotnet restore KK_StoryNPCBridge.csproj --locked-mode
dotnet build KK_StoryNPCBridge.csproj --no-restore --configuration Release --output artifacts/build
```

The output is `artifacts/build/KK_StoryNPCBridge.dll`, targeting .NET Framework 3.5. This build uses public, stripped compile-time references from [IllusionLibs](https://github.com/IllusionMods/IllusionLibs) and does not require a local game installation. Dependencies and their content hashes are recorded in `packages.lock.json`; reference assemblies are not bundled with the plugin.

To create the release DLL, installation ZIP and SHA-256 checksums:

```powershell
pwsh -File tools/package-release.ps1 -Tag v1.0.0
```

GitHub Actions builds pushes and pull requests. Pushing a `v<version>` tag builds and publishes a release after checking that the tag matches the DLL version.

Alternatively, the Windows .NET Framework 3.5 C# compiler can build against your own game and BepInEx assemblies:

```powershell
.\build.ps1 -GameRoot "<path-to-Koikatu>"
```

The output is `bin/KK_StoryNPCBridge.dll`. To copy it into the game, close the game first and run:

```powershell
.\build.ps1 -GameRoot "<path-to-Koikatu>" -Deploy
```

`KOIKATU_GAME_ROOT` can supply the game directory instead of the parameter. Build outputs and runtime configuration are excluded from version control.

## Behavior and limitations

- Actor writes require a claim and the current generation. Refresh state after loading another story.
- Releasing control or losing the controller connection restores native AI after any pending dialogue or scene cleanup.
- Runtime catalogs describe available references; occupied points, targets and story conditions can still prevent an action from starting.
- Expressions depend on the loaded character model. Animator state hashes and native scheduler action IDs are different domains.
- Activity reports retain the latest 256 entries and include counts for omitted records. They do not record same-map walking trajectories.
- The bridge provides structured events and accepts supplied dialogue. It does not call a model or create prompts.

## License

[MIT](LICENSE) ([简体中文参考译文](LICENSE.zh-CN.md)). The English license is authoritative. Game and third-party dependencies retain their respective licenses.
