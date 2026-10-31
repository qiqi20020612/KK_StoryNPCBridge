# KKAPI integration assessment

Integration assessed against `v1.5.0` / `31291f6`, incorporated into 2.0.0 together with the independent read-only H observer. Protocol remains 2. The integration assessment below distinguishes its original isolated checks from later game acceptance.

## Dependency and loading

- Compile against the official `IllusionModdingAPI.KKAPI` **1.48.1** NuGet package, pinned in the project and lock file. The official [KKAPI project](https://github.com/IllusionMods/IllusionModdingAPI/blob/v1.48.1/src/KKAPI/KKAPI.csproj) targets .NET Framework 3.5.
- `[BepInDependency(KoikatuAPI.GUID, "1.48.1")]` declares a hard dependency on `marco.kkapi`. BepInEx initializes KKAPI before the bridge. The minimum stays fixed when the local compiler uses a newer installed DLL.
- Keep C# 3, net35 and warnings-as-errors. Resolve the upstream analyzer packages using the BepInEx feed, then remove only those three packages' analyzers before compilation. This preserves the pre-integration compiler-only policy; it does not disable compiler warnings or unrelated analyzers.
- The local compiler accepts `-KkapiPath`, or discovers one installed `KKAPI.dll` recursively and validates its assembly identity/minimum version. It never copies KKAPI.
- Packaging uses an explicit three-file allowlist: bridge DLL, MIT license and `DEPENDENCIES.md`. See [installation dependencies](DEPENDENCIES.md).

## Replacements and equivalence

| Existing implementation | Replacement | Preserved behavior |
| --- | --- | --- |
| Seven direct `FindObjectOfType<TalkScene>` calls | `GameAPI.GetTalkScene()` | Official method performs the same lookup; retain the 0.2-second context refresh, fresh command/record lookups, null checks and bridge-target identity guards. No new caching. |
| Singleton/`actScene` traversal in `RefreshWorld` | `GameAPI.GetActionScene()` | Same `IsInstance` guard and nullable native `actScene`; world/generation and control recovery still belong to the bridge. |
| Two private `TalkScene.isUpdateCamera`/`StartADV` reflection pairs | `EventUtils.StartTextSceneEvent(talk, list, false, false)` | Same camera setting, heroine binding and `StartADV` call. Do not decrease talk time or end the talk scene. Prime the official iterator synchronously, then resume its first null yield and native `Program.Wait("Talk")` through the existing coroutines. Startup exceptions still reach the original catch/reply path. |
| Three manual leading `CharaChange -2 true` transfers | Official ADV helper's inserted transfer | Exactly one heroine binding at the front, with the same subsequent commands for simple text, managed dialogue and sequences/motions. |
| Two reflective cursor-field writes | `GameExtensions.SetIsCursorLock` | Same backing field; retain cursor capture, idempotence and restoration to the originally captured action scene. |
| Handwritten occupied-seat scan and player-seat exclusions | `IsValidClassSeat` and `GetHeroineAtSeat` | Still search 24 down to 0 in the player's class, exclude player/adjacent seats, select the highest free seat and throw the original error when full. Avoid constructing unused heroines. |

Official implementation reviewed at the fixed 1.48.1 tag: [GameAPI](https://github.com/IllusionMods/IllusionModdingAPI/blob/v1.48.1/src/KKAPI/MainGame/GameApi.cs), [EventUtils](https://github.com/IllusionMods/IllusionModdingAPI/blob/v1.48.1/src/KKAPI/MainGame/Utilities/EventUtils.cs), [GameExtensions](https://github.com/IllusionMods/IllusionModdingAPI/blob/v1.48.1/src/KKAPI/MainGame/Utilities/GameExtensions.cs).

No controller forwarding class, API shim, fallback implementation or additional runtime state was introduced. The two existing ADV wait coroutines remain responsible for bridge completion/hold/generation state.

## Assessed and retained

| Candidate | Why the native implementation remains |
| --- | --- |
| `GameAPI.GameLoad` / `GameCustomFunctionController.OnGameLoad` | Official load hook does not expose or test the native success result, nor pass the loaded `SaveData` instance. The bridge must reject failed loads and separate probe loads from the active story. |
| `GameAPI.GameSave` / `OnGameSave` / extended save data | Callback is before native serialization. Bridge sidecars must be written only after successful native save, including Save As/autosaves, with filename-based isolation. The injected heroine and desire entry must stay out of native save files, including exception recovery. |
| `NewGame`, `OnEnterNightMenu`, period/day callbacks | New-game callback follows scene navigation rather than the required pre-`ActionControl.Init` registration point. Time events do not provide the `Cycle` instance or a captured prior weekday, and callback exceptions are caught by KKAPI. Keep native instance checks, exact stage timing, same-weekday seven-day advancement and existing event ordering. A forwarding controller would add a layer without replacing these hooks. |
| `EventUtils.StartAdvEvent` / `CreateNewEvent` | Roaming event helper manages global player/NPC pause, BGM and restoration. Preserve the bridge's talk-only ownership/recovery and its explicit `Transfer.NewList(false, false)` flags. |
| `GameAPI.GetADVScene` / `GetCurrentHeroine` | One current ADV/heroine cannot replace visible-scenario and participant-specific searches or bridge heroine reference identity. Keep unrelated conversations excluded. |
| `TalkSceneUtils.Touch` | Official helper temporarily changes `m_touchMode`, restores it on normal return and uses its own touch-kind enum. Existing probe sets native `touchMode`, keeps that state and invokes through reflection; changing this would alter mapping, persistence and exception behavior. Preserve touch/look observation and original reactions. |
| `AddTalkButton` / `AddTouchIcon` | Native menu buttons/icons have different parents and visibility. The existing compose control also works while native menus are hidden; the waiting notice and input shield have their own layering. Keep the present UI and gating. |
| `GetNPC`, `GetHeroine`, `GetFreeClassSlots` | Their fallback lookups, roster searches or ascending creation of temporary heroine objects add behavior not needed here. Preserve exact injected identity, actor binding and highest-seat selection. |
| `Character.CreateChara` factory selection | Existing Japanese/Party overload selection is still required; reviewed KKAPI main-game utilities do not replace template creation/injection. |

Lifecycle semantics were checked against [official hooks](https://github.com/IllusionMods/IllusionModdingAPI/blob/v1.48.1/src/KKAPI/MainGame/GameAPI.Hooks.cs) and [controller callbacks](https://github.com/IllusionMods/IllusionModdingAPI/blob/v1.48.1/src/KKAPI/MainGame/GameCustomFunctionController.cs). UI/touch behavior was checked against [TalkSceneUtils](https://github.com/IllusionMods/IllusionModdingAPI/blob/v1.48.1/src/KKAPI/MainGame/TalkScene/TalkSceneUtils.cs) and [custom button implementation](https://github.com/IllusionMods/IllusionModdingAPI/blob/v1.48.1/src/KKAPI/MainGame/TalkScene/TalkSceneCustomButtons.cs).

All original Harmony registrations remain: active-save load/early actor registration; save serialization/desire filtering and finalizers; native AI action selection/arrival, world placement and conversation tracking; ADV hold/sequence/error/record/pagination hooks; keyboard, button and next-line interception; native command/touch/reaction/event routing; weekday/stage changes and gaze preservation. These have bridge-specific responsibilities. No native hook was removed simply because KKAPI offers a broader callback.

## Original integration validation

- Original baseline: locked restore and Release build passed without warnings.
- Integrated build: locked restore, net35/C# 3 Release compilation and local package validation passed without warnings. An independent source copy also restored through an empty package/HTTP cache with fallback folders disabled, then built and packaged successfully.
- Windows .NET 3.5 `csc.exe` build passed using a reference-assembly game-directory fixture. This checks the script/reference wiring, not a real Japanese or Party installation.
- Extracted original/current methods plus the official ADV/cursor/seat helper source passed **12,414 assertions**, including 12,001 seat fixtures, matching startup errors, ADV command order, first-frame/native-wait yields, hold/release, conversation cancellation, iterator disposal and cursor restoration. These use a simulated host, not Unity acceptance.
- Existing Python tools passed byte-compilation and their `--help` smoke checks. At the integration baseline the public repository had no automated game/unit test suite; 2.0.0 adds the report test project.
- IL review found only the intended migrated methods changed among existing top-level method bodies; protocol handling, native hooks, save sidecars, ownership/recovery and other unchanged methods retained their compiled bodies. Assembly remains CLR 2.0 (`v2.0.50727`), version `1.5.0.0`, with the required KKAPI reference/dependency.
- Package checks cover ZIP allowlist, dependency exclusion, plugin identity, version matching and SHA-256 checksums. No game deployment or publication was performed.

The upstream compatibility analyzer surfaced an existing concern about `ChaReference.RefObjKey.S_TEARS_*` constants across Party/non-Darkness builds. That expression implementation was not changed here; compiler-only success does not establish cross-installation expression behavior.

The original integration acceptance checklist was: dependency loading/rejection; existing/new story and failed/probe loads; no duplicate bridge heroine or seat; two save slots, Save As, autosaves and failed native saves; simple/managed/sequence/native-motion ADV with long text, waiting/input, cancellation and synchronous startup failures; touch/look and original reactions; release/disconnect/reconnect during roaming/ADV; period/day relocation and cursor/input restoration. Preserve `bridge:main`, protocol 2, event order and `native_save_only` throughout.

## 2.0.0 release validation

The following checks cover the final 2.0.0 candidate, rather than the historical integration-only build above.

| Check | Result and scope |
| --- | --- |
| Clean build | Locked restore with empty package/HTTP caches, net35/C# 3 Release build, report tests and packaging passed in an independent source directory. The SDK DLL also reproduced after CRLF conversion. Zero build warnings or errors. |
| Native compiler | Windows .NET Framework 3.5 `csc.exe` compiled against installed native game references. The release uses the deterministic SDK build. |
| Automated report tests | **106 assertions** exercise the production report module using data-only host doubles: all 46 count representations, snapshots/deltas, participants, duplicate callbacks, late callbacks, save/generation changes, interrupted unloads, missing data and saturation. These do not execute Unity. |
| Helper and JSON tests | **12,414 simulated equivalence assertions** for the migrated helpers and **20 .NET 3.5 JSON assertions** passed. |
| Artifact checks | **37 checks** passed for independent-build parity, checksums, the three-file ZIP allowlist, dependency rejection and preservation of protected source/native hook registrations. No KKAPI, game assemblies or validation fixture is packaged. |
| Existing live acceptance | All **nine available acceptance suites (93 checks)** passed: protocol, catalogs, interactions, ADV input, native menus, native reactions, NPC invitations, native operation and autonomous scheduling. Older private fixtures were aligned with the existing 1.5 ownership contract and disposable-save preconditions; production control code was not changed for those fixtures. |
| Additional live regression | **127 checks** covered managed/native ADV motion and lifecycle, sequences, native saves/Save As/failure/retry, cold reload and graceful exit, measured pagination, relocation, follow recovery, overnight restoration and native-save-only sidecars. |
| Live H reports | Foreplay, intercourse, an ordinary NPC's independent solo scene, a bridge solo scene, a female pair, a three-person scene, native title interruption and subsequent save switching passed. All **45 scalar/array count fields** were compared directly with native snapshots; the sparse mouse matrix is covered by the automated boundary fixtures. Completed sessions produced one ordered start/end/completion event; the interrupted session produced one incomplete report. |
| Nonzero native settlement | A simultaneous outcome populated overlapping native subtype counters without double-counting the aggregate. Character `hCount` was reported as a +1 difference from a nonzero baseline. Independent-mode lewdness remained 42 at the end callback, then settled to 21 after the parent H scene returned; the report retained both snapshots and the -21 final delta. |
| Cleanup | Disposable saves/profiles and the private runtime fixture were archived outside the project. Original saves and configuration were restored and verified against their backup hashes. |

Relative to 1.5.0, production C# changes are **+391 / -33, net +358 lines**, including the 350-line independent H observer. The helper migration itself was net +2 C# lines: it removes repeated discovery, reflection, ADV binding and occupied-seat construction while explicitly preserving the official iterator's startup/wait timing. All original native Harmony registrations remain; the report adds only the passive animation-state postfix described in the API.

The native field/mode review covers more cases than the live scenarios above. Free H, VR, peeping/replay/participant changes, two-male expansion scenes, every pose/mode transition and interactions with other mods still need installation-specific game verification. Synthetic tests are not evidence that those runtime combinations passed. Native per-participant orgasm/finish counts, an independent male-climax total and background AI events that never create an HProc/KKAPI lifecycle remain unavailable. Process termination can prevent report delivery.
