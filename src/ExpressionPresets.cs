using System;
using System.Collections.Generic;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        List<object> ReadExpressionPresets()
        {
            string[][] definitions = {
                new string[] { "neutral", "Neutral", "Neutral eyebrows", "Neutral", "Neutral mouth" },
                new string[] { "smile", "Smile", "Neutral eyebrows", "Smiling eyes", "Smile" },
                new string[] { "shy", "Shy", "Troubled eyebrows", "Embarrassed", "Tiny flustered mouth" },
                new string[] { "crying", "Crying", "Troubled eyebrows", "Crying", "Lonely mouth" },
                new string[] { "angry", "Angry", "Angry eyebrows", "Angry", "Angry mouth" },
                new string[] { "surprised", "Surprised", "Surprised eyebrows", "Neutral", "Surprised open mouth" }
            };
            var rows = new List<object>();
            foreach (var definition in definitions)
            {
                string key = definition[0], reason = null;
                var changes = Json.Map("eyesOpen", key == "crying" ? 0.65 : key == "shy" ? 0.8 : 1d, "eyebrowOpen", 1d, "blink", false,
                    "mouthOpen", key == "surprised" ? 0.8 : 1d, "mouthFixed", key == "surprised", "blush", key == "shy" ? 0.8 : 0d, "tears", key == "crying" ? 3 : 0, "highlight", true);
                var patterns = new Dictionary<string, object>();
                string[] fields = { "eyebrow", "eyes", "mouth" };
                for (int f = 0; f < fields.Length; f++)
                {
                    string field = fields[f]; var controller = FaceController(field); int matches = 0;
                    if (controller != null) for (int id = 0; id < controller.GetMaxPtn(); id++)
                    {
                        var pattern = ExpressionReference(field, id);
                        if ((string)pattern["name"] != definition[f + 2]) continue;
                        matches++; changes[field] = id; patterns[field] = pattern;
                    }
                    if (matches != 1) reason = (matches == 0 ? "missing_morph: " : "ambiguous_morph: ") + field + " / " + definition[f + 2];
                }
                string gazeType = key == "shy" ? "AWAY" : "FORWARD";
                foreach (Dictionary<string, object> gaze in ReadGazeCatalog())
                    if ((bool)gaze["available"] && (string)gaze["nativeType"] == gazeType) { changes["gaze"] = gaze["id"]; break; }
                if (!changes.ContainsKey("gaze")) reason = "missing_gaze_mode: " + gazeType;
                foreach (string field in changes.Keys)
                { string unavailable = FaceControlUnavailable(field); if (unavailable != null) reason = field + ": " + unavailable; }
                var row = Json.Map("key", key, "name", definition[1], "description", "Semantic combination resolved from this model's native morph labels and controls; no fixed pattern indices.",
                    "available", reason == null, "unavailableReason", reason, "changes", reason == null ? changes : null, "patterns", patterns);
                if (reason == null) row["request"] = Json.Map("op", "set_expression", "target", ActorId, "generation", ownedGeneration, "changes", Json.Map("preset", Json.Map("key", key)));
                rows.Add(row);
            }
            return rows;
        }
        Dictionary<string, object> ResolveExpressionPreset(object value)
        {
            var reference = value as Dictionary<string, object>;
            string key = reference == null ? value as string : Text(reference, "key");
            if (string.IsNullOrEmpty(key)) throw new BridgeException("invalid_argument", "preset must be a semantic key or a returned preset entry");
            foreach (Dictionary<string, object> row in ReadExpressionPresets()) if ((string)row["key"] == key)
            {
                if (!(bool)row["available"]) throw new BridgeException("preset_unavailable", key + ": " + row["unavailableReason"]);
                return new Dictionary<string, object>((Dictionary<string, object>)row["changes"]);
            }
            throw new BridgeException("unknown_preset", "Unknown expression preset; use expression_presets");
        }
    }
}
