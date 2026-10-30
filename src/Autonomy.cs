using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using ActionGame;
using ActionGame.Chara;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        bool resumeNativePending, activityDirty, disposingOwned;
        int autonomousActionBeforeClaim = -1, omittedActivities;
        long activitySequence;
        string activityPeriodId, activityStartedUtc, activityReason, activitySignature;
        float activityStartedAt, nextActivitySample, nextActivityFlush, nextResumeAttempt;
        Dictionary<string, object> activityStartState, lastActivityState, lastHandoverReport;
        readonly List<object> uncontrolledActivities = new List<object>();
        readonly Dictionary<string, object> activityCounts = new Dictionary<string, object>();
        string ActivityPath { get { return Path.Combine(Path.GetDirectoryName(ownedStatePath), "activity-journal.json"); } }

        void InitializeAutonomyJournal()
        {
            if (!File.Exists(ActivityPath)) return;
            try
            {
                var data = Json.Parse(File.ReadAllText(ActivityPath)) as Dictionary<string, object>;
                object previous;
                if (data != null && data.TryGetValue("lastReport", out previous)) lastHandoverReport = previous as Dictionary<string, object>;
            }
            catch (Exception e) { Logger.LogWarning("Activity journal: " + e.Message); }
        }
        void BeginUncontrolledPeriod(string reason)
        {
            activityPeriodId = Guid.NewGuid().ToString("N"); activityReason = reason;
            activityStartedUtc = DateTime.UtcNow.ToString("o"); activityStartedAt = Time.realtimeSinceStartup;
            uncontrolledActivities.Clear(); activityCounts.Clear(); omittedActivities = 0; activitySignature = null;
            activityStartState = CaptureAutonomyState(); lastActivityState = activityStartState; activityDirty = true;
            AppendNativeActivity("period_started", Json.Map("reason", reason, "state", activityStartState));
        }
        Dictionary<string, object> CaptureAutonomyState()
        {
            SyncOwnedStats();
            var r = ownedNpc == null || ownedNpc.AI == null ? null : ownedNpc.AI.result;
            Base target = ownedNpc == null || ownedNpc.AI == null ? null : ownedNpc.AI.target;
            object targetData = target == null ? null : Json.Map("kind", target is Player ? "player" : "npc", "name", target.heroine != null ? target.heroine.Name : target.player != null ? target.player.Name : null);
            return Json.Map("available", ownedNpc != null, "map", ownedNpc == null ? -1 : ownedNpc.mapNo, "mapInfo", MapReference(ownedNpc == null ? -1 : ownedNpc.mapNo),
                "action", r == null ? -1 : r.actionNo, "name", r == null ? null : ActionName(r.actionNo), "nativeName", r == null ? null : r.actionName, "actionInfo", r == null ? null : ActionReference(r.actionNo),
                "plansNo", r == null ? -1 : r.plansNo, "plansName", r == null ? null : r.plansName,
                "props", ownedNpc == null ? 0 : ownedNpc.itemObjList.Count, "target", targetData,
                "talking", nativeTalkPending || currentTalk != null && ReferenceEquals(currentTalk.targetHeroine, ownedHeroine),
                "stats", ownedHeroine == null ? null : new Dictionary<string, object>(ownedStats));
        }
        void AppendNativeActivity(string kind, object data)
        {
            if (ownedController != null || activityPeriodId == null || disposingOwned) return;
            var entry = Json.Map("seq", ++activitySequence, "utc", DateTime.UtcNow.ToString("o"), "kind", kind, "data", data);
            uncontrolledActivities.Add(entry);
            if (uncontrolledActivities.Count > 256) { uncontrolledActivities.RemoveAt(0); omittedActivities++; }
            object count; activityCounts.TryGetValue(kind, out count); activityCounts[kind] = count == null ? 1 : Convert.ToInt32(count) + 1;
            activityDirty = true;
        }
        void RecordNativeSelection(ActionControl.ResultInfo result)
        {
            if (result == null || ownedController != null) return;
            resumeNativePending = false;
            AppendNativeActivity("action_selected", Json.Map("action", result.actionNo, "name", ActionName(result.actionNo), "nativeName", result.actionName, "actionInfo", ActionReference(result.actionNo), "plansNo", result.plansNo, "plansName", result.plansName,
                "destination", result.point == null ? null : Json.Map("map", result.point.MapNo, "mapInfo", MapReference(result.point.MapNo))));
        }
        void ObserveUncontrolledEvent(string kind, object data)
        {
            if (ownedController != null || activityPeriodId == null || disposingOwned) return;
            if (kind == "game_dialogue" && (nativeTalkPending || currentTalk != null && ReferenceEquals(currentTalk.targetHeroine, ownedHeroine))) AppendNativeActivity("dialogue", data);
            else if (kind.StartsWith("native_interaction_", StringComparison.Ordinal)) AppendNativeActivity(kind, data);
        }
        void SampleUncontrolledActivity()
        {
            if (ownedController != null || ownedNpc == null || activityPeriodId == null) return;
            var state = CaptureAutonomyState();
            if (activityStartState == null || !(bool)activityStartState["available"]) activityStartState = state;
            if (lastActivityState != null && (bool)lastActivityState["available"] && (int)lastActivityState["map"] != (int)state["map"])
                AppendNativeActivity("map_changed", Json.Map("fromMap", lastActivityState["map"], "toMap", state["map"], "fromMapInfo", lastActivityState["mapInfo"], "toMapInfo", state["mapInfo"]));
            var signatureState = new Dictionary<string, object>(state); signatureState.Remove("map"); signatureState.Remove("mapInfo");
            string signature = Json.Stringify(signatureState);
            if (signature != activitySignature)
            { AppendNativeActivity("state_changed", state); activitySignature = signature; }
            lastActivityState = state;
        }
        Dictionary<string, object> BuildActivityReport(bool complete)
        {
            return Json.Map("reportId", activityPeriodId, "generation", ownedGeneration, "world", world, "reason", activityReason,
                "startedUtc", activityStartedUtc, "endedUtc", complete ? DateTime.UtcNow.ToString("o") : null,
                "durationSeconds", activityPeriodId == null ? 0 : Time.realtimeSinceStartup - activityStartedAt,
                "entryCount", uncontrolledActivities.Count, "omittedEntries", omittedActivities, "counts", new Dictionary<string, object>(activityCounts),
                "startState", activityStartState, "endState", CaptureAutonomyState(), "entries", uncontrolledActivities.ToArray());
        }
        object ReadAutonomyState()
        {
            return Json.Map("mode", ownedController != null ? "controlled" : resumeNativePending ? "returning_to_game" : "native", "journalId", activityPeriodId,
                "startedUtc", ownedController == null ? activityStartedUtc : null, "entryCount", ownedController == null ? uncontrolledActivities.Count : 0,
                "omittedEntries", omittedActivities, "lastReportId", lastHandoverReport == null ? null : lastHandoverReport["reportId"],
                "lastReportSummary", HandoverSummary());
        }
        object CurrentActivityReport()
        { return ownedController == null && activityPeriodId != null ? BuildActivityReport(false) : lastHandoverReport; }
        object ReadActivityReport(Dictionary<string, object> command)
        {
            string id = OptionalText(command, "reportId", "");
            if (ownedController == null && activityPeriodId != null && (id.Length == 0 || id == activityPeriodId)) return BuildActivityReport(false);
            if (lastHandoverReport != null && (id.Length == 0 || id == (string)lastHandoverReport["reportId"])) return lastHandoverReport;
            throw new BridgeException("report_not_found", "This activity segment is no longer available");
        }
        string HandoverSummary()
        {
            if (lastHandoverReport == null) return "No uncontrolled activity report yet";
            var state = lastHandoverReport["endState"] as Dictionary<string, object>;
            string ending = state != null && state["name"] != null ? "; last action: " + state["name"] + ", map " + state["map"] : "";
            return "Uncontrolled for " + Convert.ToInt32(lastHandoverReport["durationSeconds"]) + " seconds; " + lastHandoverReport["entryCount"] + " entries" + ending +
                (Convert.ToInt32(lastHandoverReport["omittedEntries"]) > 0 ? " (earlier entries truncated)" : "");
        }
        object ClaimOwnedActor(Session session)
        {
            if (ownedController != null && ownedController != session) throw new BridgeException("owned", "Another client controls this actor");
            if (ownedController == null)
            {
                if (activityPeriodId == null) BeginUncontrolledPeriod("claim_snapshot");
                SampleUncontrolledActivity(); lastHandoverReport = BuildActivityReport(true);
                if (ownedNpc != null && ownedNpc.AI != null && ownedNpc.AI.result != null && ownedNpc.AI.result.actionNo >= 0) autonomousActionBeforeClaim = ownedNpc.AI.result.actionNo;
                ownedController = session; claimedPosition = ownedNpc != null ? ownedNpc.calcPosition : Vector3.zero; claimedYaw = ownedRoot != null ? ownedRoot.transform.eulerAngles.y : 0;
                CreateResumeToken();
                resumeNativePending = false;
                if (!SceneBusy && Mode == "roaming" && ownedNpc != null && ownedNpc.AI != null) SetNativeIdle(); else stopOnRoam = true;
                activityDirty = true; FlushAutonomyJournal();
                AddEvent("controller_claimed", Json.Map("reportId", lastHandoverReport["reportId"], "summary", HandoverSummary()));
            }
            return ControlReply();
        }
        void TryResumeNativeAutonomy()
        {
            if (!resumeNativePending || disposingOwned || ownedController != null || ownedNpc == null || ownedNpc.AI == null || action == null || SceneBusy || Mode != "roaming" || DialogueActive || Time.realtimeSinceStartup < nextResumeAttempt) return;
            try
            {
                ownedNpc.Pause(false); ownedNpc.move.isStop = false;
                if (ownedAgent != null && ownedAgent.enabled && ownedAgent.isOnNavMesh) { ownedAgent.isStopped = false; ownedAgent.ResetPath(); }
                var targets = (IDictionary)AccessTools.Field(typeof(ActionControl), "dicTarget").GetValue(action.actCtrl);
                object desire = targets[ownedHeroine];
                if (desire != null && (int)AccessTools.Property(desire.GetType(), "action").GetValue(desire, null) < 0 && autonomousActionBeforeClaim >= 0)
                    AccessTools.Property(desire.GetType(), "action").SetValue(desire, autonomousActionBeforeClaim, null);
                ownedNpc.AI.NextActionNoTarget();
                resumeNativePending = false; ownedState = "native_autonomy";
                AppendNativeActivity("resumed", CaptureAutonomyState());
            }
            catch (Exception e)
            {
                nextResumeAttempt = Time.realtimeSinceStartup + 5;
                Logger.LogWarning("Resume native AI: " + e.Message);
                AddEvent("native_resume_failed", Json.Map("error", e.Message, "retryAfterSeconds", 5));
            }
        }
        void TickAutonomyJournal()
        {
            if (ownedController == null && ownedHeroine != null && !disposingOwned)
            {
                TryResumeNativeAutonomy(); ownedState = resumeNativePending ? "returning_to_game" : "native_autonomy";
                if (Time.realtimeSinceStartup >= nextActivitySample)
                { nextActivitySample = Time.realtimeSinceStartup + .5f; SampleUncontrolledActivity(); }
            }
            if (activityDirty && Time.realtimeSinceStartup >= nextActivityFlush) FlushAutonomyJournal();
        }
        void FlushAutonomyJournal()
        {
            if (!activityDirty || string.IsNullOrEmpty(ownedStatePath)) return;
            nextActivityFlush = Time.realtimeSinceStartup + 2;
            try
            {
                string temp = ActivityPath + ".tmp";
                File.WriteAllText(temp, Json.Stringify(Json.Map("schema", 1, "actor", ActorId, "currentPeriod", ownedController == null && activityPeriodId != null ? BuildActivityReport(false) : null, "lastReport", lastHandoverReport)));
                if (File.Exists(ActivityPath)) File.Replace(temp, ActivityPath, null); else File.Move(temp, ActivityPath);
                activityDirty = false;
            }
            catch (Exception e) { Logger.LogWarning("Activity journal write: " + e.Message); }
        }
    }
}
