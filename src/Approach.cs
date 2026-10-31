using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ActionGame;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        string approachId, approachState = "none", approachError;
        float approachDeadline, approachStarted, approachEnded;
        int approachRouteMap = -1;
        bool Approaching { get { return approachState == "walking"; } }
        void AssignNativeResult(ActionControl.ResultInfo result)
        { AssignNativeResult(ownedNpc.AI, result); }
        static void AssignNativeResult(ActionGame.Chara.AI ai, ActionControl.ResultInfo result)
        {
            object reactive = AccessTools.Field(typeof(ActionGame.Chara.AI), "actResult").GetValue(ai);
            reactive.GetType().GetProperty("Value").SetValue(reactive, result, null);
        }
        object WalkToPlayer()
        {
            if (action.Player == null) throw new BridgeException("not_ready", "The player is not ready");
            SetNativeIdle();
            ActionControl.ResultInfo result;
            try { result = ApproachResult(); }
            catch { SetNativeIdle(); throw; }
            approachId = Guid.NewGuid().ToString("N"); approachState = "walking"; approachError = null;
            approachStarted = Time.realtimeSinceStartup; approachDeadline = approachStarted + 180;
            animationOverride = false; ownedNpc.Pause(false); AssignNativeResult(result);
            ownedState = "approaching_player";
            AddEvent("approach_started", ReadApproach());
            return ReadApproach();
        }
        ActionControl.ResultInfo ApproachResult()
        {
            int actionNo = ownedNpc.mapNo == map ? 23 : 20;
            var result = action.actCtrl.SetAction(ownedHeroine, actionNo, actionNo == 20 ? map : -1);
            if (result == null || result.actionNo != actionNo || actionNo == 20 && (result.point == null || result.point.MapNo != map))
                throw new BridgeException("route_unavailable", "No native route point is available on the player's map");
            result.actionTime = result.totalTime = 1000000f; approachRouteMap = map;
            return result;
        }
        void TickApproach()
        {
            if (!Approaching) return;
            if (Time.realtimeSinceStartup > approachDeadline) { FinishApproach("failed", "timeout"); SetNativeIdle(); return; }
            if (SceneBusy || Mode != "roaming") return;
            if (ownedNpc == null || action == null || action.Player == null) { FinishApproach("failed", "world_unavailable"); return; }
            if (approachRouteMap != map || ownedMap == map && ownedNpc.AI.actionNo != 23)
            {
                try { AssignNativeResult(ApproachResult()); AddEvent("approach_route_changed", ReadApproach()); }
                catch (BridgeException e) { FinishApproach("failed", e.Code); SetNativeIdle(); return; }
            }
            if (ownedMap == map && Vector3.Distance(ownedNpc.calcPosition, action.Player.position) <= 1.8f)
            { FinishApproach("arrived", null); ownedNpc.AI.NextAction(new int[0]); return; }
            ownedState = "approaching_player";
        }
        void FinishApproach(string state, string reason)
        {
            if (!Approaching) return;
            approachState = state; approachError = reason;
            approachEnded = Time.realtimeSinceStartup;
            AddEvent("approach_" + state, ReadApproach());
        }
        object ReadApproach()
        {
            return Json.Map("requestId", approachId, "state", approachState, "error", approachError,
                "elapsed", approachId == null ? 0 : (Approaching ? Time.realtimeSinceStartup : approachEnded) - approachStarted,
                "targetMap", map, "targetMapInfo", MapReference(map), "actorMap", ownedMap, "actorMapInfo", MapReference(ownedMap),
                "stage", ownedMap == map ? "near_player" : "cross_map",
                "distance", ownedNpc != null && action != null && action.Player != null && ownedMap == map ? (object)Vector3.Distance(ownedNpc.calcPosition, action.Player.position) : null);
        }
        void PlaceInitialOwnedActor()
        {
            // Initial creation placement only. The public summon operation always walks.
            UnityEngine.AI.NavMeshHit hit;
            Vector3 desired = action.Player.position + action.Player.transform.right * 1.4f;
            if (!UnityEngine.AI.NavMesh.SamplePosition(desired, out hit, 3, UnityEngine.AI.NavMesh.AllAreas)) return;
            ownedNpc.Pause(true);
            ownedNpc.mapNo = map; ownedNpc.AI.accesser.mapNo = map; ownedNpc.AI.accesser.TargetMapNo = -1;
            ownedNpc.position = hit.position; ownedMap = map;
            SetNativeIdle(); ownedNpc.ReStart(); initialSummonPending = false;
        }
    }
}
