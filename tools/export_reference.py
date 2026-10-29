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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--catalog", type=Path, required=True, help="Saved catalog reply to use as reference data")
    parser.add_argument("--output", type=Path, default=Path("docs/REFERENCE.md"))
    options = parser.parse_args()
    catalog = json.loads(options.catalog.read_text(encoding="utf-8"))
    parts = ["# Runtime ID reference\n",
             "English | [简体中文](REFERENCE.zh-CN.md)\n",
             "Reference mappings for the original Japanese release of Koikatu and the default female model (`ill_Default_Female`). Use the live `catalog` for the available IDs, model-specific patterns and current game conditions. Native morph identifiers are source data; English expression labels interpret those identifiers.\n",
             "[API command and reference input rules](../API.md). Every listed runtime entry can be passed back as an object, its `key`, or its numeric `id` in the matching input domain. Queue tasks use the same action and map IDs; task IDs themselves are unique run identifiers.\n",
             "## Native scheduler actions\n",
             "Use in `set_action.action`, `queue_action.action` or `set_desire.action`. Action -1 is plugin idle and is not schedulable; use `stop`. A valid action ID can still be unavailable because of game conditions, occupied points or missing native targets. The `mapsWithPoints` array in the live result identifies maps with the correct point layer and `mapDetails` explains each one.\n",
             table(["ID", "Key", "English meaning", "Native point layer"],
                   [(r["id"], "`" + r["key"] + "`", r["name"], r.get("layer")) for r in catalog["actions"]]),
             "\n## Maps\n",
             "Use a map reference in task `map`. `automatic` (-1) asks the scheduler to choose. Map names identify locations, not current availability. Check `canSchedule` in the live result before choosing a task destination; event-only maps and private rooms may be unavailable.\n",
             table(["ID", "Key", "English meaning"],
                   [(r["id"], "`" + r["key"] + "`", r["name"]) for r in catalog["maps"]]),
             "\n## Expression patterns\n",
             "Indices are local to the actor model and facial field. A mouth reference cannot be applied to eyes. Use `set_expression.changes.eyes`, `.eyebrow` or `.mouth`. The first mesh's exact closed/open morph targets are shown; the live entry includes all mesh targets. Visual strength also depends on `eyesOpen`, `mouthOpen` and blinking. Unknown custom morph combinations are labeled explicitly rather than assigned a guessed default-model expression.\n"]
    for field in ("eyes", "eyebrow", "mouth"):
        rows = []
        for r in catalog["expressions"][field]:
            source = r["morphTargets"][0] if r["morphTargets"] else {}
            rows.append((r["id"], "`" + r["key"] + "`", r["name"], "`" + str(source.get("closed")) + "`", "`" + str(source.get("open")) + "`"))
        parts.extend(["\n### " + field + "\n", table(["ID", "Key", "English meaning", "Closed target", "Open target"], rows)])
    parts.extend(["\n### Continuous controls\n",
                  "| Key | Type | Meaning |\n| --- | --- | --- |\n| eyesOpen | number 0-1 | Eyelid opening limit; 0 closed, 1 fully open |\n| mouthOpen | number 0-1 | Mouth opening; 0 closed, 1 fully open |\n| blink | boolean | Enable automatic native blinking |\n",
                  "\n## Animator states\n",
                  "Use `play_animation.state`. The signed ID is `Animator.StringToHash(key)` on layer 0, not a scheduler action ID. Animation playback does not perform gameplay tasks, props or navigation. The runtime confirms each listed state exists; the discovery list is not a complete Studio animation library.\n",
                  table(["Signed state hash", "State key", "English meaning"],
                        [(r["id"], "`" + r["key"] + "`", r["name"]) for r in catalog["animations"]]),
                  "\n## Regenerating the reference\n\nSave a fresh `catalog` result after loading the actor, then run:\n\n```powershell\npython -X utf8 tools/export_reference.py --catalog catalog.json\n```\n\nThis command regenerates the English reference. Update the corresponding tables in the [Chinese reference](REFERENCE.zh-CN.md) with the same IDs, keys, hashes and morph targets; translate explanations only.\n\nRuntime request templates contain the current story generation. Obtain fresh templates when calling the API.\n"])
    options.output.parent.mkdir(parents=True, exist_ok=True)
    options.output.write_text("\n".join(parts), encoding="utf-8")
    print(json.dumps({"output": str(options.output), "actions": len(catalog["actions"]), "maps": len(catalog["maps"]), "expressions": {f: len(catalog["expressions"][f]) for f in ("eyes", "eyebrow", "mouth")}, "animations": len(catalog["animations"])}))


if __name__ == "__main__":
    main()
