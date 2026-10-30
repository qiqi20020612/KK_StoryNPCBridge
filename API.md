# KK_StoryNPCBridge API 1.3.0

English | [简体中文](API.zh-CN.md)

## Transport and authentication

Use UTF-8 newline-delimited JSON over IPv4 TCP loopback. The default port is `64321`; the port and authentication token are in `BepInEx/config/KK_StoryNPCBridge.cfg`. Never publish the token. The Python standard-library client in `tools/bridge_client.py` handles authentication, request IDs and heartbeats.

```json
{"id":"1","op":"auth","token":"<local token>"}
{"id":"2","op":"status"}
```

Success is `{ "id": "2", "ok": true, "result": ... }`. Errors are `{ "id": "2", "ok": false, "error": { "code": "...", "message": "English description" } }`. The protocol version is `2`; the plugin version is `1.3.0`.

Authentication and status replies identify the plugin with `pluginName="KK_StoryNPCBridge"` and `pluginGuid="KK_StoryNPCBridge"`. The single DLL installs directly as `BepInEx/plugins/KK_StoryNPCBridge.dll`.

There are at most four clients, 64 KiB per incoming command frame, eight Unity commands processed per frame and 32 queued native tasks. Send `ping` every three seconds. Transport loss or approximately 12 seconds without a heartbeat starts the recovery grace period described below. A queued command expires after five seconds before execution. Snapshot subscriptions default to 5 Hz, configurable from 1 to 20 Hz.

API descriptions, command explanations and plugin-generated summaries are English. Character names, supplied dialogue, native menu labels, `nativeName` and native asset identifiers are content from the caller or game; they are preserved, not translated. UI language does not change API descriptions, IDs or keys.

## Actor ownership

Only `bridge:main` is writable. Discover the current `generation` through `status`, `list` or `get`. Actor writes require `target`, `generation` and a successful `claim` by that connection. Stale generations and writes from another owner are rejected. `claim`, `release`, `set_stats` and `adjust_stats` work for a loaded story regardless of scene, map transitions or model availability. Navigation and scheduler writes require roaming; expressions and animation also support the owned character's native conversation. Other scene-dependent operations can return `busy` or `not_ready`.

Unclaimed actors use native AI. `claim` cancels the autonomous task and freezes the activity report. `release`, `release_all` or normal `disconnect` restores native scheduling after scene/dialogue cleanup. Unexpected controller loss first holds position for recovery; native scheduling resumes if that reservation expires. `stop` keeps ownership. Do not treat `nativeAI=true` as proof of autonomy: it means the native executor exists; `nativeAutonomy` identifies who selects actions.

### Unexpected disconnect and recovery

`claim` and `resume` replies include a private `resumeToken`, `world`, current actor generation and the complete handover report. Store these credentials in the controller; they are absent from snapshots, events and profile files. Authentication is still required on every new connection.

```python
claimed = bridge.request('claim', **p)
resume_token = claimed['resumeToken']
# After an unexpected connection loss, create/authenticate a new BridgeClient.
recovered = replacement.request('resume', resumeToken=resume_token, **p)
resume_token = recovered['resumeToken']  # Rotated; the old token is invalid.
```

On transport/heartbeat loss, the actor enters `recovery.state='waiting_reconnect'`, cancels navigation/queued tasks, holds position and preserves the active conversation and pending request IDs. Other connections cannot claim the reserved actor. `Bridge.ReconnectGraceSeconds` in the cfg defaults to `60` and is clamped to `5–600` seconds. `recovery` reports `deadlineUtc`, `remainingSeconds`, `graceSeconds` and `interruptedTaskState`. Resume requires the same running story, generation, valid token and an unexpired reservation. It rotates the token and pauses conversation/request timeouts for the disconnected interval. Interrupted actions require an explicit new command; they are not replayed automatically.

During grace, ADV interaction remains blocked except ending the conversation. No reconnection text is inserted into native dialogue. The optional developer panel shows connection status. Expiry emits `controller_recovery_expired`, releases control with reason `reconnect_timeout` and restores native AI after cleanup. Story changes or a game restart invalidate recovery.

Normal termination must send `release`, `release_all` or `disconnect` before closing TCP. `disconnect` acknowledges release and closes the connection; no target is needed. A bare socket close cannot express intent and is treated as unexpected. `BridgeClient.close()` and context-manager exit send `disconnect` by default. `close(graceful=False)` represents transport loss; the library does not implement automatic reconnection or model calls.

### Per-save bridge state

`actor.profile` identifies `save`, SHA-256 `key`, `scope='story_save_slot'` and `initializedThisLoad`. The key derives from the native save filename, case-insensitively. Sidecars live in `BepInEx/config/KK_StoryNPCBridge/profiles/<key>/`: `owned-actor.json`, `owned-native.bin` and `activity-journal.json`. First creation for a slot starts with scores/counters `0`, exposed flags `false`, `relation=-1`; subsequent loads retain that slot's bridge progress. This initialization does not clear the game's internal identity, card or scheduling setup. A successful native Save As branches the current bridge progress into the destination slot. Back up these sidecars with the native saves. Previous unscoped files remain untouched and are not automatically imported into each slot.

## Catalogs and reference inputs

`catalog` returns `actions`, `maps`, `expressions`, `animations`, `stats`, `time`, `nativeEvents`, `idDomains`, `ui`, `generation` and `descriptionLanguage="en"`. No ownership is needed. Catalogs are discovered from the loaded installation. Expressions and animations require an active model; check `visualAvailable` and `visualContext` (`roaming` or `native_talk`). Refresh visual catalogs when entering or leaving native talk, which uses a separate model/Animator.

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
| Relationship | Native derived relationship, -1 through 2 | `set_stats.changes.relation` | Accepts a returned relationship object, key or ID |
| Game time | Native stages, UI periods and weekdays | `gameTime`, `time_catalog` | Observations; there is no production command to change the player clock |
| Native activity event | Native conversation event ID | `trigger_native_event.event` | Direct effect; separate from scheduler IDs and `commandId=eventId+10` |

The reference tables are in [docs/REFERENCE.md](docs/REFERENCE.md) ([简体中文](docs/REFERENCE.zh-CN.md)). Runtime catalogs remain authoritative for models, installed content and availability.

### Expressions

`expression_catalog` returns arrays for `eyes`, `eyebrow`, `mouth`, plus continuous/boolean `controls`. English pattern names interpret the native morph target labels. `morphTargets` records the actual mesh and open/closed blend-shape names. Unknown custom patterns explicitly identify themselves as unlabeled rather than borrowing another model's expression meaning.

```python
eyes = catalog['expressions']['eyes'][2]  # Smiling eyes on the default model
bridge.request('set_expression', changes={
    'eyes': eyes,
    'eyebrow': catalog['expressions']['eyebrow'][0],
    'mouth': catalog['expressions']['mouth'][1],
    'eyesOpen': 1.0, 'blink': False,
}, **p)
```

`eyesOpen` is numeric in `[0,1]`; `blink` is boolean. Mouth opening is controlled by the native game: `expression.mouthOpen` is read-only and is not included in writable `controls`. Sending `mouthOpen` in `set_expression.changes` is rejected with `unsupported_field`; a mixed change containing it is rejected before applying any fields. Previously stored mouth-opening values are ignored. Mouth pattern selection (`mouth`) remains writable. The plugin does not write mouth-opening limits or pin the opening rate when applying expressions. `clear_expression` clears the override and returns expression management to the native game. The live actor includes numeric `expression`, descriptive `expressionDetails` and pattern-count `expressionLimits`.

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

### Native AI planning observations

`native_plan` requires only `target="bridge:main"`, with no ownership or generation requirement. It returns `live` and `beforeControl`; the same object is included as `actor.nativePlanning`. Each snapshot includes `generation`, `world`, `capturedUtc`, `gameTime`, `selection`, `available` and `unavailableReason`. Before the scheduler exists, `available=false` and the action/candidate arrays are empty.

On the first successful `claim`, `handoverReport.nativePlan` (also `activityReport.nativePlan`) contains the native AI snapshot **captured before cleanup**. It is returned in that call, without a second query. Claim then cancels the native selected action, releases its reserved point/partner and clears priority-selection memory and plugin tasks. Desire values, character attributes and recent-action history are preserved. Native idle takes over immediately in roaming, or on return from a scene. Repeated `claim` by the same owner and authenticated `resume` retain the original snapshot; releasing and claiming again captures a new one. `beforeControl` is that retained snapshot for the current generation, or `null` if none exists. It is not an instruction to replay interrupted work.

The native AI selects one action at a time, so `futureQueueAvailable=false`. It does not maintain a FIFO schedule of all future tasks. The observed fields are:

| Field | Meaning |
| --- | --- |
| `plannedActions` | Currently selected action, usually zero or one entry: `action`, `actionInfo`, `source`, `isPriority`, `destination`, `partner`, `plansNo` and `plansName`. During control this describes the plugin-selected native executor state. |
| `reservedDestination` | Actual reserved wait point with map reference and position, or `null`. This is a reservation snapshot, not a movement-history event. |
| `candidates` | All native desire entries with action references, `desire`, `isPriority`, native `priority`, `selectionKind`, `weight`, `priorityThresholdMet`, `eligibility`, and executable `request` / `queueRequest`. |
| `recentActions` | Native recent-action history in oldest-first order; **not future tasks**. |
| `recentPrioritySelections` | Native priority-selection memory, used to suppress repetition; **not future tasks**. |
| `deferredPriorityActions` | Native deferred-priority markers sorted by action ID; no execution order is implied. |

Candidates are grouped by priority, then sorted by descending desire and native priority, with ID as the final tie-breaker. Priority actions normally require desire at least `100`; generic actions use native base probability plus desire as their current `weight`. `selectionKind` is `priority`, `weighted_random` or `special`; `weight=null` when no generic probability applies. This ordering is descriptive, not a predicted execution sequence. `eligibility="not_evaluated"`: the observer does not grow desires, consume random numbers, invoke selection, reserve points or promise that a target/map is available. Time, clothing, excluded actions, target/point occupancy and later desire changes can alter the next native choice.

```python
observed = bridge.request('native_plan', target='bridge:main')
claimed = bridge.request('claim', **p)
original_plan = claimed['handoverReport']['nativePlan']
candidate = next(x for x in original_plan['candidates'] if x['key'] == 'use_phone')
bridge.request(**candidate['request'])  # Explicit choice; native conditions still apply.
```

Refresh returned requests after a generation change. Scheduler action IDs/keys use the existing action catalog; `plansNo` remains an observational sub-plan value, not a schedulable ID. The manual shell command `plan` works before and after claiming.

### Sample another native NPC's current task

`sample_native_action` takes no parameters beyond `op` (and the transport request ID). Authentication is required; target, generation and ownership are not required. Each call independently and uniformly draws one distinct, initialized native girl from the current story with an AI result and a nonnegative action ID. The bridge NPC is always excluded, including when unclaimed. Other maps are included; characters without a current native task are skipped. Consecutive calls may select the same NPC. Sampling uses a separate random generator and does not advance AI, change native queues/reservations or consume the game's random state. It is an explicit request, not a subscription sample.

The reply includes `available`, `unavailableReason` and `task`. When available, `task` contains `action`, descriptive `actionInfo`, destination `map` / `mapInfo`, `duration=0`, and executable `request` / `queueRequest` targeting the bridge. No source character ID, name, partner, appearance, attributes or coordinates are returned. `plansNo` / `plansName` describe the current native sub-plan; `arrival` and `elapsedSeconds` describe execution. Action and map IDs use the existing catalogs. A missing native destination is `map=-1` (`automatic`). Replay uses the bridge's native duration, not the source NPC's elapsed or remaining time, and still requires ownership, a fresh generation and suitable native conditions. No target or partner is copied; replay may fail even though the source NPC can perform the action.

```python
sample = bridge.request('sample_native_action')
if sample['available']:
    bridge.request('queue_action', task=sample['task'], **p)
    # Alternatively: bridge.request(**sample['task']['request']) for immediate execution.
```

When unavailable, `task=null` and `unavailableReason` is `story_not_ready`, `scene_busy` or `no_eligible_native_npc`. A ready conversation allows observation of other NPCs; navigation/action writes still obey their existing scene restrictions. The manual shell command is `sample_action` and also works before claiming.

### Animations

`animation_catalog` lists native wait-point, talk motion, controller-clip and common states that the active Animator confirms exist on layer 0. It is not a Studio animation database or an exhaustive enumeration of every Unity state. A valid raw state string remains accepted. Each entry includes its `context`, English explanation, signed hash and executable `play_animation` request. Both visual commands address the visible talk model during an owned conversation.

```python
idle = bridge.request('animation_catalog', **p)[0]  # Discover in the current context.
bridge.request('play_animation', state=idle, **p)
bridge.request('play_animation', state=idle['id'], **p)
```

Playing `Reading` does not automatically find a seat, add a book or run action `9`. Use the scheduler action for native gameplay. Live animation state is described by `animationDetails`; `animation.state`/hashes report actual playback, while `requestedState` identifies an explicit override.

## Native attributes and relationships

`stat_catalog` (also `catalog.stats`) returns field types, write ranges, `adjustable`, English meanings, `relationships` and the `adjustment` policy. `actor.stats` contains the actual native values and `actor.relationship` their descriptive relation. Native effects may produce values outside the API write ranges (for example negative anger); observations and saved bridge progress preserve those values. `set_stats.changes` validates all fields before changing state, writes native state, reads it back and returns the complete actor. `stats_changed` carries `requested`, `before` and `actual`. Both stat operations require the current story actor, ownership and generation, with no roaming/ADV/map/model restriction.

| Field | Type / range | Meaning |
| --- | --- | --- |
| `favor`, `intimacy` | integer 0–100 | Affection and intimacy toward the player |
| `anger`, `lewdness` | integer 0–100 | Native anger and lewdness scores |
| `hCount` | integer 0–2147483647 | Native intimate encounter count |
| `nickNameTalkCount` | integer 0–2147483647 | Nickname conversation count |
| `myRoomCount` | integer 0–2147483647 | Player-room visit count |
| `eventAfterDay` | integer 0–2147483647 | Native days-after-event counter |
| `hasMet`, `isFriend` | boolean | Meeting markers (talk events 0/1) and friendship marker (2) |
| `isGirlfriend`, `isStaff`, `isDate` | boolean | Girlfriend, Koikatsu club member, promised date |
| `isAnger`, `confessed`, `isFirstGirlfriend` | boolean | Angry, confession and first-girlfriend native flags |
| `isTaked`, `isLunch`, `isTalkPoint`, `isKiss` | boolean | Independently preserved native flags with these names |
| `isVirgin`, `isAnalVirgin` | boolean | Native vaginal/anal virginity flags |
| `relation` | reference | -1 `stranger` (not met), 0 `acquaintance`, 1 `friend`, 2 `girlfriend` |

Setting `relation` updates meeting, friendship and girlfriend flags together. Explicit conflicting flags are rejected with `reference_mismatch`. Individual flags can also be written independently; the returned relationship uses the game's native precedence. Writing `anger` alone also derives `isAnger = anger > 0`; provide `isAnger` explicitly to choose otherwise. Flags and scores do not run the corresponding story event or guarantee that an invitation is currently possible.

```python
friend = next(x for x in bridge.request('stat_catalog')['relationships']
              if x['key'] == 'friend')
actual = bridge.request('set_stats', changes={
    'relation': friend, 'favor': 30, 'intimacy': 20,
    'isStaff': True, 'isDate': True,
}, **p)
assert actual['stats']['relation'] == 1
```

### Incremental attribute changes

`adjust_stats` takes a nonempty `deltas` object and a caller-generated `mutationId`. Only the eight integer score/counter fields in the table support adjustment (`adjustable=true`); relationships and boolean flags remain absolute writes through `set_stats`. Each delta must be an integer from -2147483648 through 2147483647; booleans, strings, fractions and unknown fields are rejected before any write. Use field `key` values returned by `stat_catalog` as `deltas` keys.

The operation reads current native values, adds each delta using 64-bit arithmetic, and clamps the resulting value to that field's write range: 0–100 for scores, 0–2147483647 for counters. Clamping applies even if the observed starting value is outside that range. It reuses `set_stats` validation/normalization, native synchronization, sidecar persistence and whole-batch failure rollback, including talk-event markers. All fields are calculated from one snapshot and committed synchronously on the Unity thread. Persistence of restored sidecars remains best effort on I/O failure, as with `set_stats`.

`set_stats` continues to mean absolute assignment and rejects out-of-range values. Adjusting `anger` retains the existing `isAnger = actual anger > 0` derivation. Adjustments do not score emotions, change relationship rules, or convert saturated favor into intimacy.

```python
# Generate/store this ID once for this logical change; keep it unchanged on retries.
change = {'mutationId': 'chat-42-stats',
          'deltas': {'favor': 4, 'intimacy': -2, 'lewdness': 4}}
result = bridge.request('adjust_stats', **change, **p)
print(result['before']['favor'], result['actual']['favor'])
retry = bridge.request('adjust_stats', **change, **p)
assert retry['replayed'] is True  # Original outcome; no second increment.
```

The reply is an adjustment report, rather than the complete actor:

| Field | Meaning |
| --- | --- |
| `operation`, `mutationId`, `target`, `generation` | `adjust_stats` and the logical change/actor identity |
| `requestedDeltas` | Validated signed deltas supplied by the caller |
| `requested` | Normalized absolute values sent to the shared writer, including derived `isAnger` if applicable |
| `before`, `actual` | Complete native stat snapshots immediately before and after this commit |
| `appliedDeltas` | Actual `actual - before` for each requested numeric field, after clamping |
| `clampedFields` | Field keys whose calculated sum was limited to its legal range |
| `replayed` | `false` for the first success, `true` when returning its cached result |

For example, starting at `favor=98`, `intimacy=1`, `lewdness=100`, the deltas above produce `100`, `0`, `100`; `appliedDeltas` is `2`, `-1`, `0`, and all three keys appear in `clampedFields`. These are illustrative values, not an emotion-rating rule. A first success emits one `stats_changed` with the report fields except `replayed`; a retry emits no new mutation event. Cached snapshots describe the original operation; use `get` for current state after later changes.

`mutationId` is a case-sensitive, nonblank string of at most 128 characters, distinct from the TCP request `id`. A UUID is suitable. Reusing it with the same normalized field/delta pairs returns the original outcome, regardless of field order, later writes, normal release/reclaim or authenticated `resume`. Reusing it with different deltas returns `adjustment_conflict`. Ownership, current story and generation are still checked before replay. Keep the ID and deltas when retrying a timeout; after reconnecting, resume or claim control first. The client does not generate replacement IDs or retry automatically.

Records are kept in memory for the actor generation, not across story recreation/game restart; those produce a new generation and reject old commands. Up to 4096 execution attempts are retained without eviction. When full, new IDs return `adjustment_limit` before any write; recorded IDs remain queryable. Input-validation failures are not recorded. Once execution starts, a failure is recorded as `adjustment_failed`; that ID will return the same error without executing again. Inspect `get` and resolve the failure before deliberately issuing a new ID. This also prevents automatic reapplication after an uncertain rollback/persistence failure.

## Player and native game time

`status.player` supplies `name`, `lastName`, `firstName`, `nickname`, plus `loaded`, `map`, `mapInfo` and `position`. Names remain available from the loaded story when the player model is absent; player control is outside this API.

`status.gameTime` reports `available`, `stage`, `period`, `day`, `timerSeconds` and an English description. `time_catalog` (also `catalog.time`) describes all stage/period/weekday IDs. A stage is the exact native cycle phase; a period is the coarser native UI grouping. Full mappings are in the [ID reference](docs/REFERENCE.md).

`day` contains `id`, `observedDayIndex`, `weekday`, `calendarDate=null`, `dateSource='native_weekday_with_observed_day_counter'`, `isDayEnding` and `isNewDay`. The game provides a weekday, not an absolute year/month/day. The per-slot counter increments only for calendar advances observed while the plugin is running; it is not a historical total of days played. `day.id` combines the current world and that counter. `isNewDay` marks the native calendar advance/wake-up phase, and `isDayEnding` marks the night phase before the next day.

| Global event | Trigger / payload |
| --- | --- |
| `game_time_initialized` | Ready story baseline; complete `gameTime` |
| `game_stage_changed` | Exact native cycle change; `from`, `to`, `time`, `world` |
| `game_time_period_changed` | UI period change; `from`, `to`, `time`, `world` |
| `game_day_ending` | Entered native night; `day`, `reason='native_night_entered'`, `world` |
| `game_day_ended` | Native calendar actually advanced; previous `day`, `nextWeekday`, `daysAdvanced`, `reason`, `world` |
| `game_day_started` | Follows day-ended; new `day`, `previousWeekday`, `daysAdvanced`, `world` |

These events have `target=null` and `generation=null`. Initial loading establishes a baseline instead of announcing a completed day. Entering night and actually advancing the calendar are separate events. `available=false`/`day=null` indicates that a ready story baseline does not yet exist.

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
| `native_plan` | Read-only live native planning and pre-control snapshot; no ownership needed |
| `sample_native_action` | Independently sample another native girl's current task, anonymously; no target/generation/ownership needed |
| `map_catalog` | Story and event map references; no target needed |
| `expression_catalog` | Actor-specific facial pattern directory and controls |
| `animation_catalog` | Discovered playable Animator states |
| `stat_catalog` / `time_catalog` | Native attribute/relationship definitions / stage, period and weekday references |
| `native_event_catalog` | Direct native activity effects, availability and executable references; no target needed |
| `claim` | Actor plus complete `activityReport`, identical `handoverReport`, private `resumeToken` and `world`; idempotent for the same owner |
| `resume` | `resumeToken`, current target/generation; recover reserved ownership and rotate token |
| `release` | Optional `restorePosition` boolean; restores native AI and returns actor/current interval |
| `release_all` | Releases this connection's ownership; reply directly includes the current `activityReport` |
| `disconnect` | Normal close: release ownership, acknowledge and close this connection; no target needed |
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
| `set_stats` | `changes`: fields/references in the attribute table; returns actual native state in every scene |
| `adjust_stats` | Stable `mutationId`, integer `deltas`; atomically add/clamp numeric fields and return before/actual values with replay protection |
| `set_expression` / `clear_expression` | Facial references and controls described above / clear override |
| `play_animation` | `state` reference, raw state key or signed state hash |
| `native_talk` | Start the native talk scene; actual native dialogue/menu remains available |
| `trigger_native_event` | `event` object/key/ID; execute a native activity effect directly; returns execution state, actual stats and follow binding |
| `set_waiting_indicator` | Boolean `visible`; show/hide the essential ADV waiting notice; no conversation/request ID needed |
| `say_native` | `text`; paginated native ADV reply outside a managed conversation; up to 1000 UTF-16 units |
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
| `nativePlan` | Added on claim: native AI selection, reservation and candidate snapshot captured before control cleanup |
| `entryCount`, `omittedEntries`, `counts` | Retained count, truncated count and per-kind totals |

Movement entries are `map_changed`: `fromMap`, `toMap`, `fromMapInfo`, `toMapInfo`. Same-map movement does not create trajectory or arrival records. Action entries carry English meanings and preserved native names. Touch/look event positions refer to the interaction hit location, not movement tracking. The most recent 256 records are retained; a report is not an unlimited historical archive.

## Dialogue states and structured events

### Player input and waiting notice

During a ready native conversation with the claimed bridge NPC, **Write a message** opens text input even when no managed dialogue exists. Opening the input creates a managed conversation, but requests an external response only after submission. The existing `dialogue_reply` API and input UI share the same submission path.

The owner explicitly controls the notice with `set_waiting_indicator`:

```python
bridge.request('set_waiting_indicator', visible=True, **p)
# External connector work happens outside the plugin.
bridge.request('set_waiting_indicator', visible=False, **p)
```

Showing it requires the bridge NPC's native conversation or its pending opening. It creates no utterance, model request or prompt. `dialogue.ui` returns `playerInputAvailable`, `waitingIndicatorRequested` and `waitingIndicatorVisible`. While visible, native ADV input is blocked except **End conversation**. Hiding it does not clear a pending reply request or recovery lock. It clears when the conversation ends, ownership is released or the story changes; during a recovery grace period it is hidden and can reappear after a valid resume. The notice is UI only and is never a `game_dialogue` line.

### Displayed native dialogue records

`game_dialogue` records actual native ADV text commands only after their message window is shown. Scope is the native conversation's participants: a conversation involving the bridge NPC retains its displayed context, including bridge-to-player, player-to-bridge and bridge-to-other-NPC lines and related narration. Player conversations with unrelated NPCs are excluded as a whole. Voice-only touch/look reactions and non-ADV text updates are excluded. Recording also runs while the bridge NPC uses native AI; those related lines enter the uncontrolled activity report as `dialogue` records.

| Field | Meaning |
| --- | --- |
| `speaker`, `text` | Native display name and displayed text; supplied long replies appear as displayed pages |
| `speakerActor` | Speaker `id`, `role`, `name`, identity source and native class/seat when resolved |
| `conversationId` | Native host identity; stable across ADV scripts in one talk scene, or scoped to one standalone ADV episode |
| `dialogueConversationId` | Optional managed workflow ID; null when no managed dialogue is active |
| `conversation` | Native `id`, `kind` (`native_talk` / `native_adv_event`), actual participant references and optional managed ID |
| `source`, `nativeLine`, `gameTime` | `native_adv_text`, native script line index and observed game time |
| `bundle`, `asset` | Native script source identifiers when available |

Speaker roles are `bridge`, `player`, `npc`, `narrator` or `unknown`. IDs are `bridge:main`, `player:main` and session-local `native:<world>:<roster-index>` for other NPCs. Other NPC references are observational and do not grant control. Native character parameters and actual runtime character references take precedence over names. Ambiguous literal names return `role='unknown'`, null `id` and `candidates`; the plugin does not guess between same-name characters. `actor.nativeConversation` retains the most recently recorded native conversation. These records are observations, separate from managed request history and complete supplied reply events.

### Managed dialogue workflow

`phase` includes `closed`, `inviting`, `opening`, `npc_speaking`, `user_speaking`, `awaiting_input`, `waiting_reply`, `reply_error`, `ready`, `native_reaction`, `native_event` and `ending`. `native_event` marks the transition from a managed conversation into an activity. `waitingReply`, `mouseBlocked`, `requestId`, `requestKind`, `conversationId`, `turn` and `errorCode` explain the current response workflow. The response also exposes `state=waiting_model/model_error` names; new clients can use `phase` for the response state.

`dialogue_request` contains identifiers, kind and the native menu command or user input. Native button choices have `userText=null`; the bridge does not invent a player utterance or package a prompt/history for the external application. Native menu labels remain source content. `commandId`, `category` and optional `eventId` retain native numbering; known invitation examples are `eventId=8` for studying and `eventId=11` for following. These are observed UI intents, not scheduler action IDs.

Touch/look events include `interactionId`, `kind`, `nativeMode`, `region`, voice/pose data and lifecycle. `nativeMode=0` means touch; `1` means look. Native voice, animation and counters execute normally. An external application may explicitly call `say` or `dialogue_open` with `afterInteractionId` once the reaction completes; no automatic dialogue request is created.

Supplied replies are displayed through native ADV with a page budget equivalent to **60 Chinese characters or 100 ASCII characters**. Each Unicode text element beginning with ASCII costs 3 width units; every other text element costs 5. ASCII letters, digits, spaces and punctuation therefore cost 3; Chinese characters, full-width punctuation and other non-ASCII elements cost 5. Mixed text shares the 300-unit budget (for example, 30 Chinese characters plus 50 ASCII letters). A text element is moved to the next page if adding it would exceed the budget; combining sequences and surrogate pairs are not split by this pagination step. Line breaks also end a page. Managed replies accept up to 4000 UTF-16 units. The full original reply appears once in conversation history and `npc_dialogue`, regardless of the page count.

`dialogue.pagination` reports `page`, `pageCount`, `maxCharacters=100` (the absolute text-element ceiling), `maxCjkCharacters=60`, `maxWidthUnits=300`, `countUnit='weighted_text_element'`, `asciiWidthUnits=3`, `nonAsciiWidthUnits=5` and `nonBmpFallbacks`. Each `dialogue_page` event includes the same limit/counting metadata, plus `conversationId`, `turn`, one-based `page`, `pageCount`, original page `text` and actual `displayText`. These are per-page display limits; they do not reduce the maximum length of the complete reply. Native word wrapping can still overflow a line for unusually long ASCII tokens without spaces.

Unity 5's native typewriter cannot safely measure supplementary Unicode characters (for example Emoji). Only native presentation substitutes each such character with `□`; API history and original page text are preserved. `nonBmpFallbacks` reports the number of substitutions in the reply. Invalid unpaired UTF-16 surrogates are rejected before displaying text.

Other events include controller claims/releases, task queued/started/arrived/completed/failed/cancelled, dialogue cancellation and native invitation acceptance/cancellation. Events and snapshots contain sequence numbers. After reconnecting, refresh generation and request identifiers; never replay stale write commands blindly.

## Direct native activity effects

`native_event_catalog` and `catalog.nativeEvents` list supported effects. `trigger_native_event` accepts a returned entry, its key or numeric ID. It requires ownership and the current generation, but no `conversationId`, `requestId`, prior chat or invitation/acceptance roll. The target must be loaded on the current map in roaming or its own native talk scene. Check each entry's `available` and `unavailableReason` immediately before calling. Entries include `supported=true`, English `description`, native `commandId`, `result`, executable `request`, `executionKind` (`state_effect` or `native_scene_flow`) and structured `requirements`.

```python
event = next(x for x in bridge.request('native_event_catalog') if x['key'] == 'follow_player')
execution = bridge.request('trigger_native_event', event=event, **p)
# For activities, observe actor.nativeEvent or native_event_* lifecycle events.
```

The event list includes the following effects. Friend or higher means the native relationship is `friend` (`1`) or `girlfriend` (`2`); it is not an affection-score threshold.

| Event ID / key | Relationship or membership requirement | Effect |
| --- | --- | --- |
| `1 / confess` | Friend or girlfriend | Establish the native girlfriend relationship and markers; update the first-girlfriend ranking and player counter. Already dating is idempotent. |
| `2 / break_up` | Girlfriend only | End the relationship and date promise, clear this NPC's calendar booking, set native breakup anger (`anger=100`, `isAnger=true`, `talkTime=0`) and run the native breakup/escape flow. |
| `3 / invite_h` | Friend or girlfriend | Enter the native H flow at an eligible native location. The host checks location eligibility again before applying the effect. |
| `4 / eat_lunch` | Friend or girlfriend | Enter native lunch; also requires lunch stage `5` and `isLunch=false`. |
| `5 / club_activity` | Club member only | Enter native club activities during staff stage `8`. |
| `7 / invite_date` | Friend or girlfriend | Set the date promise and native calendar/save identity. A date booked with another NPC is rejected. |
| `8 / study_together` | No relationship/membership restriction | Enter native study during free stage `5`, `8` or `9`. |
| `9 / exercise_together` | No relationship/membership restriction | Enter native exercise during free stage `5`, `8` or `9`. |
| `10 / invite_club` | Nonmember only | Set native membership and its marker; update the membership ranking and additive club report. |
| `11 / follow_player` | No relationship/membership restriction | Establish native following. |

The existing `6 / go_home_together` effect remains available at after-school stage `9`. Exact command IDs and native results are in the [ID reference](docs/REFERENCE.md#native-activity-effects). `requirements` exposes `minimumRelation` / `exactRelation` references (or `null`), `clubMember` (`true`, `false`, or unrestricted `null`), permitted `stages` (empty means no extra stage restriction), `sameMap`, `nativeHLocation`, `unusedLunch` and `unoccupiedDateSlot`. These are effect prerequisites, not a second acceptance roll. H location checks may finish in the internally prepared host when called from roaming. Unavailability reasons include `requires_friend_or_girlfriend`, `requires_girlfriend`, `requires_club_membership`, `already_club_member`, `another_date_scheduled`, `requires_native_h_location`, and the existing scene/time/lunch reasons. Pending scene effects recheck prerequisites before dispatch.

Confession, date booking and club admission are immediate `state_effect` operations. They do not open a talk scene, consume an invitation turn, cancel a pending external response or interrupt navigation/dialogue. The reply is completed with actual state. Direct `set_stats` writes remain available, but writing a relation/date/member flag does not execute the associated event's calendar or ranking/report effects.

Follow establishes the game's native player-follow relation; `stop` or a new task clears that relation. Normal release hands an active follow to the game; claiming again cancels it with the autonomous activity. Breakup, H and activity effects enter the game's native scene flow; native activity ADV, stat changes, maps and time progression still occur where applicable. Internal host preparation omits ordinary greetings and invitation menus. Scene-flow effects cancel an existing managed reply request with reason `native_event`; they do not request a model response. Finish any native choice or touch/look reaction first. No additional relationship gate is imposed on study, exercise, follow or the existing go-home effect.

Replies and `actor.nativeEvent` contain `executionId`, event reference, `state` (`opening`, `preparing`, `running`, `completed`, `failed`, `cancelled`), `error`, `effectApplied`, numeric `nativeResult`, actual `followingPlayer`, `generation`, `world` and current `stats`. `before` and `actual` report native effect state before dispatch and at application/completion: `stats`, descriptive `relationship`, `dateScheduled` for this NPC, native `girlfriendCount`, `staffCount`, `playerGirlfriendedCount` and `clubReportStaffAdd`. `actual` is `null` until application or termination; `stats` remains the live readback. Native result `-1 / continue_talk` means a state-only effect has no terminal scene result; it is unrelated to scheduler action `-1`.

Lifecycle events are `native_event_requested`, `native_event_started`, `native_event_completed`, `native_event_failed` and `native_event_cancelled`. Completion means the state effect is applied or the dispatched native flow has ended; follow remains active until stopped or handed back to the game. While a scene flow runs, other action/dialogue writes return `native_event_busy`; `set_stats`, `adjust_stats`, observation and release remain available. Cancellation before effect application prevents dispatch; once applied, native execution is allowed to finish and is not rolled back or automatically replayed after reconnecting.

## Development helpers and limits

`Development.AllowGameCommands` gates `game.*` helpers: story load, map changes, story advancement/continuation, screenshots, save-export probes, native menu/pointer probes and native body interaction probes. `game.map` accepts a returned map object/key/ID but changes the player map and is a development tool, not the NPC walking interface.

Examples of errors are `unauthorized`, `invalid_request`, `invalid_argument`, `not_plugin_actor`, `not_owner`, `owned`, `generation_required`, `stale_generation`, `not_loaded`, `not_ready`, `unknown_reference`, `reference_mismatch`, `unknown_animation`, `unknown_map`, `action_unavailable`, `native_interaction_busy`, `stale_interaction`, `adjustment_conflict`, `adjustment_limit`, `adjustment_failed` and dialogue request/context errors. Check the error code and current state before retrying mutations.
