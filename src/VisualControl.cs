using System;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        bool OwnedTalkActive { get { return currentTalk != null && ReferenceEquals(currentTalk.targetHeroine, ownedHeroine); } }
        bool VisualActorReady { get { return ownedReady && ownedCha != null && ownedCha.loadEnd && ownedCha.gameObject.activeInHierarchy && ownedRoot != null && (ownedMap == map || OwnedTalkActive); } }
        void RequireVisualActor()
        {
            if (!VisualActorReady || Mode != "roaming" && !OwnedTalkActive)
                throw new BridgeException("not_loaded", "The NPC model must be loaded in roaming or her native conversation");
        }
        string NativeTalkAnimation()
        {
            if (!OwnedTalkActive) return null;
            var motion = HarmonyLib.AccessTools.Field(typeof(TalkScene), "motion").GetValue(currentTalk) as Illusion.Game.Elements.EasyLoader.Motion;
            return motion == null ? null : motion.state;
        }
        string ActualAnimationKey(int hash)
        {
            if (animationOverride && UnityEngine.Animator.StringToHash(ownedAnimation) == hash) return ownedAnimation;
            string state = OwnedTalkActive ? NativeTalkAnimation() : ownedNpc == null ? null : ownedNpc.motion.state;
            if (!string.IsNullOrEmpty(state) && UnityEngine.Animator.StringToHash(state) == hash) return state;
            foreach (string candidate in AnimationStates()) if (UnityEngine.Animator.StringToHash(candidate) == hash) return candidate;
            return null;
        }
    }
}
