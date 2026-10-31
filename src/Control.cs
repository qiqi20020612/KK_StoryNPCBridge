using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.AI;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        static string Text(Dictionary<string, object> d, string key)
        {
            object value;
            if (!d.TryGetValue(key, out value) || !(value is string) || ((string)value).Length == 0)
                throw new BridgeException("invalid_argument", key + " must be a nonempty string");
            return (string)value;
        }
        static double Number(object value, double min, double max, string name)
        {
            if (!(value is long) && !(value is int) && !(value is double) && !(value is float)) throw new BridgeException("invalid_argument", name + " must be numeric");
            double n = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (double.IsNaN(n) || double.IsInfinity(n) || n < min || n > max) throw new BridgeException("invalid_argument", name + " outside " + min + ".." + max);
            return n;
        }
        static bool Boolean(object value, string name)
        { if (!(value is bool)) throw new BridgeException("invalid_argument", name + " must be boolean"); return (bool)value; }
        static Dictionary<string, object> Object(Dictionary<string, object> d, string key)
        {
            object value;
            if (!d.TryGetValue(key, out value) || !(value is Dictionary<string, object>)) throw new BridgeException("invalid_argument", key + " object required");
            return (Dictionary<string, object>)value;
        }
        static Vector3 Position(Dictionary<string, object> d)
        {
            object value; if (!d.TryGetValue("position", out value)) throw new BridgeException("invalid_argument", "position required");
            var items = value as IList; if (items == null || items.Count != 3) throw new BridgeException("invalid_argument", "position must be [x,y,z]");
            return new Vector3((float)Number(items[0], -10000, 10000, "x"), (float)Number(items[1], -10000, 10000, "y"), (float)Number(items[2], -10000, 10000, "z"));
        }
        void ValidateFace(Dictionary<string, object> changes)
        {
            foreach (var p in changes)
            {
                if (!IsFaceField(p.Key)) throw new BridgeException("unsupported_field", p.Key);
                string unavailable = FaceControlUnavailable(p.Key);
                if (unavailable != null) throw new BridgeException("unsupported_control", p.Key + ": " + unavailable);
                if (p.Key == "blink" || p.Key == "mouthFixed" || p.Key == "highlight") Boolean(p.Value, p.Key);
                else if (p.Key == "eyesOpen" || p.Key == "eyebrowOpen" || p.Key == "mouthOpen" || p.Key == "blush") Number(p.Value, 0, 1, p.Key);
                else if (p.Key == "gaze") GazeId(p.Value);
                else if (p.Key == "tears") { double n = Number(p.Value, 0, 3, p.Key); if (n != Math.Floor(n)) throw new BridgeException("invalid_argument", "tears must be an integer level 0..3"); }
                else
                {
                    int count = p.Key == "eyes" ? ownedCha.eyesCtrl.GetMaxPtn() : p.Key == "eyebrow" ? ownedCha.eyebrowCtrl.GetMaxPtn() : ownedCha.mouthCtrl.GetMaxPtn();
                    double n = Number(p.Value, 0, count - 1, p.Key);
                    if (n != Math.Floor(n)) throw new BridgeException("invalid_argument", "Pattern must be an integer");
                }
            }
        }
        void RequireActor()
        {
            if (!ownedReady || ownedRoot == null || !ownedRoot.activeInHierarchy || ownedCha == null || ownedMap != map || SceneBusy || Mode != "roaming")
                throw new BridgeException("not_loaded", "The plugin NPC must be ready on the current roaming map");
        }
        void RequireOwner(Session session)
        { if (ownedController != session) throw new BridgeException("not_owner", "Claim the plugin NPC before changing her"); }

        object Execute(Dictionary<string, object> d, Session session)
        {
            string op = Text(d, "op");
            if (op == "status" || op == "list") return Snapshot();
            if (op == "stat_catalog") return ReadStatCatalog();
            if (op == "time_catalog") return ReadTimeCatalog();
            if (op == "native_event_catalog") return ReadNativeEventCatalog();
            if (op == "sample_native_action") return SampleNativeAction();
            if (op == "catalog") return ReadCatalog();
            if (op == "map_catalog") return ReadMapCatalog();
            if (op.StartsWith("game.", StringComparison.Ordinal)) return ExecuteGameCommand(op, d);
            if (op == "panel_open" || op == "panel_close")
            { if (panelVisible != (op == "panel_open")) TogglePanel(); return Json.Map("panelVisible", panelVisible); }
            if (op == "release_all")
            { if (ownedController == session) ReleaseOwned("released", false); return Json.Map("released", true, "activityReport", CurrentActivityReport()); }
            if (op == "disconnect")
            { if (ownedController == session) ReleaseOwned("disconnected", false); return Json.Map("disconnected", true); }
            if (Text(d, "target") != ActorId) throw new BridgeException("not_plugin_actor", "Only bridge:main can be controlled; saved NPCs are excluded");
            if (op == "get") return ReadOwnedActor();
            if (op == "native_interaction_status") return ReadNativeInteraction();
            if (op == "activity_report") return ReadActivityReport(d);
            if (op == "dialogue_status") return ReadDialogue();
            if (op == "adv_sequence_status") return ReadAdvSequence();
            if (op == "native_actions") return ReadNativeActions();
            if (op == "native_plan") return ReadNativePlanning();
            if (op == "expression_catalog") return ReadExpressionCatalog();
            if (op == "expression_presets") return ReadExpressionPresets();
            if (op == "animation_catalog") return ReadAnimationCatalog();
            if (op == "spawn" || op == "despawn" || op == "respawn") throw new BridgeException("native_lifecycle", "Native NPC lifecycle is managed by story loading; reload the story to recreate her");
            object generation;
            if (!d.TryGetValue("generation", out generation) || !(generation is string)) throw new BridgeException("generation_required", "Use the current actor generation from get/list");
            if ((string)generation != ownedGeneration) throw new BridgeException("stale_generation", "Actor was recreated; refresh state before issuing another command");
            if (op == "resume") return ResumeControl(d, session);
            if (op == "claim")
            {
                if (ownedHeroine == null || !Manager.Game.IsInstance() || !ReferenceEquals(ownedSave, Manager.Game.Instance.saveData))
                    throw new BridgeException("not_ready", "Load a story before claiming its bridge NPC");
                return ClaimOwnedActor(session);
            }
            if (op == "set_stats") { RequireOwner(session); return SetOwnedStats(Object(d, "changes")); }
            if (op == "adjust_stats") { RequireOwner(session); return AdjustOwnedStats(Text(d, "mutationId"), Object(d, "deltas")); }
            if (op == "cancel_adv_sequence") { RequireOwner(session); return CancelAdvSequence(d); }
            if (op == "set_waiting_indicator") { RequireOwner(session); return SetWaitingIndicator(Boolean(d.ContainsKey("visible") ? d["visible"] : null, "visible")); }
            if (op == "trigger_native_event") { RequireOwner(session); if (Recovering) throw new BridgeException("controller_disconnected", "Resume control first"); return TriggerNativeEvent(d); }
            if (op == "release")
            {
                RequireOwner(session); object restore;
                bool revert = d.TryGetValue("restorePosition", out restore) && Boolean(restore, "restorePosition");
                ReleaseOwned("released", revert); return ReadOwnedActor();
            }
            if (NativeEventBusy) throw new BridgeException("native_event_busy", "Wait for the current native activity to finish");
            bool dialogueCommand = op.StartsWith("dialogue_", StringComparison.Ordinal) || op == "model_reply" || op == "model_error" || op == "say";
            bool visualCommand = op == "set_expression" || op == "clear_expression" || op == "play_animation";
            bool talkCommand = op == "claim" || op == "say_native" || op == "play_adv_sequence" || dialogueCommand || visualCommand;
            if (ownedNpc == null || ownedNpc.AI == null || SceneBusy && !(DialogueActive && dialogueCommand) || Mode != "roaming" && !(talkCommand && Mode == "talk")) throw new BridgeException("not_ready", "This command requires story roaming or the target's native conversation");
            if (op == "dialogue_reply") { if (Recovering) throw new BridgeException("controller_disconnected", "Wait for controller recovery"); return SubmitAdvInput(d); }
            RequireOwner(session);
            if (visualCommand && AdvSequenceBusy) throw new BridgeException("adv_sequence_busy", "Wait for or cancel the ADV sequence before changing visuals");
            if (op == "play_adv_sequence") return PlayAdvSequence(d);
            if (op == "model_reply" || op == "model_error" || op == "dialogue_respond" || op == "dialogue_fail") return ReceiveExternalReply(d, op == "model_error" || op == "dialogue_fail");
            if (op == "say_native") { if (DialogueActive) throw new BridgeException("managed_dialogue", "Use model_reply or say in an active ADV conversation"); return SayNative(Text(d, "text")); }
            if (op == "dialogue_close" || op == "dialogue_retry" || op == "dialogue_request_input" || op == "dialogue_wait_model" || op == "dialogue_wait") return DialogueControl(op, d);
            if (op == "dialogue_open") return OpenAdvDialogue(d);
            if (op == "say")
            {
                RequireAfterInteraction(d);
                if (!DialogueActive) return OpenAdvDialogue(d);
                if (dialoguePhase != "ready" && dialoguePhase != "awaiting_input") throw new BridgeException("dialogue_busy", "Wait for the current dialogue turn");
                string text = Text(d, "text"); ValidateDialogueText(text); dialogueTurn++;
                QueueAdvLine("npc", text, OptionalBool(d, "requestInput", true) ? "awaiting_input" : "ready"); SetDialoguePhase("npc_speaking"); return ReadDialogue();
            }
            if (op == "set_action" || op == "queue_action" || op == "set_desire" || op == "clear_queue" || op == "native_talk" || op == "summon" || op == "walk_to_player") return ExecuteNativeCommand(op, d);
            if (visualCommand) RequireVisualActor(); else RequireActor();
            if (op == "set_expression" || op == "clear_expression") return SetOwnedExpression(d, op == "clear_expression");
            if (op == "turn")
            {
                object yaw; if (!d.TryGetValue("yaw", out yaw)) throw new BridgeException("invalid_argument", "yaw required");
                float angle = (float)Number(yaw, -3600, 3600, "yaw"); StopOwnedMotion("cancelled");
                ownedRoot.transform.rotation = Quaternion.Euler(0, angle, 0);
                if (idlePoint != null)
                { idlePoint.transform.rotation = ownedRoot.transform.rotation; HarmonyLib.AccessTools.Field(typeof(ActionGame.WaitPoint), "_angle").SetValue(idlePoint, null); }
                return ReadOwnedActor();
            }
            if (op == "stop") { StopOwnedMotion("cancelled"); return ReadOwnedActor(); }
            if (op == "play_animation")
            {
                object reference; if (!d.TryGetValue("state", out reference)) throw new BridgeException("invalid_argument", "state reference required");
                string state = AnimationState(reference);
                if (!ownedCha.animBody.HasState(0, Animator.StringToHash(state))) throw new BridgeException("unknown_animation", state);
                if (Mode == "roaming") StopOwnedMotion("cancelled"); PlayOwnedAnimation(state); ownedState = "animation"; return ReadOwnedActor();
            }
            if (op == "move" || op == "teleport") return MoveOwned(d, op);
            throw new BridgeException("unsupported_operation", op);
        }
        object MoveOwned(Dictionary<string, object> d, string operation)
        {
            Vector3 desired = Position(d); NavMeshHit hit;
            object speedValue;
            float speed = d.TryGetValue("speed", out speedValue) ? (float)Number(speedValue, 0.1, 5, "speed") : 1.2f;
            if (!NavMesh.SamplePosition(desired, out hit, 1.5f, ownedAgent.areaMask)) throw new BridgeException("invalid_destination", "No nearby walkable point");
            var path = new NavMeshPath();
            if (!ownedAgent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) throw new BridgeException("unreachable", "Destination is not reachable on this map");
            StopOwnedMotion("cancelled");
            ownedNpc.Pause(true);
            if (operation == "teleport")
            {
                if (!ownedAgent.Warp(hit.position)) throw new BridgeException("warp_failed", "Game rejected the warp");
                ownedNpc.position = hit.position;
                return ReadOwnedActor();
            }
            ownedAgent.speed = speed;
            if (!ownedAgent.SetPath(path)) throw new BridgeException("path_failed", "Navigation rejected the path");
            ownedAgent.isStopped = false; ownedMoving = true; ownedState = "moving";
            ownedMoveId = Guid.NewGuid().ToString("N"); moveDeadline = Time.realtimeSinceStartup + 30;
            PlayOwnedAnimation("Locomotion"); SetLocomotionSpeed(speed);
            return Json.Map("status", "running", "moveId", ownedMoveId, "destination", Vector(hit.position));
        }
    }
}
