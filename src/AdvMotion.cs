using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using UnityEngine;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        static readonly string[] AdvMotionFields = { "state", "bundle", "asset", "ikBundle", "ikAsset", "shakeBundle", "shakeAsset", "layerNo" };

        static string MotionText(Dictionary<string, object> motion, string key)
        {
            string text = OptionalText(motion, key, "");
            if (text.Length > 256 || text.IndexOf('\0') >= 0 || text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0)
                throw new BridgeException("invalid_argument", key + " must be a single resource identifier of at most 256 characters");
            return text;
        }
        static void ValidateMotionPair(string bundle, string asset, string label)
        {
            if ((bundle.Length == 0) != (asset.Length == 0)) throw new BridgeException("invalid_argument", label + " bundle and asset must be supplied together");
            if (bundle.Length == 0) return;
            if (bundle.IndexOf('\\') >= 0 || bundle.IndexOf(':') >= 0 || bundle.StartsWith("/", StringComparison.Ordinal) ||
                bundle.IndexOf("..", StringComparison.Ordinal) >= 0 || !bundle.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase))
                throw new BridgeException("invalid_argument", label + " bundle must be a relative abdata .unity3d path using forward slashes");
            if (!File.Exists(Path.Combine(Paths.GameRootPath, "abdata/" + bundle))) throw new BridgeException("motion_resource_missing", label + " bundle is not installed: " + bundle);
        }
        static T LoadMotionAsset<T>(string bundle, string asset) where T : UnityEngine.Object
        {
            var source = new AssetBundleData { bundle = bundle, asset = asset };
            try
            {
                T result = source.GetAsset<T>();
                if (result == null) throw new BridgeException("motion_resource_missing", "No " + typeof(T).Name + " asset: " + bundle + "/" + asset);
                return result;
            }
            finally { source.UnloadBundle(false, false); }
        }
        Dictionary<string, object> NormalizeAdvMotion(Dictionary<string, object> input, bool requireState)
        {
            foreach (string key in input.Keys)
            {
                if (key == "overrideBundle" || key == "overrideAsset")
                    throw new BridgeException("unsupported_control", "Native override clip replacement has not been verified; overrideBundle and overrideAsset are unavailable");
                if (Array.IndexOf(AdvMotionFields, key) < 0) throw new BridgeException("unsupported_field", "Unsupported motion field: " + key);
            }
            var motion = new Dictionary<string, object>();
            foreach (string key in AdvMotionFields) if (key != "layerNo") motion[key] = MotionText(input, key);
            string state = (string)motion["state"];
            if (requireState && state.Length == 0) throw new BridgeException("invalid_argument", "motion.state is required");
            object layer;
            if (input.TryGetValue("layerNo", out layer) && Number(layer, 0, int.MaxValue, "layerNo") != 0)
                throw new BridgeException("unsupported_control", "Only native CharaMotion layerNo 0 has been verified");
            motion["layerNo"] = 0;
            foreach (string prefix in new string[] { "", "ik", "shake" })
                ValidateMotionPair((string)motion[prefix.Length == 0 ? "bundle" : prefix + "Bundle"],
                    (string)motion[prefix.Length == 0 ? "asset" : prefix + "Asset"], prefix.Length == 0 ? "Controller" : prefix);
            return motion;
        }
        static bool HasMotionState(MotionIKData data, string state)
        { foreach (var item in data.states) if (item.name == state) return true; return false; }
        static void ValidateMotionIK(MotionIKData data, string state)
        {
            foreach (var item in data.states)
                if (item.name == state)
                {
                    // This API stages one character, so partner-dependent corrections cannot be resolved.
                    foreach (var frame in item.frames) if (frame.editNo != 0)
                        throw new BridgeException("unsupported_control", "The selected IK state requires another staged character");
                    foreach (var part in item.parts)
                        if (part != null && part.param2 != null && !string.IsNullOrEmpty(part.param2.target) && part.param2.sex != 0)
                            throw new BridgeException("unsupported_control", "The selected IK target requires another staged character");
                    return;
                }
            throw new BridgeException("incompatible_motion", "The IK asset has no entry for motion.state");
        }
        static bool HasShakeState(YureExcel data, string state)
        { foreach (var item in data.param) if (item.name == state) return true; return false; }

        object DescribeAdvMotions(Dictionary<string, object> motion, bool validate)
        {
            var actor = ownedCha;
            if (!OwnedTalkActive || !VisualActorReady || actor.animBody == null)
                throw new BridgeException("not_ready", "Advanced ADV motions require the bridge NPC's native TalkScene model");
            string state = (string)motion["state"], bundle = (string)motion["bundle"], asset = (string)motion["asset"];
            RuntimeAnimatorController controller = bundle.Length == 0 ? actor.animBody.runtimeAnimatorController : LoadMotionAsset<RuntimeAnimatorController>(bundle, asset);
            MotionIKData ik = null; YureExcel shake = null;
            if (((string)motion["ikAsset"]).Length > 0)
                ik = new MotionIKData(LoadMotionAsset<TextAsset>((string)motion["ikBundle"], (string)motion["ikAsset"]));
            if (((string)motion["shakeAsset"]).Length > 0)
                shake = LoadMotionAsset<YureExcel>((string)motion["shakeBundle"], (string)motion["shakeAsset"]);
            var candidates = new List<string>();
            if (state.Length > 0) candidates.Add(state);
            foreach (var clip in controller.animationClips) if (!candidates.Contains(clip.name)) candidates.Add(clip.name);
            if (ik != null) foreach (var item in ik.states) if (!candidates.Contains(item.name)) candidates.Add(item.name);
            if (shake != null) foreach (var item in shake.param) if (!candidates.Contains(item.name)) candidates.Add(item.name);
            // Animator cannot enumerate states. Preserve the existing installation-aware discovery candidates.
            if (bundle.Length == 0) foreach (Dictionary<string, object> item in (System.Collections.IEnumerable)ReadAnimationCatalog())
                if (!candidates.Contains((string)item["key"])) candidates.Add((string)item["key"]);
            var probe = new GameObject("KK Story NPC Bridge motion validation"); probe.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var animator = probe.AddComponent<Animator>(); animator.runtimeAnimatorController = controller; animator.Update(0);
                if (validate)
                {
                    if (!animator.HasState(0, Animator.StringToHash(state))) throw new BridgeException("unknown_animation", "motion.state does not exist on controller layer 0");
                    if (ik != null) ValidateMotionIK(ik, state);
                    if (shake != null && !HasShakeState(shake, state)) throw new BridgeException("incompatible_motion", "The shake asset has no entry for motion.state");
                }
                var rows = new List<object>();
                foreach (string candidate in candidates)
                {
                    if (!animator.HasState(0, Animator.StringToHash(candidate)) || ik != null && !HasMotionState(ik, candidate) || shake != null && !HasShakeState(shake, candidate)) continue;
                    if (ik != null) { try { ValidateMotionIK(ik, candidate); } catch (BridgeException) { continue; } }
                    var reference = new Dictionary<string, object>(motion); reference["state"] = candidate;
                    rows.Add(Json.Map("key", candidate, "id", Animator.StringToHash(candidate), "description", "Verified Animator state on native ADV layer 0; resource-specific pose without gameplay effects.",
                        "motion", reference, "request", Json.Map("op", "play_adv_motion", "target", ActorId, "generation", ownedGeneration, "motion", reference, "durationSeconds", 3)));
                }
                return Json.Map("context", "native_talk", "controller", controller.name, "layerCount", animator.layerCount, "motions", rows,
                    "discoveryComplete", false, "description", "Candidates come from clips, native IK/shake entries and the live animation catalog, filtered through Animator.HasState. Supply state to verify an additional known name.",
                    "parameters", Json.Map("state", "Required state name; verified against the selected controller.", "bundle", "Optional installed controller bundle; pair with asset.",
                        "asset", "RuntimeAnimatorController asset.", "ikBundle", "Optional native IK TextAsset bundle; pair with ikAsset.", "ikAsset", "IK data must contain the selected state without partner-dependent corrections.",
                        "shakeBundle", "Optional native YureExcel bundle; pair with shakeAsset.", "shakeAsset", "Physics and body-shape data must contain the selected state.",
                        "layerNo", "Only integer 0 is supported.", "overrideBundle", "Unavailable: actual clip replacement has not been verified.", "overrideAsset", "Unavailable: actual clip replacement has not been verified."));
            }
            finally { Destroy(probe); }
        }
        object ReadAdvMotionCatalog(Dictionary<string, object> command)
        { return DescribeAdvMotions(NormalizeAdvMotion(command.ContainsKey("motion") ? Object(command, "motion") : Json.Map(), false), false); }
        object PlayAdvMotion(Dictionary<string, object> command)
        {
            RequireAdvSequenceReady();
            var motion = NormalizeAdvMotion(Object(command, "motion"), true);
            object duration; double seconds = command.TryGetValue("durationSeconds", out duration) ? Number(duration, 0.1, 30, "durationSeconds") : 3;
            DescribeAdvMotions(motion, true);
            return StartAdvSequence(new List<AdvSequenceSegment> { new AdvSequenceSegment { Speaker = "npc", Text = "", NativeMotion = motion, Wait = seconds } }, "play_adv_motion");
        }
        static string[] AdvMotionArgs(Dictionary<string, object> motion)
        {
            return new string[] { "-2", (string)motion["state"], (string)motion["bundle"], (string)motion["asset"], (string)motion["ikBundle"], (string)motion["ikAsset"],
                (string)motion["shakeBundle"], (string)motion["shakeAsset"], "", "", ((int)motion["layerNo"]).ToString(CultureInfo.InvariantCulture) };
        }
    }
}
