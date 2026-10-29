using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ActionGame;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        static readonly string[] ActionKeys = { "change_clothes", "use_toilet", "shower", "eat", "solo_intimacy", "seek_player_intimacy", "club_activity", "talk_to_npc", "talk_to_player", "read", "use_phone", "play_phone_game", "take_selfie", "listen_to_music", "practice_dance", "groom", "drink", "take_break", "exercise", "personality_activity", "flee", "study", "sleep", "follow_player", "respond_to_call", "shy_response", "paired_intimacy", "paired_intimacy_partner", "await_talk_response", "await_intimacy_response", "hesitate" };
        static readonly string[] ActionNames = { "Change clothes", "Use toilet", "Take a shower", "Eat", "Solo intimate activity", "Seek intimacy with player", "Club activity", "Talk to another NPC", "Talk to player", "Read", "Use phone", "Play phone game", "Take a selfie", "Listen to music", "Practice dancing", "Groom appearance", "Have a drink", "Take a break", "Exercise", "Personality-specific activity", "Flee", "Study", "Sleep", "Follow player", "Respond to player call", "Shy response", "Paired intimate activity", "Paired intimate activity partner", "Wait for talk response", "Wait for intimacy response", "Hesitant idle" };
        static string ActionName(int id) { return id == -1 ? "Bridge idle" : id >= 0 && id < ActionNames.Length ? ActionNames[id] : "Native action " + id; }
        Dictionary<string, object> ActionReference(int id)
        {
            string nativeName = null, layer = null;
            if (action != null && action.actCtrl != null)
            {
                var infos = (IDictionary)AccessTools.Field(typeof(ActionControl), "dicBaseInfo").GetValue(action.actCtrl);
                object info = infos[id];
                if (info != null) { nativeName = Convert.ToString(AccessTools.Field(info.GetType(), "name").GetValue(info)); layer = (string)AccessTools.Field(info.GetType(), "layer").GetValue(info); }
            }
            return Json.Map("id", id, "key", id == -1 ? "bridge_idle" : id >= 0 && id < ActionKeys.Length ? ActionKeys[id] : "native_action/" + id,
                "name", ActionName(id), "description", id == -1 ? "Plugin-controlled idle. Use stop rather than scheduling action -1." : "Native story scheduler action. Execution depends on time, available points, targets and game conditions.",
                "nativeName", nativeName, "layer", layer);
        }
        Dictionary<string, object> MapReference(int id)
        {
            MapInfo.Param info = null;
            if (action != null && action.Map != null && action.Map.infoDic != null) action.Map.infoDic.TryGetValue(id, out info);
            return Json.Map("id", id, "key", id == -1 ? "automatic" : "map/" + id, "name", MapEnglishName(id), "nativeName", info == null ? null : info.MapName,
                "description", id == -1 ? "Let the native scheduler choose a suitable map." : "Native story or event map. Check canSchedule before using it as a task destination.",
                "outdoors", info == null ? null : (object)info.isOutdoors, "canSchedule", id == -1 || action != null && action.Map != null && action.Map.calcGateDic.ContainsKey(id));
        }
        static readonly Dictionary<int, string> MapNames = new Dictionary<int, string> {
            { -1, "Automatic map selection" },
            { 0, "Player room" },
            { 2, "School first floor" },
            { 3, "School second floor" },
            { 4, "School third floor" },
            { 5, "Classroom 1-1" },
            { 8, "Classroom 2-1" },
            { 9, "Classroom 2-2" },
            { 11, "Classroom 3-1" },
            { 14, "Girls restroom, first floor" },
            { 15, "Girls restroom, second floor" },
            { 16, "Girls restroom, third floor" },
            { 17, "Staff room" },
            { 18, "Male staff restroom" },
            { 20, "Infirmary" },
            { 21, "Library" },
            { 22, "Club room" },
            { 26, "Manga club room" },
            { 28, "Japanese-style room" },
            { 31, "Gym storage" },
            { 32, "Gymnasium" },
            { 33, "Courtyard" },
            { 34, "Athletic field" },
            { 36, "Rooftop" },
            { 37, "Swimming pool" },
            { 38, "Cafeteria" },
            { 45, "Shower room" },
            { 46, "Locker room" },
            { 47, "Backyard" },
            { 50, "Gymnasium live performance" },
            { 51, "Restroom event view" },
            { 52, "Shower event view" },
            { 53, "Locker room event view" },
            { 60, "Love hotel" },
            { 61, "Karaoke" },
            { 62, "Church" },
            { 63, "Crowded train" },
            { 64, "Train" },
            { 65, "Park" },
            { 66, "Church event" },
            { 70, "Equipment storage" },
        };
        static string MapEnglishName(int id) { string name; return MapNames.TryGetValue(id, out name) ? name : "Native map " + id; }
        object ReadMapCatalog()
        {
            var result = new List<object>(); result.Add(MapReference(-1));
            if (action == null || action.Map == null || action.Map.infoDic == null) return result;
            var ids = new List<int>(action.Map.infoDic.Keys); ids.Sort();
            foreach (int id in ids) result.Add(MapReference(id));
            return result;
        }
        int ReferenceId(object value, string kind, Func<int, Dictionary<string, object>> describe, Func<string, int> lookup)
        {
            var reference = value as Dictionary<string, object>;
            if (reference != null)
            {
                object id, key;
                bool hasId = reference.TryGetValue("id", out id), hasKey = reference.TryGetValue("key", out key);
                if (!hasId && !hasKey) throw new BridgeException("invalid_argument", kind + " reference requires id or key");
                int result = hasId ? ReferenceId(id, kind, describe, lookup) : ReferenceId(key, kind, describe, lookup);
                if (hasKey && (!(key is string) || (string)describe(result)["key"] != (string)key)) throw new BridgeException("reference_mismatch", kind + " id and key do not match");
                return result;
            }
            if (value is string)
            {
                int found = lookup((string)value);
                if (found == int.MinValue) throw new BridgeException("unknown_reference", "Unknown " + kind + " key; use catalog");
                return found;
            }
            double n = Number(value, int.MinValue + 1d, int.MaxValue, kind);
            if (n != Math.Floor(n)) throw new BridgeException("invalid_argument", kind + " id must be an integer");
            int number = (int)n;
            if (lookup((string)describe(number)["key"]) != number) throw new BridgeException("unknown_reference", "Unknown " + kind + " id; use catalog");
            return number;
        }
        int ActionId(object value)
        {
            return ReferenceId(value, "action", ActionReference, delegate(string key) {
                if (action == null || action.actCtrl == null) return int.MinValue;
                var infos = (IDictionary)AccessTools.Field(typeof(ActionControl), "dicBaseInfo").GetValue(action.actCtrl);
                foreach (DictionaryEntry item in infos) if ((string)ActionReference((int)item.Key)["key"] == key) return (int)item.Key;
                return int.MinValue;
            });
        }
        int MapId(object value)
        {
            return ReferenceId(value, "map", MapReference, delegate(string key) {
                if (key == "automatic") return -1;
                if (action != null && action.Map != null && action.Map.infoDic != null) foreach (int id in action.Map.infoDic.Keys) if ((string)MapReference(id)["key"] == key) return id;
                return int.MinValue;
            });
        }
        FBSBase FaceController(string field)
        { return ownedCha == null ? null : field == "eyes" ? (FBSBase)ownedCha.eyesCtrl : field == "eyebrow" ? (FBSBase)ownedCha.eyebrowCtrl : ownedCha.mouthCtrl; }
        Dictionary<string, object> ExpressionReference(string field, int id)
        {
            FBSBase controller = FaceController(field);
            var shapes = new List<object>();
            if (controller != null && controller.FBSTarget != null) foreach (var target in controller.FBSTarget)
            {
                if (target == null || target.PtnSet == null || id < 0 || id >= target.PtnSet.Length || target.ObjTarget == null) continue;
                var renderer = target.ObjTarget.GetComponent<SkinnedMeshRenderer>();
                if (renderer == null || renderer.sharedMesh == null) continue;
                Mesh mesh = renderer.sharedMesh; var pattern = target.PtnSet[id];
                if ((object)pattern == null) continue;
                shapes.Add(Json.Map("mesh", mesh.name, "closed", pattern.Close >= 0 && pattern.Close < mesh.blendShapeCount ? mesh.GetBlendShapeName(pattern.Close) : null,
                    "open", pattern.Open >= 0 && pattern.Open < mesh.blendShapeCount ? mesh.GetBlendShapeName(pattern.Open) : null));
            }
            return Json.Map("id", id, "key", field + "/" + id, "field", field, "name", ExpressionEnglishName(field, id, shapes),
                "description", "Native facial blend-shape pattern for this actor. See morphTargets for the exact open and closed mesh targets. Use the same field when applying this reference.", "morphTargets", shapes);
        }
        static readonly Dictionary<string, string> ExpressionNames = new Dictionary<string, string> {
            { "eyes:def_cl|def_op", "Neutral" },
            { "eyes:def_cl|def_cl", "Closed neutral eyes" },
            { "eyes:egao_cl|egao_op", "Smiling eyes" },
            { "eyes:egao_cl|egao_cl", "Closed smiling eyes" },
            { "eyes:egao_cl|bisyou_op", "Soft smile" },
            { "eyes:winkl_op|winkl_op", "Left wink" },
            { "eyes:winkr_op|winkr_op", "Right wink" },
            { "eyes:def_cl|setunai_op", "Wistful" },
            { "eyes:def_cl|tere_op", "Embarrassed" },
            { "eyes:def_cl|ikari_op", "Angry" },
            { "eyes:def_cl|sinken_op", "Serious" },
            { "eyes:def_cl|tumara_op", "Bored" },
            { "eyes:def_cl|kurusi_op", "Pained" },
            { "eyes:def_cl|keno_op", "Disgusted" },
            { "eyes:def_cl|sian_op", "Thoughtful" },
            { "eyes:def_cl|kanasi_op", "Sad" },
            { "eyes:def_cl|naki_op", "Crying" },
            { "eyes:def_cl|aseri_op", "Anxious" },
            { "eyes:def_cl|rakutan_op", "Dejected" },
            { "eyes:def_cl|komaru_op", "Troubled" },
            { "eyes:def_cl|doya_op", "Smug" },
            { "eyes:gag|gag", "Comical eyes" },
            { "eyes:gyul_op|gyul_op", "Left eye squeeze" },
            { "eyes:gyur_op|gyur_op", "Right eye squeeze" },
            { "eyes:gyu_cl|gyu_cl", "Both eyes squeezed shut" },
            { "eyes:gyul02_op|gyul02_op", "Left eye squeeze, variant 2" },
            { "eyes:gyur02_op|gyur02_op", "Right eye squeeze, variant 2" },
            { "eyes:gyu02_cl|gyu02_cl", "Both eyes squeezed shut, variant 2" },
            { "eyebrow:def_cl|def_op", "Neutral eyebrows" },
            { "eyebrow:oko_cl|oko_op", "Angry eyebrows" },
            { "eyebrow:koma_cl|koma_op", "Troubled eyebrows" },
            { "eyebrow:tmara_cl|tmara_op", "Bored eyebrows" },
            { "eyebrow:gimoL_cl|gimoL_op", "Questioning, left" },
            { "eyebrow:gimoR_cl|gimoR_op", "Questioning, right" },
            { "eyebrow:sianL_cl|sianL_op", "Thinking, left" },
            { "eyebrow:sianR_cl|sianR_op", "Thinking, right" },
            { "eyebrow:oko2L_cl|oko2L_op", "Angry, left" },
            { "eyebrow:oko2R_cl|oko2R_op", "Angry, right" },
            { "eyebrow:sinken_cl|sinken_op", "Serious eyebrows" },
            { "eyebrow:huan_cl|huan_op", "Worried eyebrows" },
            { "eyebrow:odoro_cl|odoro_op", "Surprised eyebrows" },
            { "eyebrow:rakutan_cl|rakutan_op", "Dejected eyebrows" },
            { "eyebrow:doya_cl|doya_op", "Smug eyebrows" },
            { "eyebrow:winkl_cl|winkl_op", "Left-wink eyebrows" },
            { "eyebrow:winkr_cl|winkr_op", "Right-wink eyebrows" },
            { "mouth:def_cl|def_op", "Neutral mouth" },
            { "mouth:bisyou_op|egao_op", "Smile" },
            { "mouth:uresi_cl|uresi_op", "Joyful smile" },
            { "mouth:bisyou_op|uresi_s_op", "Small joyful smile" },
            { "mouth:bisyou_op|uresi_ss_op", "Tiny joyful smile" },
            { "mouth:doki_cl|doki_s_op", "Flustered mouth" },
            { "mouth:doki_s_cl|doki_s_op", "Small flustered mouth" },
            { "mouth:doki_ss_cl|doki_ss_op", "Tiny flustered mouth" },
            { "mouth:ikari_cl|ikari_op", "Angry mouth" },
            { "mouth:ikari_cl|ikari02_op", "Angry mouth, variant 2" },
            { "mouth:doki_ss_cl|sinken02_op", "Serious narrow mouth" },
            { "mouth:sinken03_cl|sinken03_op", "Serious pursed mouth" },
            { "mouth:sinken03_cl|keno_op", "Disgusted mouth" },
            { "mouth:doki_ss_cl|sabisi_op", "Lonely mouth" },
            { "mouth:sinken03_cl|aseri_op", "Anxious mouth" },
            { "mouth:human_cl|o_l_op", "Dissatisfied open mouth" },
            { "mouth:doki_ss_cl|akire_op", "Exasperated mouth" },
            { "mouth:def_cl|odoro_op", "Surprised open mouth" },
            { "mouth:def_cl|odoro_s_op", "Small surprised mouth" },
            { "mouth:doya_cl|doya_op", "Smug smile" },
            { "mouth:pero_cl|pero_cl", "Tongue out" },
            { "mouth:def_cl|name_op", "Licking mouth shape" },
            { "mouth:def_cl|kuwae_op", "Rounded holding mouth shape" },
            { "mouth:kisu_op|kisu_op", "Puckered lips" },
            { "mouth:name02_op|name02_op", "Licking mouth shape, variant 2" },
            { "mouth:n_s_cl|a_s_op", "Vowel A, small" },
            { "mouth:n_l_cl|a_l_op", "Vowel A, large" },
            { "mouth:n_s_cl|i_s_op", "Vowel I, small" },
            { "mouth:n_l_cl|i_l_op", "Vowel I, large" },
            { "mouth:n_s_cl|u_s_op", "Vowel U, small" },
            { "mouth:n_l_cl|u_l_op", "Vowel U, large" },
            { "mouth:n_s_cl|e_s_op", "Vowel E, small" },
            { "mouth:n_l_cl|e_l_op", "Vowel E, large" },
            { "mouth:n_s_cl|o_s_op", "Vowel O, small" },
            { "mouth:n_l_cl|o_l_op", "Vowel O, large" },
            { "mouth:n_s_cl|n_s_cl", "Closed consonant mouth, small" },
            { "mouth:n_l_cl|n_l_cl", "Closed consonant mouth, large" },
            { "mouth:neko_cl|neko_op", "Cat-like mouth" },
            { "mouth:san_op|san_op", "Triangular mouth" },
            { "mouth:niko_cl|niko_op", "Beaming smile" },
            { "mouth:mogu_op|tabe_op", "Eating mouth" },
            { "mouth:mogu_cl|mogu_op", "Chewing mouth" },
            { "mouth:human_cl|human_cl", "Dissatisfied closed mouth" },
        };
        static string MorphSuffix(string shape)
        {
            if (shape == null) return "";
            int dot = shape.IndexOf('.'); string part = dot < 0 ? shape : shape.Substring(dot + 1);
            int underscore = part.IndexOf('_'); return underscore < 0 ? part : part.Substring(underscore + 1);
        }
        static string ExpressionEnglishName(string field, int id, List<object> shapes)
        {
            if (shapes.Count > 0)
            {
                var shape = (Dictionary<string, object>)shapes[0]; string name;
                if (ExpressionNames.TryGetValue(field + ":" + MorphSuffix((string)shape["closed"]) + "|" + MorphSuffix((string)shape["open"]), out name)) return name;
            }
            return "Unlabeled " + field + " pattern " + id + " (see exact morph targets)";
        }
        int ExpressionId(string field, object value)
        {
            var reference = value as Dictionary<string, object>; object domain;
            if (reference != null && reference.TryGetValue("field", out domain) && !Equals(domain, field)) throw new BridgeException("reference_mismatch", "Expression belongs to another field");
            FBSBase controller = FaceController(field);
            if (controller == null) throw new BridgeException("not_loaded", "Load the actor to resolve expression patterns");
            return ReferenceId(value, "expression", delegate(int id) { return ExpressionReference(field, id); }, delegate(string key) {
                for (int id = 0; id < controller.GetMaxPtn(); id++) if (key == field + "/" + id) return id;
                return int.MinValue;
            });
        }
        Dictionary<string, object> NormalizeFace(Dictionary<string, object> changes)
        {
            var result = new Dictionary<string, object>(changes);
            foreach (string field in new string[] { "eyes", "eyebrow", "mouth" }) if (result.ContainsKey(field)) result[field] = ExpressionId(field, result[field]);
            return result;
        }
        object ReadExpressionCatalog()
        {
            var result = Json.Map("available", FaceReady(ownedCha), "scope", "current_actor", "generation", ownedGeneration);
            foreach (string field in new string[] { "eyes", "eyebrow", "mouth" })
            {
                var rows = new List<object>(); FBSBase controller = FaceController(field);
                if (controller != null) for (int id = 0; id < controller.GetMaxPtn(); id++)
                {
                    var row = ExpressionReference(field, id);
                    row["request"] = Json.Map("op", "set_expression", "target", ActorId, "generation", ownedGeneration, "changes", Json.Map(field, Json.Map("id", id, "key", row["key"], "field", field)));
                    rows.Add(row);
                }
                result[field] = rows;
            }
            result["controls"] = new object[] {
                Json.Map("key", "eyesOpen", "name", "Eye opening limit", "type", "number", "min", 0, "max", 1, "description", "Maximum eyelid opening; 0 closed, 1 fully open."),
                Json.Map("key", "mouthOpen", "name", "Mouth opening", "type", "number", "min", 0, "max", 1, "description", "Mouth opening; 0 closed, 1 fully open."),
                Json.Map("key", "blink", "name", "Automatic blinking", "type", "boolean", "description", "Enable or disable the native blink controller.") };
            return result;
        }
        object ReadExpressionDetails()
        {
            if (!FaceReady(ownedCha)) return null;
            return Json.Map("eyes", ExpressionReference("eyes", ownedCha.GetEyesPtn()), "eyebrow", ExpressionReference("eyebrow", ownedCha.GetEyebrowPtn()), "mouth", ExpressionReference("mouth", ownedCha.GetMouthPtn()));
        }
        List<string> AnimationStates()
        {
            var names = new List<string>(new string[] { "Idle", "Locomotion", "Locomotion 0", "Locomotion_Anger", "talk", "talk_uke", "Phone3", "Dinner", "Dinner2" });
            if (ownedNpc != null && !string.IsNullOrEmpty(ownedNpc.motion.state)) { if (!names.Contains(ownedNpc.motion.state)) names.Add(ownedNpc.motion.state); }
            if (action != null && action.PosSet != null && action.PosSet.waitPointDic != null)
                foreach (var points in action.PosSet.waitPointDic.Values) foreach (var point in points) if (point != null && point.parameterList != null)
                    foreach (var parameter in point.parameterList) if (parameter.motionList != null) foreach (var motion in parameter.motionList)
                        if (!string.IsNullOrEmpty(motion.motion) && !names.Contains(motion.motion)) names.Add(motion.motion);
            names.Sort(); return names;
        }
        static string AnimationName(string state)
        {
            if (state == "Idle" || state == "kotei_tachi") return "Standing idle (" + state + ")";
            if (state == "suwari_Idle") return "Seated idle";
            if (state == "talk") return "Conversation, speaking";
            if (state == "talk_uke") return "Conversation, listening";
            if (state.StartsWith("Locomotion", StringComparison.Ordinal)) return "Walking locomotion (" + state + ")";
            string[] prefixes = { "Appearance", "ChangeMind", "Club_Cheer", "Club_Comic", "Club_Koikatu", "Club_Land", "Club_Swim", "Club_Tea", "Dance", "Dinner", "Drink", "Game", "Music", "Phone", "Reading", "Shooting", "Shower", "Sleep", "Sport", "Study", "Toilet", "Lesbian", "Masturbation", "Houshi" };
            string[] names = { "Grooming", "Taking a break", "Cheer club practice", "Manga club activity", "Koikatsu club activity", "Athletics club practice", "Swimming club practice", "Tea ceremony club activity", "Dance practice", "Eating a meal", "Drinking", "Playing a phone game", "Listening to music", "Using a phone", "Reading", "Taking a selfie", "Showering", "Sleeping", "Exercising", "Studying", "Using a toilet", "Paired intimate activity", "Solo intimate activity", "Intimate activity" };
            for (int i = 0; i < prefixes.Length; i++) if (state.StartsWith(prefixes[i], StringComparison.Ordinal)) return names[i] + " (" + state + ")";
            return "Native animation " + state;
        }
        Dictionary<string, object> AnimationReference(string state)
        { return Json.Map("id", Animator.StringToHash(state), "key", state, "state", state, "name", AnimationName(state),
            "description", "Animator base-layer state. This changes animation only, without running the corresponding native gameplay action.", "layer", 0); }
        object ReadAnimationCatalog()
        {
            var result = new List<object>();
            if (ownedCha == null || ownedCha.animBody == null) return result;
            foreach (string state in AnimationStates()) if (ownedCha.animBody.HasState(0, Animator.StringToHash(state)))
            {
                var row = AnimationReference(state);
                row["request"] = Json.Map("op", "play_animation", "target", ActorId, "generation", ownedGeneration, "state", Json.Map("id", row["id"], "key", state)); result.Add(row);
            }
            return result;
        }
        string AnimationState(object value)
        {
            var reference = value as Dictionary<string, object>; object key, id;
            if (reference != null)
            {
                if (reference.TryGetValue("key", out key) || reference.TryGetValue("state", out key))
                {
                    string state = AnimationState(key);
                    if (reference.TryGetValue("id", out id) && Number(id, int.MinValue, int.MaxValue, "animation id") != Animator.StringToHash(state)) throw new BridgeException("reference_mismatch", "Animation id and key do not match");
                    return state;
                }
                if (!reference.TryGetValue("id", out id)) throw new BridgeException("invalid_argument", "Animation reference requires key, state or id");
                return AnimationState(id);
            }
            if (value is string)
            {
                string state = (string)value;
                if (ownedCha.animBody.HasState(0, Animator.StringToHash(state))) return state;
            }
            else
            {
                double hash = Number(value, int.MinValue, int.MaxValue, "animation id");
                foreach (string state in AnimationStates()) if (Animator.StringToHash(state) == hash && ownedCha.animBody.HasState(0, (int)hash)) return state;
            }
            throw new BridgeException("unknown_animation", "Use animation_catalog to select an available Animator state");
        }
        object ReadCatalog()
        {
            return Json.Map("catalogVersion", 1, "descriptionLanguage", "en", "generation", ownedGeneration, "actions", ReadNativeActions(), "maps", ReadMapCatalog(), "expressions", ReadExpressionCatalog(), "animations", ReadAnimationCatalog(),
                "idDomains", Json.Map("actions", "Native story scheduler action IDs, shared by set_action and queue_action.", "expressions", "Actor-specific FBS pattern indices; eyes, eyebrow and mouth are separate domains.", "animations", "Signed Animator.StringToHash state IDs on layer 0; distinct from scheduler action IDs.", "maps", "Native map IDs; -1 means automatic selection in task commands."),
                "ui", Json.Map("defaultLanguage", "en", "languages", new string[] { "en", "zh-CN" }, "configSection", "UI", "configKey", "Language", "description", "Only plugin debug and essential dialogue UI are localized. API descriptions remain English."),
                "referenceInput", "Pass a returned entry, its numeric id, or its key. Actor writes require claim and the current generation. Requests in catalog entries are ready to submit after claiming the actor.");
        }
    }
}
