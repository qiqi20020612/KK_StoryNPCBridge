using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        sealed class NativeEventRun
        {
            internal string Id = Guid.NewGuid().ToString("N"), Generation, World, State, Error;
            internal int Event, Result;
            internal bool Applied, NewHost, Closing;
            internal float Started;
            internal TalkScene Talk;
            internal object Before, Actual;
            internal readonly Dictionary<Canvas, bool> Canvases = new Dictionary<Canvas, bool>();
        }
        NativeEventRun nativeEventRun;
        bool nativeEventCommand, ownedNativeFollow;
        bool NativeEventBusy { get { return nativeEventRun != null && (nativeEventRun.State == "opening" || nativeEventRun.State == "preparing" || nativeEventRun.State == "running"); } }
        static readonly int[] NativeEventIds = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        static readonly string[] NativeEventKeys = { "confess", "break_up", "invite_h", "eat_lunch", "club_activity", "go_home_together", "invite_date", "study_together", "exercise_together", "invite_club", "follow_player" };
        static readonly string[] NativeEventNames = { "Confess affection", "Break up", "Invite to H", "Eat lunch together", "Do club activities together", "Go home together", "Arrange a date", "Study together", "Exercise together", "Invite to the club", "Follow the player" };
        static bool ImmediateNativeEvent(int id) { return id == 1 || id == 7 || id == 10; }
        static int NativeEventResult(int id) { return ImmediateNativeEvent(id) ? -1 : id == 2 ? 8 : id == 3 ? 2 : id == 4 ? 3 : id == 5 ? 4 : id == 6 ? 5 : id == 8 ? 6 : id == 9 ? 7 : 1; }
        void InstallNativeEventHooks()
        {
            PatchNative(typeof(TalkScene), "Start", Type.EmptyTypes, "PrepareNativeEventHost", null, null);
            PatchNative(typeof(TalkScene), "StartSelection", Type.EmptyTypes, "NativeEventSelection", null, null);
            foreach (var method in typeof(TalkScene).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (method.Name == "StartADV") harmony.Patch(method, Hook("AllowNativeEventAdv"), null, null, null, null);
            PatchNative(typeof(ActionGame.Communication.Info), "GetEventADV", new Type[] { typeof(int), typeof(int).MakeByRefType(), typeof(bool).MakeByRefType() }, "NativeEventAcceptedResult", null, null);
            PatchNative(typeof(ActionGame.Communication.Info), "GetIntroductionADV", new Type[] { typeof(bool).MakeByRefType(), typeof(int).MakeByRefType(), typeof(ActionGame.Communication.ChangeValueAbstractInfo).MakeByRefType(), typeof(bool) }, "NativeEventIntroduction", null, null);
        }
        string NativeEventUnavailable(int id)
        { return NativeEventUnavailable(id, null); }
        string NativeEventUnavailable(int id, TalkScene host)
        {
            if (action == null || ownedHeroine == null) return "story_not_ready";
            if (SceneBusy) return "scene_busy";
            bool inTalk = host != null && ReferenceEquals(host.targetHeroine, ownedHeroine) || OwnedTalkActive;
            if (Mode != "roaming" && !inTalk) return "wrong_context";
            if (ownedNpc == null || ownedMap != map || !ownedReady) return "npc_not_loaded";
            if ((id == 1 || id == 3 || id == 4 || id == 7) && ownedHeroine.relation < 1) return "requires_friend_or_girlfriend";
            if (id == 2 && !ownedHeroine.isGirlfriend) return "requires_girlfriend";
            if (id == 10 && ownedHeroine.isStaff) return "already_club_member";
            if (id == 7 && NativeDateHeroine() != null && !ReferenceEquals(NativeDateHeroine(), ownedHeroine)) return "another_date_scheduled";
            if (id == 3 && inTalk && !NativeHLocationReady(host != null ? host : currentTalk)) return "requires_native_h_location";
            int stage = (int)action.Cycle.nowType;
            if (id == 4 && stage != 5) return "requires_lunch_time";
            if (id == 4 && ownedHeroine.isLunch) return "already_had_lunch";
            if (id == 5 && stage != 8) return "requires_staff_time";
            if (id == 5 && !ownedHeroine.isStaff) return "requires_club_membership";
            if (id == 6 && stage != 9) return "requires_after_school";
            if ((id == 8 || id == 9) && stage != 5 && stage != 8 && stage != 9) return "requires_free_time";
            return null;
        }
        Dictionary<string, object> NativeEventReference(int id)
        {
            int index = Array.IndexOf(NativeEventIds, id);
            if (index < 0) throw new BridgeException("unknown_reference", "Unknown native activity event id");
            string unavailable = NativeEventUnavailable(id);
            return Json.Map("id", id, "key", NativeEventKeys[index], "name", NativeEventNames[index], "commandId", id + 10,
                "description", NativeEventDescription(id), "supported", true, "requirements", NativeEventRequirements(id),
                "executionKind", ImmediateNativeEvent(id) ? "state_effect" : "native_scene_flow",
                "available", unavailable == null, "unavailableReason", unavailable, "result", Json.Map("id", NativeEventResult(id), "key", NativeEventResult(id) == -1 ? "continue_talk" : ((ActionGame.Communication.ResultEnum)NativeEventResult(id)).ToString().ToLowerInvariant()),
                "request", Json.Map("op", "trigger_native_event", "target", ActorId, "generation", ownedGeneration, "event", Json.Map("id", id, "key", NativeEventKeys[index])));
        }
        object ReadNativeEventCatalog()
        { var rows = new List<object>(); foreach (int id in NativeEventIds) rows.Add(NativeEventReference(id)); return rows; }
        object ReadNativeEventRun()
        {
            if (nativeEventRun == null) return null;
            return Json.Map("executionId", nativeEventRun.Id, "event", NativeEventReference(nativeEventRun.Event), "state", nativeEventRun.State, "error", nativeEventRun.Error,
                "effectApplied", nativeEventRun.Applied, "nativeResult", nativeEventRun.Result, "followingPlayer", ownedNpc != null && action != null && action.Player != null && ReferenceEquals(action.Player.chaser, ownedNpc),
                "generation", nativeEventRun.Generation, "world", nativeEventRun.World, "stats", new Dictionary<string, object>(ownedStats), "before", nativeEventRun.Before, "actual", nativeEventRun.Actual);
        }
        object TriggerNativeEvent(Dictionary<string, object> command)
        {
            object reference; if (!command.TryGetValue("event", out reference)) throw new BridgeException("invalid_argument", "event reference is required");
            int id = ReferenceId(reference, "native_event", NativeEventReference, delegate(string key) {
                int index = Array.IndexOf(NativeEventKeys, key); return index < 0 ? int.MinValue : NativeEventIds[index];
            });
            if (NativeEventBusy || nativeEventRun != null && nativeEventRun.Closing && nativeEventRun.Talk != null || nativeTalkPending && !OwnedTalkActive)
                throw new BridgeException("native_event_busy", "A native activity or conversation transition is already running");
            string unavailable = NativeEventUnavailable(id);
            if (unavailable != null) throw new BridgeException("native_event_unavailable", unavailable);
            if (NativeReactionActive) throw new BridgeException("native_interaction_busy", "Wait for the native touch/look reaction to finish");
            var scenario = OwnedTalkActive ? ManagedAdvScenario() : null;
            if (scenario != null && scenario.isChoice) throw new BridgeException("choice_pending", "Finish the current native choice before triggering an activity");
            object before = CaptureNativeEventEffects();
            if (ImmediateNativeEvent(id))
            {
                nativeEventRun = new NativeEventRun { Event = id, Result = -1, Generation = ownedGeneration, World = world, Started = Time.realtimeSinceStartup, State = "running", Before = before };
                AddEvent("native_event_requested", ReadNativeEventRun());
                try
                {
                    ApplyNativeEventState(id); nativeEventRun.Applied = true; nativeEventRun.Actual = CaptureNativeEventEffects();
                    AddEvent("native_event_started", ReadNativeEventRun()); FinishNativeEvent("completed", null);
                }
                catch (Exception e) { Logger.LogWarning("Native state effect: " + e.Message); FinishNativeEvent("failed", "native_execution_failed"); }
                return ReadNativeEventRun();
            }
            SetNativeIdle(); nativeQueue.Clear(); activeTask = null; ClearFaceOverride(); animationOverride = false;
            ClearAdvControls();
            nativeEventRun = new NativeEventRun { Event = id, Result = NativeEventResult(id), Generation = ownedGeneration, World = world, Started = Time.realtimeSinceStartup,
                State = OwnedTalkActive ? "preparing" : "opening", Talk = OwnedTalkActive ? currentTalk : null, NewHost = !OwnedTalkActive, Before = before };
            AddEvent("native_event_requested", ReadNativeEventRun());
            if (DialogueActive)
            {
                if (modelRequestId != null) AddEvent("dialogue_request_cancelled", Json.Map("conversationId", dialogueId, "requestId", modelRequestId, "reason", "native_event"));
                modelRequestId = null; modelReceived = false; receivedModelText = null; queuedAdvText = null; dialogueError = null; releaseAdvHold = true; SetDialoguePhase("native_event");
            }
            if (!OwnedTalkActive && id == 11)
            {
                ownedNativeFollow = true; ownedNpc.Pause(false); action.Player.ChaserSet(ownedNpc);
                nativeEventRun.Applied = true; nativeEventRun.Actual = CaptureNativeEventEffects(); AddEvent("native_event_started", ReadNativeEventRun()); FinishNativeEvent("completed", null);
            }
            else if (!OwnedTalkActive) ExecuteNativeCommand("native_talk", Json.Map());
            return ReadNativeEventRun();
        }
        bool ForcedNativeEventHost(TalkScene talk)
        { return nativeEventRun != null && nativeEventRun.Generation == ownedGeneration && nativeEventRun.World == world && ReferenceEquals(nativeEventRun.Talk, talk) &&
            (NativeEventBusy || nativeEventRun.Closing || nativeEventRun.State == "cancelled"); }
        void HideNativeEventHost(TalkScene talk)
        { foreach (string field in new string[] { "canvas", "canvasFemale", "canvasBack" }) { var canvas = AccessTools.Field(typeof(TalkScene), field).GetValue(talk) as Canvas; if (canvas != null) { if (!nativeEventRun.Canvases.ContainsKey(canvas)) nativeEventRun.Canvases[canvas] = canvas.enabled; canvas.enabled = false; } } }
        void RestoreNativeEventHost()
        { foreach (var entry in nativeEventRun.Canvases) if (entry.Key != null) entry.Key.enabled = entry.Value; nativeEventRun.Canvases.Clear(); nativeEventRun.Talk = null; }
        static void PrepareNativeEventHost(TalkScene __instance)
        {
            Plugin p = Current;
            if (p == null || p.nativeEventRun == null || !p.nativeEventRun.NewHost || !ReferenceEquals(p.nativeEventRun.Talk, null) ||
                p.nativeEventRun.Generation != p.ownedGeneration || p.nativeEventRun.World != p.world || p.nativeEventRun.Applied ||
                !p.nativeTalkPending) return;
            p.nativeEventRun.Talk = __instance; p.HideNativeEventHost(__instance);
        }
        static bool AllowNativeEventAdv(TalkScene __instance)
        {
            return Current == null || !Current.ForcedNativeEventHost(__instance);
        }
        static bool NativeEventIntroduction(ActionGame.Communication.Info __instance, ref bool __0, ref int __1, ref List<ADV.Program.Transfer> __result)
        {
            Plugin p = Current;
            if (p == null || !p.IsNativeEventInfo(__instance)) return true;
            __0 = false; __1 = -1; __result = ADV.Program.Transfer.NewList(false, false); return false;
        }
        bool IsNativeEventInfo(ActionGame.Communication.Info info)
        { return nativeEventRun != null && nativeEventRun.Talk != null && ForcedNativeEventHost(nativeEventRun.Talk) && ReferenceEquals(info, AccessTools.Property(typeof(TalkScene), "info").GetValue(nativeEventRun.Talk, null)); }
        static bool NativeEventSelection(TalkScene __instance)
        {
            Plugin p = Current;
            if (p == null || !p.ForcedNativeEventHost(__instance)) return true;
            p.HideNativeEventHost(__instance);
            if (p.nativeEventRun.State == "opening") p.InvokeNativeEvent(__instance);
            else if (p.nativeEventRun.State == "cancelled") p.CloseNativeEventHost(__instance);
            return false;
        }
        static bool NativeEventAcceptedResult(ActionGame.Communication.Info __instance, int __0, ref int __1, ref bool __2, ref List<ADV.Program.Transfer> __result)
        {
            Plugin p = Current;
            if (p == null || !p.nativeEventCommand || !p.IsNativeEventInfo(__instance) || p.nativeEventRun.Event != __0) return true;
            p.ApplyNativeEventState(__0);
            __1 = p.nativeEventRun.Result; __2 = (__0 == 4 && !p.ownedHeroine.talkEvent.Contains(79)) || (__0 == 8 && !p.ownedHeroine.talkEvent.Contains(69)) || (__0 == 9 && !p.ownedHeroine.talkEvent.Contains(74));
            __result = ADV.Program.Transfer.NewList(false, false); p.nativeEventRun.Applied = true; p.nativeEventRun.Actual = p.CaptureNativeEventEffects(); return false;
        }
        void InvokeNativeEvent(TalkScene talk)
        {
            if (nativeEventRun.Applied) return;
            string unavailable = NativeEventUnavailable(nativeEventRun.Event, talk);
            if (unavailable != null)
            {
                FailNativeEvent(talk, unavailable);
                return;
            }
            if (DialogueActive) ResetAdvDialogue("native_event");
            nativeEventRun.State = "running"; StopNativeMenuActivity(talk);
            if (nativeEventRun.Event == 11) ownedNativeFollow = true;
            nativeEventCommand = true;
            try { AccessTools.Method(typeof(TalkScene), "CommandFunc", new Type[] { typeof(int) }, null).Invoke(talk, new object[] { nativeEventRun.Event + 10 }); }
            catch { FailNativeEvent(talk, "native_execution_failed"); throw; }
            finally { nativeEventCommand = false; }
            if (!nativeEventRun.Applied) FailNativeEvent(talk, "native_effect_not_applied");
            else { SyncOwnedStats(); nativeEventRun.Actual = CaptureNativeEventEffects(); AddEvent("native_event_started", ReadNativeEventRun()); }
        }
        void FailNativeEvent(TalkScene talk, string reason)
        {
            FinishNativeEvent("failed", reason);
            if (!nativeEventRun.Applied)
            { if (nativeEventRun.NewHost) CloseNativeEventHost(talk); else RestoreNativeEventHost(); }
        }
        void CloseNativeEventHost(TalkScene talk)
        {
            if (nativeEventRun.Closing) return;
            nativeEventRun.Closing = true; nativeEventCommand = true;
            try { AccessTools.Method(typeof(TalkScene), "CommandFunc", new Type[] { typeof(int) }, null).Invoke(talk, new object[] { -1 }); }
            finally { nativeEventCommand = false; }
        }
        void FinishNativeEvent(string state, string error)
        {
            nativeEventRun.State = state; nativeEventRun.Error = error;
            SyncOwnedStats(); nativeEventRun.Actual = CaptureNativeEventEffects(); SaveNativeProfile(); SaveOwnedProfile(); AddEvent("native_event_" + state, ReadNativeEventRun());
        }
        void CancelPendingNativeEvent(string reason)
        { if (NativeEventBusy && !nativeEventRun.Applied) { FinishNativeEvent("cancelled", reason); if (!nativeEventRun.NewHost) RestoreNativeEventHost(); } }
        void EndOwnedNativeFollow()
        {
            ownedNativeFollow = false;
            if (action != null && action.Player != null && ReferenceEquals(action.Player.chaser, ownedNpc)) action.Player.ChaserEnd();
        }
        void TickNativeEvents()
        {
            if (ownedNativeFollow && (action == null || action.Player == null || !ReferenceEquals(action.Player.chaser, ownedNpc)))
            {
                ownedNativeFollow = false;
                if (ownedController != null && !Recovering && !SceneBusy && Mode == "roaming") SetNativeIdle();
            }
            if (nativeEventRun == null) return;
            if (nativeEventRun.Generation != ownedGeneration || nativeEventRun.World != world) { nativeEventRun = null; ownedNativeFollow = false; return; }
            if (nativeEventRun.State == "cancelled")
            { if (nativeEventRun.Talk != null && NativeTalkReady(nativeEventRun.Talk)) CloseNativeEventHost(nativeEventRun.Talk); return; }
            if (!NativeEventBusy) return;
            if (!nativeEventRun.Applied && (ownedController == null || Recovering)) { CancelPendingNativeEvent("controller_lost"); return; }
            if (!nativeEventRun.Applied && Time.realtimeSinceStartup - nativeEventRun.Started > 60) { CancelPendingNativeEvent("opening_timeout"); return; }
            if (nativeEventRun.NewHost && !nativeEventRun.Applied && nativeEventRun.Talk != null)
            { Tutorial tutorial = UnityEngine.Object.FindObjectOfType<Tutorial>(); if (tutorial != null) tutorial.EndScene(true); }
            if (nativeEventRun.State == "preparing" && OwnedTalkActive && !SceneBusy)
            {
                if (NativeTalkReady(currentTalk)) InvokeNativeEvent(currentTalk);
                else
                {
                    var scenario = ManagedAdvScenario();
                    if (scenario != null && !scenario.isChoice && scenario.textController != null && scenario.textController.NameMessageVisible)
                    { scenario.textController.ForceCompleteDisplayText(); AdvanceBridgeAdvLine(scenario); }
                }
            }
            if (nativeEventRun.Applied && !nativeTalkPending && nativeEventRun.Talk == null && !SceneBusy && !ADV.Program.isADVProcessing)
                FinishNativeEvent("completed", null);
        }
    }
}
