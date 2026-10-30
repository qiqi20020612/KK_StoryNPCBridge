"""Render English API ID reference tables from a saved runtime catalog."""
import argparse
import json
from pathlib import Path


def cell(value):
    return str(value).replace("|", "\\|").replace("\n", " ")


def table(headers, rows):
    return "\n".join([
        "| " + " | ".join(headers) + " |",
        "| " + " | ".join("---" for _ in headers) + " |",
        *("| " + " | ".join(cell(c) for c in row) + " |" for row in rows),
    ])


def native_event_requirements(entry):
    requirements = entry["requirements"]
    parts = []
    if requirements["minimumRelation"]:
        parts.append("Friend or girlfriend")
    if requirements["exactRelation"]:
        parts.append(requirements["exactRelation"]["name"])
    if requirements["clubMember"] is not None:
        parts.append("Club member" if requirements["clubMember"] else "Nonmember")
    if requirements["nativeHLocation"]:
        parts.append("eligible native H location")
    if requirements["stages"]:
        parts.append(("stage " if parts else "Stage ") + ", ".join(map(str, requirements["stages"])))
    if requirements["unusedLunch"]:
        parts.append("`isLunch=false`")
    if requirements["unoccupiedDateSlot"]:
        parts.append("no other NPC's date booking")
    return "; ".join(parts) or "Ready roaming or the NPC's native conversation"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--catalog", type=Path, required=True, help="Saved catalog reply to use as reference data")
    parser.add_argument("--output", type=Path, default=Path("docs/REFERENCE.md"))
    options = parser.parse_args()
    catalog = json.loads(options.catalog.read_text(encoding="utf-8"))
    parts = ["# Runtime ID reference\n",
             "English | [简体中文](REFERENCE.zh-CN.md)\n",
             "Reference mappings for the original Japanese release of Koikatu and the default female model (`ill_Default_Female`). Use the live `catalog` for the available IDs, model-specific patterns and current game conditions. Native morph identifiers are source data; English expression labels interpret those identifiers.\n",
             "[API command and reference input rules](../API.md). Callable references accept a returned object, its `key`, or its numeric `id` in the matching input domain. Game-time references are read-only. Queue tasks use the same action and map IDs; task IDs themselves are unique run identifiers.\n",
             "## Relationships\n",
             "Use `set_stats.changes.relation`. A relationship reference updates meeting, friendship and girlfriend flags together. The native getter derives the result from those flags. Attribute types, ranges and all writable flags are documented in [Native attributes and relationships](../API.md#native-attributes-and-relationships).\n",
             "`adjust_stats.deltas` accepts numeric field keys with `stat_catalog.fields[].adjustable=true`, using the returned `min`/`max` for clamping. Relationship references and boolean flags remain absolute-only; retry identity and result fields are described in [Incremental attribute changes](../API.md#incremental-attribute-changes).\n",
             table(["ID", "Key", "English meaning"],
                   [(r["id"], "`" + r["key"] + "`", r["name"]) for r in catalog["stats"]["relationships"]]),
             "\n## Game time\n",
             "These IDs describe observations from `gameTime`; they are not scheduler action IDs or clock-write commands. The game exposes weekdays, not an absolute calendar date. See [Player and native game time](../API.md#player-and-native-game-time) for day counters and events.\n",
             "### Native stages\n",
             table(["ID", "Key", "English meaning"],
                   [(r["id"], "`" + r["key"] + "`", r["name"]) for r in catalog["time"]["stages"]]),
             "\n### UI periods\n",
             "Stages 0–4 map to period 0, 5–7 to 1, 8 to 2, 9–10 to 3 and 11 to 4.\n",
             table(["ID", "Key", "English meaning"],
                   [(r["id"], "`" + r["key"] + "`", r["name"]) for r in catalog["time"]["periods"]]),
             "\n### Weekdays\n",
             table(["ID", "Key", "English meaning"],
                   [(r["id"], "`" + r["key"] + "`", r["name"]) for r in catalog["time"]["weekdays"]]),
             "\n## Native activity effects\n",
             "Use `trigger_native_event.event` with an entry from `native_event_catalog` / `catalog.nativeEvents`, its key or its numeric ID. These are native conversation event IDs, separate from scheduler action IDs. `commandId` is the native UI command (`event ID + 10`). The accepted native result is applied directly, without an invitation/refusal roll or a pre-existing conversation. Check live availability; the NPC must be loaded on the current map.\n",
             table(["Event ID", "Key", "Effect", "Command ID", "Native result", "Additional requirements"],
                   [(r["id"], "`" + r["key"] + "`", r["name"], r["commandId"],
                     "`" + str(r["result"]["id"]) + " / " + r["result"]["key"] + "`",
                     native_event_requirements(r))
                    for r in catalog["nativeEvents"]]),
             "\nActivity scenes retain their native ADV and time/state effects. Follow uses the native player-follow binding. Confession/date/admission apply state effects without opening or interrupting dialogue; native result `-1 / continue_talk` means no terminal scene result, not scheduler idle. The date effect updates native calendar/save identity; admission and confession update the applicable ranking/player/report counters. `set_stats` writes flags without executing these related event effects. `requirements` describes relationship/membership/stage/context constraints; button interception remains observational until the external controller explicitly executes an effect.",
             "\n## Native scheduler actions\n",
             "`sample_native_action` independently draws another native girl's current task without source character information. Returned `task.actionInfo` and `mapInfo` use these same action and map domains; `task` can be supplied to `set_action` / `queue_action`. Sampling does not guarantee replay availability. See [Sample another native NPC's current task](../API.md#sample-another-native-npcs-current-task).\n",
             "`native_plan` observes selected actions and current desire-weighted candidates using these same references; `claim` returns the pre-control snapshot in `handoverReport.nativePlan`. The native AI chooses one action at a time. Its recent-action and priority memory are not a future task queue; candidate order is descriptive and does not guarantee eligibility or execution. See [Native AI planning observations](../API.md#native-ai-planning-observations).\n",
             "Use in `set_action.action`, `queue_action.action` or `set_desire.action`. Action -1 is plugin idle and is not schedulable; use `stop`. A valid action ID can still be unavailable because of game conditions, occupied points or missing native targets. The `mapsWithPoints` array in the live result identifies maps with the correct point layer and `mapDetails` explains each one.\n",
             table(["ID", "Key", "English meaning", "Native point layer"],
                   [(r["id"], "`" + r["key"] + "`", r["name"], r.get("layer")) for r in catalog["actions"]]),
             "\n## Maps\n",
             "Use a map reference in task `map`. `automatic` (-1) asks the scheduler to choose. Map names identify locations, not current availability. Check `canSchedule` in the live result before choosing a task destination; event-only maps and private rooms may be unavailable.\n",
             table(["ID", "Key", "English meaning"],
                   [(r["id"], "`" + r["key"] + "`", r["name"]) for r in catalog["maps"]]),
             "\n## Expression patterns\n",
             "Indices are local to the actor model and facial field. A mouth reference cannot be applied to eyes. Use `set_expression.changes.eyes`, `.eyebrow` or `.mouth`. The first mesh's exact closed/open morph targets are shown; the live entry includes all mesh targets. Visual strength also depends on `eyesOpen`, native mouth opening and blinking. Mouth opening is read-only (`expression.mouthOpen`); it cannot be set through the API. Unknown custom morph combinations are labeled explicitly rather than assigned a guessed default-model expression.\n"]
    for field in ("eyes", "eyebrow", "mouth"):
        rows = []
        for r in catalog["expressions"][field]:
            source = r["morphTargets"][0] if r["morphTargets"] else {}
            rows.append((r["id"], "`" + r["key"] + "`", r["name"], "`" + str(source.get("closed")) + "`", "`" + str(source.get("open")) + "`"))
        parts.extend(["\n### " + field + "\n", table(["ID", "Key", "English meaning", "Closed target", "Open target"], rows)])
    parts.extend(["\n### Continuous controls\n",
                  "| Key | Type | Meaning |\n| --- | --- | --- |\n| eyesOpen | number 0-1 | Eyelid opening limit; 0 closed, 1 fully open |\n| blink | boolean | Enable automatic native blinking |\n",
                  "\n## Animator states\n",
                  "Use `play_animation.state`. The signed ID is `Animator.StringToHash(key)` on layer 0, not a scheduler action ID. Animation playback does not perform gameplay tasks, props or navigation. The runtime confirms each listed state exists; the discovery list is not a complete Studio animation library. These examples use the roaming model. Native talk uses another Animator; refresh `animation_catalog` after entering or leaving a conversation and check each entry's `context`. `animation.state` and hashes report actual playback.\n",
                  table(["Signed state hash", "State key", "English meaning"],
                        [(r["id"], "`" + r["key"] + "`", r["name"]) for r in catalog["animations"]]),
                  "\n## Regenerating the reference\n\nSave a fresh `catalog` result after loading the actor, then run:\n\n```powershell\npython -X utf8 tools/export_reference.py --catalog catalog.json\n```\n\nThis command regenerates the English reference. Update the corresponding tables in the [Chinese reference](REFERENCE.zh-CN.md) with the same IDs, keys, hashes and morph targets; translate explanations only.\n\nRuntime request templates contain the current story generation. Obtain fresh templates when calling the API.\n"])
    options.output.parent.mkdir(parents=True, exist_ok=True)
    options.output.write_text("\n".join(parts), encoding="utf-8")
    print(json.dumps({"output": str(options.output), "actions": len(catalog["actions"]), "maps": len(catalog["maps"]), "expressions": {f: len(catalog["expressions"][f]) for f in ("eyes", "eyebrow", "mouth")}, "animations": len(catalog["animations"])}))


if __name__ == "__main__":
    main()
