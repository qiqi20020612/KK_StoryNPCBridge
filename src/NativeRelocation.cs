using System;
using ActionGame;
using ActionGame.Chara;
using UnityEngine;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        sealed class NativeRelocation
        {
            internal string Id = Guid.NewGuid().ToString("N"), Generation, World, State = "pending", Error;
            internal int FromStage, ToStage, FromMap, Destination = -1;
            internal float ReadyDeadline;
            internal object Actual;
        }
        sealed class NativePlacementScope
        {
            internal NativeRelocation Run;
            internal bool PreviousPlacement;
        }
        NativeRelocation nativeRelocation;
        bool nativeWorldPlacement;
        void ResetNativeRelocation() { nativeRelocation = null; nativeWorldPlacement = false; }
        object ReadNativeRelocation()
        {
            var run = nativeRelocation; if (run == null) return null;
            return Json.Map("relocationId", run.Id, "generation", run.Generation, "world", run.World, "state", run.State,
                "reason", "native_stage_changed", "source", "native_cycle_first_action", "fromStage", StageReference(run.FromStage), "toStage", StageReference(run.ToStage),
                "fromMap", run.FromMap, "fromMapInfo", MapReference(run.FromMap), "destinationMap", run.Destination, "destinationMapInfo", MapReference(run.Destination),
                "actual", run.Actual, "error", run.Error);
        }
        void RequestNativeRelocation(int before, int after)
        {
            if (ownedController == null) return;
            if (nativeRelocation != null && nativeRelocation.State == "pending" && nativeRelocation.World == world && nativeRelocation.Generation == ownedGeneration)
            { nativeRelocation.ToStage = after; return; }
            EndNativeRelocation("cancelled", "superseded_by_native_stage");
            nativeRelocation = new NativeRelocation { Generation = ownedGeneration, World = world,
                FromStage = before, ToStage = after, FromMap = ownedNpc == null ? ownedMap : ownedNpc.mapNo };
            AddEvent("native_relocation_requested", ReadNativeRelocation());
        }
        void InterruptForNativeStage()
        {
            const string reason = "native_stage_changed";
            if (ownedMoving) AddEvent("move_cancelled", Json.Map("moveId", ownedMoveId, "reason", reason));
            ownedMoving = false; FinishApproach("cancelled", reason);
            if (activeTask != null)
            { var data = (System.Collections.Generic.Dictionary<string, object>)activeTask.Read(); data["reason"] = reason; AddEvent("native_action_cancelled", data); }
            foreach (var task in nativeQueue)
            { var data = (System.Collections.Generic.Dictionary<string, object>)task.Read(); data["reason"] = reason; AddEvent("native_action_cancelled", data); }
            activeTask = null; nativeQueue.Clear(); EndOwnedNativeFollow(); animationOverride = false; stopOnRoam = false;
            if (DialogueActive) ResetAdvDialogue(reason);
            if (NativeEventBusy) FinishNativeEvent("cancelled", reason);
            ClearAdvControls();
            if (ownedAgent != null && ownedAgent.enabled && ownedAgent.isOnNavMesh) { ownedAgent.isStopped = true; ownedAgent.ResetPath(); }
        }
        static void BeforeNativeWorldPlacement(ActionScene __instance, bool __0, ref NativePlacementScope __state)
        {
            var p = Current;
            if (!__0 || p == null || p.action != __instance) return;
            // Coroutine startup can reach this boundary before Cycle.Change's postfix.
            if (__instance.Cycle != null && p.observedCycle == __instance.Cycle) p.ObserveStage(__instance.Cycle);
            if (p.nativeRelocation == null || p.nativeRelocation.State != "pending" && p.nativeRelocation.State != "waiting_scene") return;
            var run = p.nativeRelocation;
            if (run.Generation != p.ownedGeneration || run.World != p.world || p.ownedController == null) return;
            __state = new NativePlacementScope { Run = run, PreviousPlacement = p.nativeWorldPlacement };
            p.nativeWorldPlacement = true; run.State = "initializing"; run.ReadyDeadline = 0;
            try { p.InterruptForNativeStage(); }
            catch (Exception e)
            {
                p.nativeWorldPlacement = false;
                p.EndNativeRelocation("failed", e.GetBaseException().Message);
                p.Logger.LogWarning("Native relocation cleanup: " + e.Message);
            }
        }
        static void NativePlacementSelected(AI __instance, ref ActionControl.ResultInfo __result)
        {
            var p = Current;
            var npc = p == null || p.ownedHeroine == null ? null : p.ownedHeroine.charaBase as NPC;
            if (p == null || !p.nativeWorldPlacement || npc == null || !ReferenceEquals(npc.AI, __instance) || p.ownedController == null || p.action == null) return;
            var run = p.nativeRelocation;
            if (run == null || run.State != "initializing" || run.Generation != p.ownedGeneration || run.World != p.world) return;
            if (__result == null || __result.point == null)
            { npc.Pause(true); p.EndNativeRelocation("failed", "native_destination_unavailable"); return; }
            // Native CoordinateSetting and initial result assignment have already run.
            try
            {
                run.Destination = __result.point.MapNo;
                var idle = p.NativeIdle(p.action.actCtrl, __result, true);
                AssignNativeResult(__instance, idle); __result = idle;
                run.State = "waiting_scene";
            }
            catch (Exception e)
            {
                npc.Pause(true);
                p.EndNativeRelocation("failed", e.GetBaseException().Message);
                p.Logger.LogWarning("Native relocation hold: " + e.Message);
            }
        }
        static Exception AfterNativeWorldPlacement(NativePlacementScope __state, Exception __exception)
        {
            var p = Current;
            if (p == null || __state == null) return __exception;
            var run = __state.Run;
            p.nativeWorldPlacement = run.Generation == p.ownedGeneration && run.World == p.world && __state.PreviousPlacement;
            if (p.nativeRelocation != run) return __exception;
            if (__exception != null) p.EndNativeRelocation("failed", __exception.GetBaseException().Message);
            else if (run.State == "initializing") p.EndNativeRelocation("failed", "native_actor_not_initialized");
            if (run.State == "failed")
            {
                // ReStart may have resumed the graph after a failed FirstAction postfix.
                try { var npc = p.ownedHeroine == null ? null : p.ownedHeroine.charaBase as NPC; if (npc != null) npc.Pause(true); }
                catch (Exception e) { p.Logger.LogWarning("Native relocation pause: " + e.Message); }
            }
            return __exception;
        }
        void EndNativeRelocation(string state, string error)
        {
            if (nativeRelocation == null || nativeRelocation.State == "completed" || nativeRelocation.State == "failed" || nativeRelocation.State == "cancelled") return;
            nativeRelocation.State = state; nativeRelocation.Error = error;
            AddEvent("native_relocation_" + state, ReadNativeRelocation());
        }
        void TickNativeRelocation()
        {
            var run = nativeRelocation;
            if (run == null || run.State == "completed" || run.State == "failed" || run.State == "cancelled") return;
            if (run.Generation != ownedGeneration || run.World != world || ownedController == null)
            { EndNativeRelocation("cancelled", "controller_or_world_changed"); return; }
            if (run.State != "waiting_scene" || nativeWorldPlacement || SceneBusy || Mode != "roaming") return;
            if (run.ReadyDeadline == 0) run.ReadyDeadline = Time.realtimeSinceStartup + 5;
            if (ownedNpc == null || ownedNpc.mapNo != run.Destination)
            { if (Time.realtimeSinceStartup >= run.ReadyDeadline) EndNativeRelocation("failed", "native_destination_not_ready"); return; }
            lastNativePosition = ownedNpc.calcPosition; claimedPosition = lastNativePosition; claimedYaw = ownedNpc.rotation.eulerAngles.y;
            if (Recovering) ownedNpc.Pause(true);
            ownedState = Recovering ? "waiting_reconnect" : "idle";
            run.Actual = Json.Map("map", ownedNpc.mapNo, "mapInfo", MapReference(ownedNpc.mapNo), "clothing", ReadClothing());
            EndNativeRelocation("completed", null);
        }
    }
}
