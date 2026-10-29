# KK_StoryNPCBridge API 1.0.0

## Transport and authentication

Use UTF-8 newline-delimited JSON over IPv4 TCP loopback. The default port is `64321`; the port and authentication token are in `BepInEx/config/KK_StoryNPCBridge.cfg`. Never publish the token. The Python standard-library client in `tools/bridge_client.py` handles authentication, request IDs and heartbeats.

```json
{"id":"1","op":"auth","token":"<local token>"}
{"id":"2","op":"status"}
```

Success is `{ "id": "2", "ok": true, "result": ... }`. Errors are `{ "id": "2", "ok": false, "error": { "code": "...", "message": "English description" } }`. The protocol version is `2`; the plugin version is `1.0.0`.

Authentication and status replies identify the plugin with `pluginName="KK_StoryNPCBridge"` and `pluginGuid="KK_StoryNPCBridge"`. The single DLL installs directly as `BepInEx/plugins/KK_StoryNPCBridge.dll`.

There are at most four clients, 64 KiB per incoming command frame, eight Unity commands processed per frame and 32 queued native tasks. Send `ping` every three seconds. Controller disconnect or approximately 12 seconds without a heartbeat releases control. A queued command expires after five seconds before execution. Snapshot subscriptions default to 5 Hz, configurable from 1 to 20 Hz.

API descriptions, command explanations and plugin-generated summaries are English. Character names, supplied dialogue, native menu labels, `nativeName` and native asset identifiers are content from the caller or game; they are preserved, not translated. UI language does not change API descriptions, IDs or keys.

## Actor ownership

Only `bridge:main` is writable. Discover the current `generation` through `status`, `list` or `get`. Actor writes require `target`, `generation` and a successful `claim` by that connection. Stale generations and writes from another owner are rejected. Most writes require roaming; dialogue operations also support the owned character's native conversation. Scene loading can return `busy` or `not_ready`.

Unclaimed actors use native AI. `claim` cancels the autonomous task and freezes the activity report. `release`, `release_all` or controller loss restores native scheduling after scene/dialogue cleanup. `stop` keeps ownership. Do not treat `nativeAI=true` as proof of autonomy: it means the native executor exists; `nativeAutonomy` identifies who selects actions.

## Catalogs and reference inputs

`catalog` returns `actions`, `maps`, `expressions`, `animations`, `idDomains`, `ui`, `generation` and `descriptionLanguage="en"`. No ownership is needed. Catalogs are discovered from the loaded installation. Expressions and animations require the character to be loaded; their empty/unavailable catalogs indicate that the actor should first walk to the player.

Each discrete entry has an integer `id`, stable `key`, English `name` and English `description`. Native source names or exact morph targets are included where applicable. Entries with `request` / `queueRequest` contain command parameters with the current actor generation, ready to pass to `BridgeClient.request` after claiming control. The client adds the transport request ID; raw TCP clients must add their own `id`. Requests do not claim automatically. Refresh the catalog after loading another story or character model.

These forms are equivalent where the catalog entry is available:

```python
phone = next(x for x in catalog['actions'] if x['key'] == 'use_phone')
bridge.request('set_action', action=10, **p)
bridge.request('set_action', action='use_phone', **p)
bridge.request('set_action', action=phone, **p)
bridge.request(**phone['request'])
bridge.request(**phone['queueRequest'])
```

An object can contain `id`, `key`, or both. When both are present, they must refer to the same entry. Display names are descriptive and are not accepted as selectors. Unknown keys, mismatched references and expression entries from the wrong field are rejected before changing the affected state.

| Domain | Meaning | Input location | Important distinction |
| --- | --- | --- | --- |
| Scheduler action | Native action selection ID, e.g. `10 = Use phone` | `set_action.action`, `queue_action.action`, `set_desire.action` | Also used inside queued tasks; not an animation hash |
| Facial pattern | Current actor's eyes, eyebrow or mouth FBS index | `set_expression.changes.<field>` | Each facial field has its own ID space |
| Animator state | Signed `Animator.StringToHash` ID on layer 0, or its state key | `play_animation.state` | Visual playback only, with no scheduler side effects |
| Map | Native story/event map ID | Task `map`; development `game.map.map` | `-1` / `automatic` is valid only for task auto-selection |
| Native plan | Read-only native sub-plan value | `native.plansNo` / `plansName` | Preserve it for observation; it is not a separate callable task ID |

The reference tables are in [docs/REFERENCE.md](docs/REFERENCE.md). Runtime catalogs remain authoritative for models, installed content and availability.

### Expressions

`expression_catalog` returns arrays for `eyes`, `eyebrow`, `mouth`, plus continuous/boolean `controls`. English pattern names interpret the native morph target labels. `morphTargets` records the actual mesh and open/closed blend-shape names. Unknown custom patterns explicitly identify themselves as unlabeled rather than borrowing another model's expression meaning.

```python
eyes = catalog['expressions']['eyes'][2]  # Smiling eyes on the default model
bridge.request('set_expression', changes={
    'eyes': eyes,
    'eyebrow': catalog['expressions']['eyebrow'][0],
    'mouth': catalog['expressions']['mouth'][1],
    'eyesOpen': 1.0, 'mouthOpen': 0.3, 'blink': False,
}, **p)
```

`eyesOpen` and `mouthOpen` are numeric values in `[0,1]`; `blink` is boolean. `clear_expression` clears the override and returns expression management to the native game. The live actor includes numeric `expression`, descriptive `expressionDetails` and pattern-count `expressionLimits`.

### Native tasks and maps

`native_actions` includes names, native layer, desire, `mapsWithPoints`, descriptive `mapDetails`, and executable request templates. `map_catalog` includes map names and `canSchedule`. Event-only maps can be listed while being unavailable for roaming tasks. Automatic map selection has `id=-1` and `key="automatic"`.

```python
destination = next(x for x in catalog['maps'] if x['key'] == 'map/34')
result = bridge.request('set_action', action=phone, map=destination,
                        duration=30, **p)
queued = bridge.request('queue_action', action=phone, map=destination,
                        duration=0, **p)['queue'][-1]
bridge.request('clear_queue', **p)
bridge.request(**queued['request'])       # new queue task, new taskId
bridge.request('set_action', task=queued, **p)  # replay returned task directly
```

Queue entries carry `taskId`, `action`, `actionInfo`, `map`, `mapInfo`, `duration` and a replayable `request`. `set_action` and `queue_action` accept a returned entry under `task`; when `task` is present its action/map/duration fields are used. A replay creates a new task, not the original task identity. Duration `0` or omission uses the native duration; positive values up to 3600 are seconds after arrival. Conditions and point occupancy can still produce `action_unavailable` despite a valid ID.

### Animations

`animation_catalog` lists discovered native wait-point and common states that the loaded Animator confirms exist on layer 0. It is not a Studio animation database or a guaranteed exhaustive enumeration of every Unity state. A valid raw state string remains accepted. Each returned entry explains the state and contains its signed hash and an executable `play_animation` request.

```python
idle = next(x for x in catalog['animations'] if x['key'] == 'Idle')
bridge.request('play_animation', state=idle, **p)
bridge.request('play_animation', state=idle['id'], **p)
```

Playing `Reading` does not automatically find a seat, add a book or run action `9`. Use the scheduler action for native gameplay. Live animation state is described by `animationDetails`.

## Commands

Unless stated otherwise, actor commands take `target`; write commands also take `generation` and require control.

| Command | Parameters / result |
| --- | --- |
| `auth` | `token`; authenticate this connection |
| `ping` | Heartbeat; returns `pong=true` |
| `subscribe` | Enable snapshot pushes for this connection; close it to stop |
| `status` / `list` | World, player, one owned actor, events, English description language and configured UI language |
| `get` | Actor object, including full `activityReport` and descriptive current IDs |
| `catalog` | All runtime ID catalogs; no target or ownership needed |
| `native_actions` | Full native action directory for the actor |
| `map_catalog` | Story and event map references; no target needed |
| `expression_catalog` | Actor-specific facial pattern directory and controls |
| `animation_catalog` | Discovered playable Animator states |
| `claim` | Actor plus complete `activityReport` and identical `handoverReport`; idempotent for the same owner |
| `release` | Optional `restorePosition` boolean; restores native AI and returns actor/current interval |
| `release_all` | Releases this connection's ownership; reply directly includes the current `activityReport` |
| `activity_report` | Optional retained `reportId`; normal calls already include complete entries |
| `walk_to_player` / `summon` | Native walking request; observe `approach` for completion |
| `set_action` | `action`, optional `map`, `duration`; or a returned `task` |
| `queue_action` | Same task fields; append to the queue |
| `clear_queue` | Remove waiting tasks; returns native task state |
| `set_desire` | `action` reference and integer `value` from 0 to 100; native mutable desires only |
| `move` | World `position=[x,y,z]` on the loaded current map NavMesh |
| `teleport` | Explicit position operation; same `position` format |
| `turn` | `yaw` from -3600 to 3600 degrees |
| `stop` | Cancel current task/movement while retaining control |
| `set_stats` | `changes`: integer `favor`, `intimacy`, `anger` in `[0,100]`, boolean `isAnger` |
| `set_expression` / `clear_expression` | Facial references and controls described above / clear override |
| `play_animation` | `state` reference, raw state key or signed state hash |
| `native_talk` | Start the native talk scene; actual native dialogue/menu remains available |
| `say_native` | `text`; one native ADV line outside a managed conversation |
| `dialogue_open` | Optional `text`, `initiator='player'/'npc'`, `requestInput`, `timeoutSeconds`, `afterInteractionId` |
| `say` | `text`, optional `requestInput`, `afterInteractionId`; start/continue managed dialogue |
| `dialogue_status` | Full conversation state, request IDs and displayed history |
| `dialogue_reply` | User text: `conversationId`, `turn`, `text`; no ownership takeover needed |
| `dialogue_request_input` | `conversationId`; request user text when ready |
| `dialogue_wait` | `conversationId`; request an external response when ready |
| `dialogue_respond` | `conversationId`, `requestId`, `text`; submit an external response |
| `dialogue_fail` | `conversationId`, `requestId`, `error`; fail an external response request |
| `dialogue_retry` | `conversationId`; retry a failed/timed-out request with a new request ID |
| `dialogue_close` | `conversationId`; end managed dialogue and release ADV input blocking |
| `native_interaction_status` | Current/recent touch or look lifecycle and native reaction result |
| `panel_open` / `panel_close` | Development only: explicitly toggle the debug panel |

`model_reply`, `model_error` and `dialogue_wait_model` are aliases for `dialogue_respond`, `dialogue_fail` and `dialogue_wait`. No model SDK, prompt construction or external service is part of this project. `spawn` / `despawn` / `respawn` are rejected because native story loading owns the character lifecycle.

## Activity report reply

Actor-valued replies and snapshot actors include `activityReport`. While uncontrolled it is the live interval; after claiming it is the frozen report of that interval. `claim.handoverReport` is the same complete report. `entries` is supplied directly, without a second query.

| Field | Meaning |
| --- | --- |
| `reportId`, `generation`, `world` | Segment identity and source world |
| `reason` | Why native control began, such as `released` or `controller_disconnected` |
| `startedUtc`, `endedUtc` | ISO 8601 UTC; live intervals have null `endedUtc` |
| `durationSeconds` | Observed interval duration |
| `startState`, `endState` | Native action/map references, target, stats and conversation state; no movement coordinates |
| `entries` | Ordered records with `seq`, `utc`, `kind`, `data` |
| `entryCount`, `omittedEntries`, `counts` | Retained count, truncated count and per-kind totals |

Movement entries are `map_changed`: `fromMap`, `toMap`, `fromMapInfo`, `toMapInfo`. Same-map movement does not create trajectory or arrival records. Action entries carry English meanings and preserved native names. Touch/look event positions refer to the interaction hit location, not movement tracking. The most recent 256 records are retained; a report is not an unlimited historical archive.

## Dialogue states and structured events

`phase` includes `closed`, `inviting`, `opening`, `npc_speaking`, `awaiting_input`, `waiting_reply`, `reply_error`, `ready`, `native_reaction` and `ending`. `waitingReply`, `mouseBlocked`, `requestId`, `requestKind`, `conversationId`, `turn` and `errorCode` explain the current response workflow. The response also exposes `state=waiting_model/model_error` names; new clients can use `phase` for the response state.

`dialogue_request` contains identifiers, kind and the native menu command or user input. Native button choices have `userText=null`; the bridge does not invent a player utterance or package a prompt/history for the external application. Native menu labels remain source content. `commandId`, `category` and optional `eventId` retain native numbering; known invitation examples are `eventId=8` for studying and `eventId=11` for following. These are observed UI intents, not scheduler action IDs.

Touch/look events include `interactionId`, `kind`, `nativeMode`, `region`, voice/pose data and lifecycle. `nativeMode=0` means touch; `1` means look. Native voice, animation and counters execute normally. An external application may explicitly call `say` or `dialogue_open` with `afterInteractionId` once the reaction completes; no automatic dialogue request is created.

Other events include controller claims/releases, task queued/started/arrived/completed/failed/cancelled, dialogue cancellation and native invitation acceptance/cancellation. Events and snapshots contain sequence numbers. After reconnecting, refresh generation and request identifiers; never replay stale write commands blindly.

## Development helpers and limits

`Development.AllowGameCommands` gates `game.*` helpers: story load, map changes, story advancement/continuation, screenshots, save-export probes, native menu/pointer probes and native body interaction probes. `game.map` accepts a returned map object/key/ID but changes the player map and is a development tool, not the NPC walking interface.

Examples of errors are `unauthorized`, `invalid_request`, `invalid_argument`, `not_plugin_actor`, `not_owner`, `owned`, `generation_required`, `stale_generation`, `not_loaded`, `not_ready`, `unknown_reference`, `reference_mismatch`, `unknown_animation`, `unknown_map`, `action_unavailable`, `native_interaction_busy`, `stale_interaction` and dialogue request/context errors. Check the error code and current state before retrying mutations.
