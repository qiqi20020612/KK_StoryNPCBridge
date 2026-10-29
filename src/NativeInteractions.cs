using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        sealed class NativeInteraction
        {
            internal string Id = Guid.NewGuid().ToString("N"), State = "running", Reason, Generation, ConversationId, PreviousPhase, Region, Kind;
            internal TalkScene Talk;
            internal int Mode;
            internal Vector3 Position;
            internal float Started, Ended;
            internal bool EndTracked, Applied;
            internal Dictionary<string, object> Reaction;
        }
        NativeInteraction nativeInteraction;
        bool NativeReactionActive { get { return nativeInteraction != null && nativeInteraction.State == "running"; } }

        static bool RouteNativeTouch(TalkScene __instance, string __0, Vector3 __1, ref NativeInteraction __state)
        {
            Plugin p = Current;
            if (p == null || !ReferenceEquals(__instance.targetHeroine, p.ownedHeroine)) return true;
            if (!p.NativeInputReady(__instance)) return false;
            if (p.NativeReactionActive) p.FinishNativeInteraction(p.nativeInteraction, "cancelled", "superseded");
            __state = new NativeInteraction { Talk = __instance, Generation = p.ownedGeneration, Mode = __instance.touchMode,
                Kind = __instance.touchMode == 0 ? "touch" : __instance.touchMode == 1 ? "look" : "unknown", Region = __0, Position = __1,
                Started = Time.realtimeSinceStartup, ConversationId = p.DialogueActive ? p.dialogueId : null, PreviousPhase = p.DialogueActive ? p.dialoguePhase : null };
            p.nativeInteraction = __state;
            if (p.DialogueActive)
            {
                // Close only our held ADV segment before the original native reaction runs.
                p.releaseAdvHold = true; p.queuedAdvText = null; p.SetDialoguePhase("native_reaction");
            }
            p.AddEvent("native_interaction_started", p.ReadNativeInteraction());
            return true;
        }
        static void NativeTouchReturned(NativeInteraction __state)
        {
            if (Current == null || __state == null || !ReferenceEquals(Current.nativeInteraction, __state)) return;
            __state.Applied = true;
            Current.AddEvent("native_interaction_applied", Current.ReadNativeInteraction());
        }
        static Exception NativeTouchFailed(Exception __exception, NativeInteraction __state)
        {
            if (__exception != null && Current != null && __state != null) Current.FinishNativeInteraction(__state, "failed", __exception.GetType().Name);
            return __exception;
        }
        static void NativeReactionLoaded(TalkScene __instance, string __0, string __1, string __2, string __3, bool __4)
        {
            Plugin p = Current;
            if (p == null || !p.NativeReactionActive || !ReferenceEquals(p.nativeInteraction.Talk, __instance)) return;
            p.nativeInteraction.Reaction = Json.Map("assetBundle", __0, "voiceFile", __1, "expression", __2, "pose", __3);
            p.AddEvent("native_interaction_reaction", p.ReadNativeInteraction());
        }
        static void TrackNativeReactionEnd(TalkScene __instance, ref IEnumerator __result)
        {
            Plugin p = Current;
            if (p == null || !p.NativeReactionActive || !ReferenceEquals(p.nativeInteraction.Talk, __instance)) return;
            p.nativeInteraction.EndTracked = true;
            __result = p.WaitNativeReaction(__result, p.nativeInteraction);
        }
        IEnumerator WaitNativeReaction(IEnumerator original, NativeInteraction interaction)
        {
            bool completed = false;
            try
            {
                while (original.MoveNext()) yield return original.Current;
                while (ReferenceEquals(nativeInteraction, interaction) && advLineActive && dialoguePhase == "native_reaction") yield return null;
                completed = true;
            }
            finally
            { FinishNativeInteraction(interaction, completed ? "completed" : "cancelled", completed ? null : "native_reaction_interrupted"); }
        }
        void FinishNativeInteraction(NativeInteraction interaction, string state, string reason)
        {
            if (!ReferenceEquals(nativeInteraction, interaction) || interaction.State != "running") return;
            interaction.State = state; interaction.Reason = reason; interaction.Ended = Time.realtimeSinceStartup;
            if (DialogueActive && interaction.ConversationId == dialogueId && dialoguePhase == "native_reaction")
            {
                if (state == "completed" && !advLineActive)
                { nativeCommandMenusShown = true; StopNativeIdleTimer(interaction.Talk); }
                SetDialoguePhase(interaction.PreviousPhase == "awaiting_input" ? "awaiting_input" : "ready");
            }
            AddEvent("native_interaction_" + state, ReadNativeInteraction());
        }
        void TickNativeInteractions()
        {
            if (!NativeReactionActive) return;
            if (nativeInteraction.Generation != ownedGeneration || nativeInteraction.Talk == null ||
                currentTalk != null && !ReferenceEquals(currentTalk, nativeInteraction.Talk))
            { FinishNativeInteraction(nativeInteraction, "cancelled", "native_conversation_closed"); return; }
            if (!nativeInteraction.EndTracked && nativeInteraction.Reaction == null && Time.realtimeSinceStartup - nativeInteraction.Started > .5f && NativeTalkReady(nativeInteraction.Talk))
            { FinishNativeInteraction(nativeInteraction, "ignored", "native_no_reaction"); return; }
            if (Time.realtimeSinceStartup - nativeInteraction.Started > 60) FinishNativeInteraction(nativeInteraction, "failed", "native_reaction_timeout");
        }
        object ReadNativeInteraction()
        {
            if (nativeInteraction == null) return Json.Map("state", "none", "canStartDialogue", false);
            NativeInteraction interaction = nativeInteraction;
            Transform voice = interaction.Talk == null ? null : AccessTools.Field(typeof(TalkScene), "transVoice").GetValue(interaction.Talk) as Transform;
            AudioSource audio = voice == null ? null : voice.GetComponentInChildren<AudioSource>();
            bool ready = interaction.State == "completed" && interaction.Generation == ownedGeneration && ReferenceEquals(currentTalk, interaction.Talk) &&
                NativeTalkReady(interaction.Talk) && !advLineActive && (!DialogueActive || dialoguePhase == "ready" || dialoguePhase == "awaiting_input");
            return Json.Map("interactionId", interaction.Id, "state", interaction.State, "kind", interaction.Kind, "nativeMode", interaction.Mode, "generation", interaction.Generation, "nativeApplied", interaction.Applied,
                "region", interaction.Region, "position", Vector(interaction.Position), "conversationId", interaction.ConversationId, "reason", interaction.Reason,
                "elapsed", (interaction.State == "running" ? Time.realtimeSinceStartup : interaction.Ended) - interaction.Started,
                "reaction", interaction.Reaction, "voicePlaying", audio != null && audio.isPlaying,
                "voiceClip", audio == null || audio.clip == null ? null : audio.clip.name, "canStartDialogue", ready);
        }
        void RequireAfterInteraction(Dictionary<string, object> command)
        {
            if (NativeReactionActive) throw new BridgeException("native_interaction_busy", "Wait for the original native reaction to finish");
            object id;
            if (!command.TryGetValue("afterInteractionId", out id)) return;
            if (!(id is string) || nativeInteraction == null || (string)id != nativeInteraction.Id || nativeInteraction.Generation != ownedGeneration || nativeInteraction.Talk == null || !ReferenceEquals(nativeInteraction.Talk, currentTalk))
                throw new BridgeException("stale_interaction", "Refresh native_interaction_status before continuing");
            if (nativeInteraction.State != "completed") throw new BridgeException("interaction_not_completed", "This native interaction did not complete");
        }
    }
}
