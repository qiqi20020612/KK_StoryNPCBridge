using System;
using System.Collections.Generic;
using HarmonyLib;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        const string AdvSequenceMarker = "KK_StoryNPCBridge.sequence";
        sealed class AdvSequenceRun
        {
            internal string Id = Guid.NewGuid().ToString("N"), Generation, World, Conversation, State = "queued", After, Reason, Error;
            internal string RequestedUtc = DateTime.UtcNow.ToString("o"), StartedUtc, EndedUtc;
            internal int Segment, Page, PageCount;
            internal bool OwnSession, SkipPending, FaceOverride, AnimationOverride, VisualsCaptured;
            internal TalkScene Talk;
            internal ADV.TextScenario Scenario;
            internal ChaControl FaceActor;
            internal Dictionary<string, object> FaceSnapshot;
            internal List<AdvSequenceSegment> Segments;
            internal object Actual;
            internal string Operation = "play_adv_sequence", MotionRestore;
            internal AdvMotionSnapshot MotionSnapshot;
            internal string EndTag { get { return "KKBridgeSequenceEnd_" + Id; } }
        }
        AdvSequenceRun advSequence;
        bool AdvSequenceBusy { get { return advSequence != null && (advSequence.State == "queued" || advSequence.State == "running" || advSequence.State == "finishing"); } }
        bool AdvSequenceQueued { get { return advSequence != null && advSequence.State == "queued"; } }
        void InstallAdvSequenceHooks()
        {
            foreach (Type type in new Type[] { typeof(ADV.Commands.Chara.Expression), typeof(ADV.Commands.Chara.FixMouth), typeof(ADV.Commands.Chara.Motion), typeof(ADV.Commands.Base.Text), typeof(ADV.Commands.Base.Close), typeof(ADV.Commands.Base.Wait) })
                PatchNative(type, "Do", Type.EmptyTypes, null, null, "AdvSequenceCommandFailed");
        }
        object ReadAdvSequence()
        {
            var run = advSequence; if (run == null) return null;
            var segments = new List<object>(); foreach (var segment in run.Segments) segments.Add(segment.Read());
            return Json.Map("sequenceId", run.Id, "state", AdvSequenceBusy && Recovering ? "paused_reconnect" : run.State,
                "target", ActorId, "generation", run.Generation, "world", run.World, "conversationId", run.Conversation,
                "segment", run.Segment, "segmentCount", run.Segments.Count, "page", run.Page, "pageCount", run.PageCount,
                "segments", segments, "reason", run.Reason, "error", run.Error, "requestedUtc", run.RequestedUtc, "startedUtc", run.StartedUtc, "endedUtc", run.EndedUtc,
                "actual", run.Actual, "ownsDialogueSession", run.OwnSession, "operation", run.Operation, "motionRestore", run.MotionRestore);
        }
        object PlayAdvSequence(Dictionary<string, object> command)
        {
            RequireAdvSequenceReady();
            return StartAdvSequence(ParseAdvSequence(command), "play_adv_sequence");
        }
        void RequireAdvSequenceReady()
        {
            if (AdvSequenceBusy) throw new BridgeException("adv_sequence_busy", "Wait for or cancel the current ADV sequence");
            if (!OwnedTalkActive || !NativeTalkReady(currentTalk) || !VisualActorReady)
                throw new BridgeException("not_ready", "Open the bridge NPC's native_talk and wait for its menu before playing a sequence");
            if (NativeReactionActive || ExternalWaitVisible || DialogueActive && (WaitingModel || queuedAdvText != null || dialoguePhase != "ready" && dialoguePhase != "awaiting_input"))
                throw new BridgeException("dialogue_busy", "Finish the current native reaction or dialogue request before playing a sequence");
        }
        object StartAdvSequence(List<AdvSequenceSegment> segments, string operation)
        {
            bool own = !DialogueActive;
            string after = own ? "ready" : dialoguePhase;
            if (own)
            {
                InitializeDialogueSession(currentTalk.necessaryInfo != null && currentTalk.necessaryInfo.isAttack ? "npc" : "player", defaultModelTimeoutSeconds);
                dialogueHostSeen = true;
                AddEvent("dialogue_opened", Json.Map("conversationId", dialogueId, "channel", "native_adv", "initiator", dialogueInitiator, "source", operation == "play_adv_sequence" ? "adv_sequence" : operation));
            }
            else dialogueTurn++;
            advSequence = new AdvSequenceRun { Generation = ownedGeneration, World = world, Conversation = dialogueId,
                Talk = currentTalk, Segments = segments, OwnSession = own, After = after, Operation = operation };
            StopNativeMenuActivity(currentTalk);
            QueueAdvLine("sequence", "", after); SetDialoguePhase("sequence_playing");
            AddEvent("adv_sequence_requested", ReadAdvSequence());
            return ReadAdvSequence();
        }
        object CancelAdvSequence(Dictionary<string, object> command)
        {
            if (advSequence == null || Text(command, "sequenceId") != advSequence.Id)
                throw new BridgeException("stale_sequence", "Use the current sequenceId from adv_sequence_status");
            if (AdvSequenceBusy) EndAdvDialogue("sequence_cancelled");
            return ReadAdvSequence();
        }
        bool IsAdvSequenceWait(ADV.Commands.Base.Wait command)
        { return advSequence != null && command.args.Length >= 5 && command.args[1] == AdvSequenceMarker && command.args[2] == advSequence.Id; }
        bool AdvSequenceWaitPaused(ADV.Commands.Base.Wait command)
        { return IsAdvSequenceWait(command) && (advSequence.SkipPending || AdvSequenceBusy && Recovering); }
        bool TrackAdvSequenceWait(ADV.Commands.Base.Wait command)
        {
            if (!IsAdvSequenceWait(command) || !AdvSequenceBusy) return false;
            var run = advSequence;
            run.Scenario = AccessTools.Property(typeof(ADV.CommandBase), "scenario").GetValue(command, null) as ADV.TextScenario;
            if (command.args[4] == "segment")
            {
                run.Segment = int.Parse(command.args[3]);
                CaptureAdvMotionNative(run);
                AddEvent("adv_sequence_segment_started", ReadAdvSequence());
            }
            else if (command.args[4] == "end")
            {
                managedWait = command; advSawHold = true; lastHoldProcess = UnityEngine.Time.realtimeSinceStartup;
                run.Actual = run.MotionSnapshot == null ? CaptureAdvSequenceVisuals() : ReadAdvMotionActual(run);
                if (run.OwnSession)
                { run.State = "finishing"; releaseAdvHold = true; SetDialoguePhase("sequence_finishing"); }
                else { FinishAdvSequence("completed", null, null); SetDialoguePhase(run.After); }
            }
            return true;
        }
        object CaptureAdvSequenceVisuals()
        {
            if (!VisualActorReady) return null;
            return Json.Map("expression", ReadFace(ownedCha), "animationHash", ownedCha.animBody.GetCurrentAnimatorStateInfo(0).shortNameHash);
        }
        void TrackAdvSequencePage(Dictionary<string, object> data)
        {
            var run = advSequence;
            if (!AdvSequenceBusy || run.Segment < 1 || run.Segment > run.Segments.Count) return;
            run.Page++; data["sequenceId"] = run.Id; data["segment"] = run.Segment;
            var segment = run.Segments[run.Segment - 1];
            if (segment.Recorded) return;
            segment.Recorded = true;
            AddDialogueMessage(segment.Speaker == "npc" ? "assistant" : segment.Speaker == "player" ? "user" : "narrator", segment.Text);
            AddEvent(segment.Speaker == "npc" ? "npc_dialogue" : "adv_sequence_text", Json.Map("sequenceId", run.Id, "segment", run.Segment,
                "conversationId", dialogueId, "turn", dialogueTurn, "speaker", segment.Speaker == "npc" ? ownedName : segment.Speaker == "player" ? ownedSave.player.Name : "",
                "role", segment.Speaker, "text", segment.Text, "channel", "native_adv"));
        }
        void FinishAdvSequence(string state, string reason, string error)
        {
            if (!AdvSequenceBusy) return;
            var run = advSequence;
            run.State = state; run.Reason = reason; run.Error = error; run.EndedUtc = DateTime.UtcNow.ToString("o");
            if (run.Actual == null) run.Actual = run.MotionSnapshot == null ? CaptureAdvSequenceVisuals() : ReadAdvMotionActual(run);
            RestoreAdvMotion(run);
            if (run.VisualsCaptured)
            {
                if (run.Generation == ownedGeneration && !NativeEventBusy)
                { RestoreExtraFace(run.FaceActor, run.FaceSnapshot); faceOverride = run.FaceOverride; animationOverride = run.AnimationOverride; }
                run.VisualsCaptured = false;
                run.FaceActor = null; run.FaceSnapshot = null;
            }
            AddEvent("adv_sequence_" + run.State, ReadAdvSequence());
        }
        void InterruptAdvSequence(string reason, bool jump)
        {
            if (!AdvSequenceBusy) return;
            advSequence.SkipPending = jump && advSequence.Scenario != null;
            FinishAdvSequence(reason == "native_window_closed" || reason == "opening_timeout" ? "failed" : "cancelled", reason, null);
        }
        void ClearAdvSequenceHost(string reason)
        {
            InterruptAdvSequence(reason, false);
            if (advSequence != null && advSequence.Conversation == dialogueId)
            { advSequence.Scenario = null; advSequence.Talk = null; advSequence.SkipPending = false; }
        }
        void TickAdvSequenceCleanup()
        {
            var run = advSequence; if (run == null) return;
            if (run.SkipPending)
            {
                if (run.Scenario == null || run.Talk != currentTalk || run.Generation != ownedGeneration || run.World != world || NativeEventBusy)
                    run.SkipPending = false;
                else if (!SceneBusy)
                {
                    run.SkipPending = false;
                    internalAdvAdvance = true;
                    try { run.Scenario.SearchTagJumpOrOpenFile(run.EndTag, run.Scenario.CurrentLine); }
                    catch (Exception e) { Logger.LogWarning("ADV sequence cleanup: " + e.Message); }
                    finally { internalAdvAdvance = false; }
                }
            }
            if (run.State == "finishing" && !advLineActive)
            { FinishAdvSequence("completed", null, null); ResetAdvDialogue("sequence_completed"); }
        }
        void FailAdvSequence(Exception exception)
        {
            if (!AdvSequenceBusy) return;
            advSequence.SkipPending = advSequence.Scenario != null;
            FinishAdvSequence("failed", "native_execution_failed", exception.GetBaseException().Message);
            EndAdvDialogue("sequence_failed");
        }
        static Exception AdvSequenceCommandFailed(ADV.CommandBase __instance, Exception __exception)
        {
            var p = Current;
            if (__exception != null && p != null && p.AdvSequenceBusy && p.advSequence.Scenario != null &&
                ReferenceEquals(AccessTools.Property(typeof(ADV.CommandBase), "scenario").GetValue(__instance, null), p.advSequence.Scenario))
                p.FailAdvSequence(__exception);
            return __exception;
        }
    }
}
