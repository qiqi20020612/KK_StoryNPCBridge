# KK_StoryNPCBridge

English | [简体中文](README.zh-CN.md)

A BepInEx 5 plugin that exposes native story-mode NPC state and control through a local API for Koikatu and Koikatsu Party. External applications can provide manual controls or AI-driven behavior. Model access, prompts, context management and decisions belong to the external application.

## Current scope

**The plugin currently injects one new bridge character. Only this injected character can be controlled.** Its actor ID is `bridge:main`, and its initial appearance uses the game's default female model, `ill_Default_Female`.

Existing story NPCs cannot currently be selected or taken over. The player is not a control target. When the bridge character is unclaimed, the game manages her with its native AI. Normal release restores native AI; an unexpected disconnect first reserves control for a bounded reconnection period.

## Planned NPC targeting

Future development will extend control to existing story NPCs and support targeting:

- One specific NPC.
- A selected set of NPCs.
- All NPCs.

These targeting modes are planned features, not capabilities of the current version.

## Features

- Read player names, native game periods, weekdays and explicit day-ending/day-start events.
- Read and modify native scores, relationships, club membership and promised-date flags in any scene, with actual native results returned.
- Adjust numeric attributes by signed deltas, clamp to native ranges and return before/actual values; stable mutation IDs prevent duplicate application on retries.
- Keep bridge progress independently for each story save; first creation starts with zero scores/counters and false exposed state flags.
- Control expressions and body animation during roaming and native conversations.
- Schedule native actions immediately or queue the next tasks.
- Observe native AI selections, reserved destinations and desire-weighted candidates; taking control returns the original planning snapshot before clearing native work.
- Independently sample another native girl's current task as an anonymous behavior reference, with reusable action requests.
- Walk to the player using native navigation, including cross-map routes.
- Use native ADV dialogue with user input, external-response waiting, NPC-initiated invitations and long replies paginated at the equivalent of 60 Chinese characters or 100 ASCII characters per page.
- Play ordered text/narration, expressions, loaded motions and delays in the bridge's ready native conversation through `play_adv_sequence`, with progress, cancellation and recovery.
- Control native blush, tear levels, gaze, highlights and facial opening limits, with live semantic presets such as `smile`, `shy` and `crying` resolved from the current model's morphs.
- Report native dialogue choices and distinguish touch from look while preserving original body reactions.
- Record displayed native ADV lines involving the bridge NPC, with speaker identities and conversation participants; unrelated conversations and voice-only reactions are excluded.
- Let the player open text input from a native conversation; external connectors explicitly show/hide a waiting indicator without injecting dialogue.
- Execute native confession, breakup, H, lunch, date, club activities/admission, study, exercise, follow and go-home effects independently through the API, with explicit relationship/membership requirements and no invitation/refusal roll.
- Return activity reports directly when taking control; movement history records map changes only.
- Discover English ID descriptions and reuse returned catalog entries, keys, numeric IDs or request templates in calls.
- Hold position after an unexpected disconnect and safely resume within a configurable grace period (60 seconds by default).

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
- `BepInEx/config/KK_StoryNPCBridge/profiles/<save-key>/`: bridge-character data and activity journal for each story save slot.

The profile key is derived from the native save filename. Each slot first initializes exposed scores/counters to `0`, flags to `false`, and the relationship to `stranger` (`-1`, not met). Later loads retain that slot's bridge progress. Profiles are sidecar data and should be backed up with the story saves. Earlier unscoped profile files are left untouched and are not imported into every save.

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
player
time
use 1
stats
stat relation=friend isStaff=true isDate=true favor=30
adjust demo-stats-1 favor=4 intimacy=-2 lewdness=4
summon
get
report
release
quit
```

`use 1` claims the injected bridge character and prints the complete handover report. `stat` sets absolute values; `adjust` adds deltas. Use a new mutation ID for a new change and reuse the ID/deltas after a timeout (see [incremental attribute changes](API.md#incremental-attribute-changes)). `summon` makes her walk near the player; it does not teleport. Keep the controller connected while tasks run. Normal Python client shutdown releases control immediately. Unexpected transport or heartbeat loss starts the reconnection grace period; use the private credentials returned by `claim` to resume (see the API reference).

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
pwsh -File tools/package-release.ps1 -Tag v1.4.0
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
- Releasing control restores native AI after any pending dialogue or scene cleanup. Unexpected disconnects hold position for `Bridge.ReconnectGraceSeconds` (default 60, range 5–600) before restoring native AI. Recovery applies to the same running story/generation, not a game restart.
- Runtime catalogs describe available references; occupied points, targets and story conditions can still prevent an action from starting.
- Expressions depend on the loaded character model. Refresh animation catalogs when entering or leaving a conversation; native talk uses its own Animator. Animator state hashes and native scheduler action IDs are different domains.
- The native game provides weekdays rather than an absolute calendar date. The plugin reports an observed day counter per save. Native ADV displays supplementary Unicode characters such as Emoji as `□` to avoid a Unity text error; API history/events retain the original text.
- Activity reports retain the latest 256 entries and include counts for omitted records. They do not record same-map walking trajectories.
- The bridge provides structured events and accepts supplied dialogue. It does not call a model or create prompts.

## License

[MIT](LICENSE). Game and third-party dependencies retain their respective licenses.
