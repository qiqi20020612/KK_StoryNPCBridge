using System;
using System.Collections.Generic;
using UnityEngine;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        static readonly string[] ExtraFaceFields = { "eyebrowOpen", "mouthOpen", "mouthFixed", "blush", "highlight", "tears", "gaze" };
        ChaControl extraFaceActor;
        Dictionary<string, object> extraFaceOriginal;
        static bool IsFaceField(string key)
        { return key == "eyes" || key == "eyebrow" || key == "mouth" || key == "eyesOpen" || key == "blink" || Array.IndexOf(ExtraFaceFields, key) >= 0; }
        void InstallExpressionHooks()
        { PatchNative(typeof(ChaControl), "ChangeLookEyesPtn", new Type[] { typeof(int) }, "KeepOwnedGaze", null, null); }
        static void KeepOwnedGaze(ChaControl __instance, ref int __0)
        {
            var p = Current; object value;
            if (p != null && p.faceOverride && p.ownedController != null && ReferenceEquals(__instance, p.ownedCha) &&
                (p.Mode == "roaming" || p.OwnedTalkActive) && p.ownedFace.TryGetValue("gaze", out value)) __0 = Convert.ToInt32(value);
        }
        static void WriteExtraFace(ChaControl c, string key, object value)
        {
            switch (key)
            {
                case "eyebrowOpen": c.ChangeEyebrowOpenMax(Convert.ToSingle(value)); break;
                case "mouthOpen": c.ChangeMouthOpenMax(Convert.ToSingle(value)); break;
                case "mouthFixed": c.ChangeMouthFixed((bool)value); break;
                case "blush": c.ChangeHohoAkaRate(Convert.ToSingle(value)); break;
                case "highlight": c.HideEyeHighlight(!(bool)value); break;
                case "tears": c.tearsLv = Convert.ToByte(value); break;
                case "gaze": c.ChangeLookEyesPtn(Convert.ToInt32(value)); break;
            }
        }
        static void RestoreExtraFace(ChaControl c, Dictionary<string, object> original)
        {
            if (c == null || !c.loadEnd || original == null) return;
            foreach (string key in ExtraFaceFields) WriteExtraFace(c, key, original[key]);
        }
        void ApplyFace(ChaControl c, Dictionary<string, object> face)
        {
            if (extraFaceActor != c)
            {
                RestoreExtraFace(extraFaceActor, extraFaceOriginal);
                extraFaceActor = c; extraFaceOriginal = ReadFace(c);
            }
            ApplyFaceValues(c, face);
        }
        void ClearFaceOverride()
        {
            faceOverride = false;
            RestoreExtraFace(extraFaceActor, extraFaceOriginal);
            extraFaceActor = null; extraFaceOriginal = null;
            if (ownedCha != null) ownedCha.enableExpression = true;
        }
        object SetOwnedExpression(Dictionary<string, object> command, bool clear)
        {
            var changes = clear ? NeutralFace() : NormalizeFace(Object(command, "changes")); ValidateFace(changes);
            var before = ownedFace; var actual = ReadFace(ownedCha); bool wasOverride = faceOverride;
            var originalActor = extraFaceActor; var original = extraFaceOriginal;
            ownedFace = clear ? NeutralFace() : new Dictionary<string, object>(before);
            foreach (var pair in changes) ownedFace[pair.Key] = pair.Value;
            try
            {
                if (clear) ClearFaceOverride(); else { faceOverride = true; ApplyFace(ownedCha, ownedFace); }
                SaveOwnedProfile();
            }
            catch
            {
                faceOverride = false; ApplyFaceValues(ownedCha, actual); ownedFace = before;
                extraFaceActor = originalActor; extraFaceOriginal = original; faceOverride = wasOverride;
                ownedCha.enableExpression = !wasOverride; throw;
            }
            return ReadOwnedActor();
        }
        string FaceControlUnavailable(string key)
        {
            if (!FaceReady(ownedCha) || !VisualActorReady) return "model_not_loaded";
            if (SceneBusy) return "scene_transition";
            if (Mode != "roaming" && !OwnedTalkActive) return "unsupported_visual_context";
            if (key == "blush")
            {
                if (ownedCha.customMatFace == null || !ownedCha.customMatFace.HasProperty(ChaShader._overcolor2)) return "blush_shader_unavailable";
                if (Manager.Config.EtcData != null && !Manager.Config.EtcData.hohoAka) return "disabled_in_game_settings";
            }
            if (key == "highlight")
            {
                bool found = false;
                if (ownedCha.rendEye != null) foreach (var renderer in ownedCha.rendEye)
                    if (renderer != null && renderer.sharedMaterial != null)
                    { found = true; if (!renderer.sharedMaterial.HasProperty(ChaShader._isHighLight)) return "highlight_shader_unavailable"; }
                if (!found) return "highlight_shader_unavailable";
            }
            if (key == "tears") foreach (var id in new ChaReference.RefObjKey[] { ChaReference.RefObjKey.S_TEARS_01, ChaReference.RefObjKey.S_TEARS_02, ChaReference.RefObjKey.S_TEARS_03 })
                if (ownedCha.GetReferenceInfo(id) == null) return "tear_meshes_unavailable";
            if (key == "gaze" && (ownedCha.eyeLookCtrl == null || ownedCha.eyeLookCtrl.eyeLookScript == null || ownedCha.eyeLookCtrl.eyeLookScript.eyeTypeStates == null)) return "gaze_controller_unavailable";
            return null;
        }
        static bool SupportedGaze(EYE_LOOK_TYPE type)
        { return type == EYE_LOOK_TYPE.FORWARD || type == EYE_LOOK_TYPE.TARGET || type == EYE_LOOK_TYPE.AWAY; }
        Dictionary<string, object> GazeReference(int id)
        {
            string unavailable = FaceControlUnavailable("gaze");
            var states = unavailable == null ? ownedCha.eyeLookCtrl.eyeLookScript.eyeTypeStates : null;
            if (states == null || id < 0 || id >= states.Length || states[id] == null) throw new BridgeException("unknown_reference", "Unknown gaze id; refresh expression_catalog.gaze");
            var state = states[id]; bool supported = SupportedGaze(state.lookType);
            string description = state.lookType == EYE_LOOK_TYPE.FORWARD ? "Look forward relative to the head." : state.lookType == EYE_LOOK_TYPE.TARGET
                ? "Look toward the current native eye target; this does not select a new target." : state.lookType == EYE_LOOK_TYPE.AWAY ? "Look away from the current native target."
                : "Requires native fixed-angle data; arbitrary gaze directions are not exposed.";
            var row = Json.Map("id", id, "key", "gaze/" + id, "field", "gaze", "name", state.lookType.ToString(), "nativeName", state.comment,
                "nativeType", state.lookType.ToString(), "description", description, "available", supported, "unavailableReason", supported ? null : "native_fixed_angles_required");
            if (supported) row["request"] = Json.Map("op", "set_expression", "target", ActorId, "generation", ownedGeneration, "changes", Json.Map("gaze", Json.Map("id", id, "key", row["key"], "field", "gaze")));
            return row;
        }
        List<object> ReadGazeCatalog()
        {
            var rows = new List<object>();
            if (FaceControlUnavailable("gaze") == null) for (int i = 0; i < ownedCha.eyeLookCtrl.eyeLookScript.eyeTypeStates.Length; i++)
                if (ownedCha.eyeLookCtrl.eyeLookScript.eyeTypeStates[i] != null) rows.Add(GazeReference(i));
            return rows;
        }
        int GazeId(object value)
        {
            var reference = value as Dictionary<string, object>; object field;
            if (reference != null && reference.TryGetValue("field", out field) && !Equals(field, "gaze")) throw new BridgeException("reference_mismatch", "Reference belongs to another field");
            int id = ReferenceId(value, "gaze", GazeReference, delegate(string key) {
                foreach (Dictionary<string, object> row in ReadGazeCatalog()) if ((string)row["key"] == key) return (int)row["id"];
                return int.MinValue;
            });
            if (!(bool)GazeReference(id)["available"]) throw new BridgeException("unsupported_control", "gaze: native_fixed_angles_required");
            return id;
        }
        List<object> ReadFaceControls()
        {
            var rows = new List<object>();
            string[] keys = { "eyesOpen", "blink", "eyebrowOpen", "mouthOpen", "mouthFixed", "blush", "highlight", "tears", "gaze" };
            string[] names = { "Eye opening limit", "Automatic blinking", "Eyebrow opening limit", "Mouth opening limit", "Fixed mouth opening", "Cheek blush", "Eye highlights", "Tear level", "Native gaze mode" };
            string[] descriptions = { "Maximum eyelid opening; native blinking remains independent.", "Enable the native blink controller.",
                "Eyebrow opening limit; visible strength depends on the selected morph and blinking.", "Mouth opening limit, not a fixed opening pose; lip sync stays native unless mouthFixed is true.",
                "Pin the mouth to mouthOpen; false restores native mouth motion and lip sync.", "Native cheek blush intensity; respects the game's blush setting.",
                "Show or hide native eye highlights; no texture or color replacement.", "Native tear mesh level: 0 none, 1 light, 2 medium, 3 heavy; not a continuous amount.",
                "Native gaze mode from the live gaze directory; no arbitrary angles or target coordinates." };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i], type = key == "blink" || key == "mouthFixed" || key == "highlight" ? "boolean" : key == "tears" ? "integer" : key == "gaze" ? "reference" : "number";
                var row = Json.Map("key", key, "name", names[i], "type", type, "description", descriptions[i], "available", FaceControlUnavailable(key) == null, "unavailableReason", FaceControlUnavailable(key));
                if (type == "number" || type == "integer") { row["min"] = 0; row["max"] = key == "tears" ? 3 : 1; }
                rows.Add(row);
            }
            return rows;
        }
    }
}
