using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.AI;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        GameObject ownedRoot;
        ChaControl ownedCha;
        NavMeshAgent ownedAgent;
        bool ownedReady, ownedMoving;
        int ownedMap = -1;
        int rosterCountAtSpawn, npcCountAtSpawn;
        string ownedGeneration = "none", ownedName, ownedStatePath, ownedState = "waiting_for_world", ownedError = null;
        string ownedMoveId, ownedAnimation = "Idle";
        float moveDeadline;
        Session ownedController;
        Vector3 claimedPosition;
        float claimedYaw;
        Dictionary<string, object> ownedStats, ownedFace;

        static Dictionary<string, object> NeutralFace()
        { return Json.Map("eyes", 0, "eyebrow", 0, "mouth", 0, "eyesOpen", 1f, "mouthOpen", 0f, "blink", false); }
        void InitializeOwnedProfile()
        {
            ownedName = Config.Bind("OwnedNpc", "DisplayName", "Bridge NPC", "Name of the one plugin-owned female actor; existing profile names are preserved").Value;
            ownedStats = Json.Map("favor", 0, "intimacy", 0, "anger", 0, "isAnger", false);
            ownedFace = NeutralFace();
            string directory = Path.Combine(Paths.ConfigPath, "KK_StoryNPCBridge");
            Directory.CreateDirectory(directory); ownedStatePath = Path.Combine(directory, "owned-actor.json");
            if (File.Exists(ownedStatePath))
            {
                var profile = Json.Parse(File.ReadAllText(ownedStatePath, Encoding.UTF8)) as Dictionary<string, object>;
                if (profile == null || Text(profile, "id") != ActorId) throw new FormatException("Invalid owned NPC profile");
                var stats = Object(profile, "stats"); ValidateStats(stats);
                foreach (var p in stats) ownedStats[p.Key] = p.Value;
                object face;
                if (profile.TryGetValue("face", out face) && face is Dictionary<string, object>)
                    foreach (var p in (Dictionary<string, object>)face) if (ownedFace.ContainsKey(p.Key)) ownedFace[p.Key] = p.Value;
            }
            else SaveOwnedProfile();
        }
        void SaveOwnedProfile()
        {
            var profile = Json.Map("schema", 1, "id", ActorId, "name", ownedName, "template", "ill_Default_Female", "stats", ownedStats, "face", ownedFace);
            string temporary = ownedStatePath + ".tmp";
            File.WriteAllText(temporary, Json.Stringify(profile), new UTF8Encoding(false));
            if (File.Exists(ownedStatePath)) File.Replace(temporary, ownedStatePath, null);
            else File.Move(temporary, ownedStatePath);
        }
        void TickOwnedActor()
        {
            if (Mode == "talk") wasNativeTalk = true;
            else if (wasNativeTalk && Mode == "roaming" && !SceneBusy)
            { wasNativeTalk = false; SyncOwnedStats(); SaveOwnedProfile(); SaveNativeProfile(); }
            if (ownedController != null && (!ownedController.Alive || (DateTime.UtcNow.Ticks - System.Threading.Interlocked.Read(ref ownedController.LastSeen)) / TimeSpan.TicksPerSecond > 12))
                ReleaseOwned("controller_disconnected", false);
            if (action == null) { ownedState = "waiting_for_world"; return; }
            if (ownedHeroine == null) EnsureNativeHeroine(Manager.Game.Instance.saveData);
            BindNativeActor();
            if (!ownedReady || SceneBusy || Mode != "roaming") return;
            if (stopOnRoam) { stopOnRoam = false; if (ownedController != null) SetNativeIdle(); else resumeNativePending = true; }
            if (initialSummonPending && action.Player != null && action.Player.chaCtrl != null && action.Player.chaCtrl.loadEnd)
            { PlaceInitialOwnedActor(); }
            TickApproach();
            if (!ownedMoving) return;
            if (ownedAgent == null || !ownedAgent.enabled || !ownedAgent.isOnNavMesh) { StopOwnedMotion("failed"); return; }
            if (Time.realtimeSinceStartup > moveDeadline) { StopOwnedMotion("timeout"); return; }
            if (!ownedAgent.pathPending && ownedAgent.hasPath && ownedAgent.pathStatus != NavMeshPathStatus.PathComplete) { StopOwnedMotion("failed"); return; }
            if (!ownedAgent.pathPending && Vector3.Distance(ownedRoot.transform.position, ownedAgent.destination) <= Math.Max(0.12f, ownedAgent.stoppingDistance + 0.05f))
            { StopOwnedMotion("completed"); return; }
            SetLocomotionSpeed(ownedAgent.velocity.magnitude);
        }
        void DestroyOwnedActor(string reason)
        {
            FlushAutonomyJournal();
            disposingOwned = true;
            SyncOwnedStats(); SaveOwnedProfile(); SaveNativeProfile();
            if (DialogueActive) ResetAdvDialogue(reason);
            ReleaseOwned(reason, false);
            if (ownedNpc != null && action != null && action.npcList != null) action.npcList.Remove(ownedNpc);
            if (ownedSave != null && ownedHeroine != null) ownedSave.heroineList.Remove(ownedHeroine);
            if (ownedRoot != null) UnityEngine.Object.Destroy(ownedRoot);
            if (idlePoint != null) UnityEngine.Object.Destroy(idlePoint.gameObject);
            ownedRoot = null; ownedCha = null; ownedAgent = null; ownedNpc = null; ownedHeroine = null; ownedSave = null;
            ownedReady = false; ownedMoving = false; ownedMap = -1; activeTask = null; nativeQueue.Clear();
        }
        int NativeComponentCount()
        { return ownedRoot == null ? 0 : ownedRoot.GetComponentsInChildren<ActionGame.Chara.Base>(true).Length; }
        int NativeTreeCount()
        {
            if (ownedRoot == null) return 0;
            int count = 0;
            foreach (MonoBehaviour behavior in ownedRoot.GetComponentsInChildren<MonoBehaviour>(true))
                if (behavior != null && behavior.GetType().FullName.StartsWith("NodeCanvas.", StringComparison.Ordinal)) count++;
            return count;
        }
        bool InNativeRoster()
        {
            return ownedHeroine != null && Manager.Game.IsInstance() && Manager.Game.Instance.HeroineList.Contains(ownedHeroine);
        }
        int OwnedInstanceCount()
        {
            int count = 0;
            if (action != null && action.npcList != null)
                foreach (var npc in action.npcList) if (npc != null && ReferenceEquals(npc.heroine, ownedHeroine)) count++;
            return count;
        }
        object ReadOwnedActor()
        {
            bool loaded = ownedReady && ownedNpc != null && ownedNpc.isActive && ownedMap == map && ownedRoot != null && ownedRoot.activeInHierarchy && ownedCha != null && ownedCha.loadEnd;
            var actor = Json.Map("id", ActorId, "generation", ownedGeneration, "name", ownedName,
                "origin", "plugin", "controller", ownedController == null ? "game" : "plugin", "nativeAI", true, "nativeAutonomy", ownedController == null,
                "loaded", loaded, "map", ownedMap, "mapInfo", MapReference(ownedMap), "state", ownedState, "lastError", ownedError,
                "template", "ill_Default_Female", "stats", new Dictionary<string, object>(ownedStats),
                "statsStore", "native_runtime_plugin_sidecar", "control", ownedController == null ? null : Json.Map("owner", ownedController.Id, "moving", ownedMoving),
                "nativeNpcComponents", NativeComponentCount(), "nativeBehaviorTrees", NativeTreeCount(), "inNativeRoster", InNativeRoster(),
                "instances", OwnedInstanceCount(), "nativeRosterAtSpawn", rosterCountAtSpawn, "nativeNpcsAtSpawn", npcCountAtSpawn);
            actor.Add("native", ReadNativeStatus());
            actor.Add("approach", ReadApproach());
            actor.Add("dialogue", ReadDialogue());
            actor.Add("nativeInteraction", ReadNativeInteraction());
            actor.Add("autonomy", ReadAutonomyState());
            actor.Add("activityReport", CurrentActivityReport());
            if (loaded)
            {
                actor.Add("position", Vector(ownedRoot.transform.position)); actor.Add("yaw", ownedRoot.transform.eulerAngles.y);
                actor.Add("sex", ownedCha.sex); actor.Add("personality", ownedCha.fileParam.personality);
                actor.Add("expression", ReadFace(ownedCha));
                actor.Add("expressionDetails", ReadExpressionDetails());
                actor.Add("expressionLimits", Json.Map("eyes", ownedCha.eyesCtrl.GetMaxPtn(), "eyebrow", ownedCha.eyebrowCtrl.GetMaxPtn(), "mouth", ownedCha.mouthCtrl.GetMaxPtn()));
                var animation = ownedCha.animBody.GetCurrentAnimatorStateInfo(0);
                var states = new List<string>();
                foreach (string state in new string[] { "Idle", "Locomotion", "Locomotion 0", "Locomotion_Anger", "talk", "talk_uke", "Phone3", "Dinner", "Dinner2" })
                    if (ownedCha.animBody.HasState(0, Animator.StringToHash(state))) states.Add(state);
                actor.Add("animation", Json.Map("state", animationOverride ? ownedAnimation : ownedNpc.motion.state, "fullPathHash", animation.fullPathHash, "shortNameHash", animation.shortNameHash, "normalizedTime", animation.normalizedTime, "states", states));
                string animationState = animationOverride ? ownedAnimation : ownedNpc.motion.state;
                actor.Add("animationDetails", string.IsNullOrEmpty(animationState) ? null : AnimationReference(animationState));
                actor.Add("navigation", Json.Map("enabled", ownedAgent.enabled, "onMesh", ownedAgent.enabled && ownedAgent.isOnNavMesh));
            }
            return actor;
        }
        void PlayOwnedAnimation(string state)
        {
            if (!ownedCha.animBody.HasState(0, Animator.StringToHash(state))) throw new BridgeException("unknown_animation", "State is absent from this actor's controller");
            ownedAnimation = state; animationOverride = true; ownedCha.animBody.speed = 1; ownedCha.animBody.Play(state, 0, 0);
        }
        void SetLocomotionSpeed(float value)
        {
            if (ownedCha == null || ownedCha.animBody == null) return;
            foreach (AnimatorControllerParameter parameter in ownedCha.animBody.parameters)
                if (parameter.type == AnimatorControllerParameterType.Float && parameter.name == "Speed") ownedCha.animBody.SetFloat(parameter.nameHash, value);
        }
        void StopOwnedMotion(string reason)
        {
            bool moving = ownedMoving; ownedMoving = false;
            if (ownedNpc != null && !SceneBusy && Mode == "roaming") SetNativeIdle();
            if (ownedAgent != null && ownedAgent.enabled && ownedAgent.isOnNavMesh) { ownedAgent.isStopped = true; ownedAgent.ResetPath(); }
            if (moving) AddEvent("move_" + reason, Json.Map("moveId", ownedMoveId));
            if (Mode == "roaming" && ownedReady && ownedCha != null && ownedCha.animBody != null && ownedCha.animBody.HasState(0, Animator.StringToHash("Idle")))
            { SetLocomotionSpeed(0); PlayOwnedAnimation("Idle"); ownedState = "idle"; }
        }
        void ReleaseOwned(string reason, bool restorePosition)
        {
            if (ownedController == null && !ownedMoving && activeTask == null && nativeQueue.Count == 0 && !Approaching && !DialogueActive) return;
            FinishApproach("cancelled", reason);
            if (DialogueActive) EndAdvDialogue(reason);
            if (activeTask != null) AddEvent("native_action_cancelled", activeTask.Read());
            activeTask = null; nativeQueue.Clear(); ownedMoving = false; faceOverride = false; animationOverride = false;
            if (ownedCha != null) ownedCha.enableExpression = true;
            if (restorePosition && ownedReady && ownedAgent != null && ownedAgent.enabled && ownedAgent.isOnNavMesh)
            { ownedAgent.Warp(claimedPosition); ownedNpc.position = claimedPosition; ownedRoot.transform.rotation = Quaternion.Euler(0, claimedYaw, 0); }
            ownedController = null;
            stopOnRoam = false; resumeNativePending = true; BeginUncontrolledPeriod(reason); TryResumeNativeAutonomy();
            AddEvent("controller_released", Json.Map("reason", reason, "nativeAIResumed", !resumeNativePending));
        }
    }
}

