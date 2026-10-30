using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using HarmonyLib;
using UnityEngine;
using ActionGame;
using ActionGame.Chara;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        SaveData.Heroine ownedHeroine;
        SaveData ownedSave;
        NPC ownedNpc;
        MethodInfo nativeCharacterFactory;
        WaitPoint idlePoint;
        bool faceOverride, animationOverride, initialSummonPending, wasNativeTalk, nativeTalkPending, nativeTextBusy, stopOnRoam;
        readonly Queue<NativeTask> nativeQueue = new Queue<NativeTask>();
        NativeTask activeTask;
        int decisionCount;
        float nextRegistration;
        Vector3 lastNativePosition;
        sealed class NativeTask
        {
            internal string Id = Guid.NewGuid().ToString("N");
            internal int Action, Map;
            internal float Duration;
            internal object Read() { return Json.Map("taskId", Id, "action", Action, "actionInfo", Current.ActionReference(Action), "map", Map, "mapInfo", Current.MapReference(Map), "duration", Duration,
                "request", Json.Map("op", "queue_action", "target", ActorId, "generation", Current.ownedGeneration, "action", Current.ActionReference(Action), "map", Current.MapReference(Map), "duration", Duration)); }
        }
        void InstallNativeHooks()
        {
            PatchNative(typeof(SaveData), "Load", new Type[] { typeof(string), typeof(string) }, null, "SaveLoaded", null);
            PatchNative(typeof(ActionControl), "Init", Type.EmptyTypes, "BeforeActionInit", null, null);
            PatchNative(typeof(ActionControl), "FirstAction", new Type[] { typeof(SaveData.Heroine) }, null, "FirstNativeAction", null);
            PatchNative(typeof(ActionControl), "NextAction", new Type[] { typeof(SaveData.Heroine), typeof(int[]) }, "NextNativeAction", "NativeActionSelected", null);
            PatchNative(typeof(AI), "ArrivalSet", new Type[] { typeof(ActionControl.ResultInfo) }, null, "NativeArrived", null);
            PatchNative(typeof(ActionScene), "_SceneEvent", new Type[] { typeof(ActionGame.Chara.Base) }, null, "TrackNativeTalk", null);
            PatchNative(typeof(SaveData), "<Save>m__3", new Type[] { typeof(FileStream) }, "BeforeNativeSave", null, "AfterNativeSave");
            PatchNative(typeof(ActionControl), "Save", new Type[] { typeof(BinaryWriter) }, "BeforeDesireSave", null, "AfterDesireSave");
        }
        void PatchNative(Type type, string name, Type[] args, string prefix, string postfix, string finalizer)
        {
            harmony.Patch(AccessTools.Method(type, name, args, null), Hook(prefix), Hook(postfix), null, Hook(finalizer), null);
        }
        static HarmonyMethod Hook(string name)
        { return name == null ? null : new HarmonyMethod(typeof(Plugin).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)); }
        static void SaveLoaded(SaveData __instance, bool __result)
        { if (__result && Current != null && Manager.Game.IsInstance() && ReferenceEquals(__instance, Manager.Game.Instance.saveData)) Current.EnsureNativeHeroine(__instance); }
        static void BeforeActionInit()
        { if (Current != null && Manager.Game.IsInstance()) Current.EnsureNativeHeroine(Manager.Game.Instance.saveData); }
        static void BeforeNativeSave(SaveData __instance, FileStream __0, ref List<SaveData.Heroine> __state)
        {
            if (Current == null || __instance != Current.ownedSave || Current.ownedHeroine == null) return;
            Current.SyncOwnedStats(); Current.SaveNativeProfile(); Current.SaveOwnedProfile();
            __state = __instance.heroineList;
            __instance.heroineList = new List<SaveData.Heroine>(__state);
            __instance.heroineList.Remove(Current.ownedHeroine);
        }
        static void AfterNativeSave(SaveData __instance, FileStream __0, List<SaveData.Heroine> __state, Exception __exception)
        {
            if (__state == null) return;
            __instance.heroineList = __state;
            if (Current == null || __exception != null) return;
            try { Current.SaveProfileForNativeSlot(__0); }
            catch (Exception e) { Current.Logger.LogWarning("Story profile save: " + e.Message); Current.AddEvent("profile_save_failed", Json.Map("save", Path.GetFileName(__0.Name), "error", e.Message)); }
        }
        static void BeforeDesireSave(ActionControl __instance, ref IDictionary __state)
        {
            if (Current == null || Current.ownedHeroine == null) return;
            var field = AccessTools.Field(typeof(ActionControl), "dicTarget");
            var original = (IDictionary)field.GetValue(__instance);
            if (!original.Contains(Current.ownedHeroine)) return;
            var filtered = (IDictionary)Activator.CreateInstance(original.GetType());
            foreach (DictionaryEntry entry in original) if (!ReferenceEquals(entry.Key, Current.ownedHeroine)) filtered.Add(entry.Key, entry.Value);
            __state = original; field.SetValue(__instance, filtered);
        }
        static void AfterDesireSave(ActionControl __instance, IDictionary __state)
        { if (__state != null) AccessTools.Field(typeof(ActionControl), "dicTarget").SetValue(__instance, __state); }
        string NativeProfilePath { get { return Path.Combine(Path.GetDirectoryName(ownedStatePath), "owned-native.bin"); } }
        void SaveNativeProfile()
        {
            if (ownedHeroine == null || ownedSave == null) return;
            string temp = NativeProfilePath + ".tmp";
            using (var stream = File.Create(temp)) using (var writer = new BinaryWriter(stream))
            { writer.Write(SaveData.Version.ToString()); ownedHeroine.Save(writer); }
            if (File.Exists(NativeProfilePath)) File.Replace(temp, NativeProfilePath, null); else File.Move(temp, NativeProfilePath);
        }
        void InitializeCharacterFactory()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = typeof(Manager.Character);
            nativeCharacterFactory = type.GetMethod("CreateChara", flags, null,
                new Type[] { typeof(byte), typeof(int), typeof(GameObject), typeof(int), typeof(ChaFileControl), typeof(bool) }, null)
                ?? type.GetMethod("CreateChara", flags, null,
                new Type[] { typeof(byte), typeof(GameObject), typeof(int), typeof(ChaFileControl), typeof(bool) }, null);
            if (nativeCharacterFactory == null) throw new MissingMethodException("No supported Character.CreateChara overload was found");
            Logger.LogInfo("Native character factory: " + nativeCharacterFactory);
        }
        ChaControl CreateDefaultFemaleTemplate()
        {
            object[] arguments = nativeCharacterFactory.GetParameters().Length == 6
                ? new object[] { (byte)1, 0, null, 990001, null, true }
                : new object[] { (byte)1, null, 990001, null, true };
            return (ChaControl)nativeCharacterFactory.Invoke(Manager.Character.Instance, arguments);
        }
        void EnsureNativeHeroine(SaveData save)
        {
            if (Time.realtimeSinceStartup < nextRegistration) return;
            try { RegisterNativeHeroine(save); }
            catch (Exception e)
            {
                ownedError = e.Message; ownedState = "registration_failed"; nextRegistration = Time.realtimeSinceStartup + 10;
                Logger.LogError("Bridge NPC registration: " + e);
            }
        }
        void RegisterNativeHeroine(SaveData save)
        {
            if (save == null || save.player == null) return;
            if (ownedSave == save && ownedHeroine != null && save.heroineList.Contains(ownedHeroine)) return;
            if (!Manager.Character.IsInstance()) return;
            if (ownedHeroine != null) { SyncOwnedStats(); SaveNativeProfile(); SaveOwnedProfile(); FlushAutonomyJournal(); }
            ClearControlRecovery();
            if (DialogueActive) ResetAdvDialogue("world_changed");
            SelectStoryProfile();
            ownedController = null; ownedNpc = null; ownedRoot = null; ownedCha = null; ownedAgent = null;
            ownedReady = false; ownedMoving = false; nativeTalkPending = false; faceOverride = false; animationOverride = false; disposingOwned = false;
            if (idlePoint != null) UnityEngine.Object.Destroy(idlePoint.gameObject); idlePoint = null;
            int seat = -1;
            for (int index = 24; index >= 0; index--)
            {
                if (index == save.player.schoolClassIndex || index == save.player.schoolClassIndex + 1) continue;
                bool used = false;
                foreach (var h in save.heroineList) if (h.schoolClass == save.player.schoolClass && h.schoolClassIndex == index) used = true;
                if (!used) { seat = index; break; }
            }
            if (seat < 0) throw new InvalidOperationException("No free class slot for the bridge NPC");
            ChaControl template = CreateDefaultFemaleTemplate();
            ChaFileControl card = new ChaFileControl();
            try { card.CopyAll(template.chaFile); }
            finally { Manager.Character.Instance.DeleteChara(template, true); }
            card.parameter.lastname = ""; card.parameter.firstname = ownedName; card.parameter.nickname = ownedName;
            var heroine = new SaveData.Heroine(card, false);
            if (File.Exists(NativeProfilePath))
                using (var stream = File.OpenRead(NativeProfilePath)) using (var reader = new BinaryReader(stream))
                { var version = new Version(reader.ReadString()); heroine = new SaveData.Heroine(version, reader); }
            heroine.charFileInitialized = true;
            heroine.charFile.parameter.lastname = ""; heroine.charFile.parameter.firstname = ownedName; heroine.charFile.parameter.nickname = ownedName;
            heroine.schoolClass = save.player.schoolClass; heroine.schoolClassIndex = seat;
            ownedHeroine = heroine; ownedSave = save;
            if (!File.Exists(NativeProfilePath)) ApplyOwnedStats();
            SyncOwnedStats(); SaveOwnedProfile(); SaveNativeProfile();
            save.heroineList.Add(heroine);
            ownedGeneration = Guid.NewGuid().ToString("N");
            AddEvent("actor_initialized", Json.Map("profile", ReadStoryProfile(), "stats", new Dictionary<string, object>(ownedStats)));
            nativeQueue.Clear(); activeTask = null; initialSummonPending = false; approachState = "none";
            resumeNativePending = false; BeginUncontrolledPeriod("world_loaded");
            ownedError = null;
            Logger.LogInfo("Registered native bridge heroine at class " + heroine.schoolClass + " seat " + seat);
        }
        bool IsOwned(SaveData.Heroine h) { return h != null && ReferenceEquals(h, ownedHeroine); }
        static void FirstNativeAction(ActionControl __instance, SaveData.Heroine __0, ref ActionControl.ResultInfo __result)
        {
            if (Current == null || !Current.IsOwned(__0)) return;
            if (Current.ownedController != null) __result = Current.NativeIdle(__instance, __result);
            else Current.RecordNativeSelection(__result);
        }
        static bool NextNativeAction(ActionControl __instance, SaveData.Heroine __0, ref ActionControl.ResultInfo __result)
        {
            if (Current == null || !Current.IsOwned(__0) || Current.ownedController == null) return true;
            __result = Current.DecideNativeAction(__instance); return false;
        }
        static void NativeArrived(AI __instance, ActionControl.ResultInfo __0)
        {
            if (Current == null || Current.ownedNpc == null || !ReferenceEquals(Current.ownedNpc.AI, __instance) || Current.activeTask == null || __0.actionNo != Current.activeTask.Action) return;
            if (Current.activeTask.Duration > 0) __0.actionTime = Current.activeTask.Duration;
            Current.AddEvent("native_action_arrived", Current.activeTask.Read());
        }
        static void NativeActionSelected(SaveData.Heroine __0, ActionControl.ResultInfo __result)
        { if (Current != null && Current.IsOwned(__0) && Current.ownedController == null) Current.RecordNativeSelection(__result); }
        ActionControl.ResultInfo NativeIdle(ActionControl control, ActionControl.ResultInfo previous)
        {
            var npc = ownedHeroine.charaBase as NPC;
            ActionScene scene = Manager.Game.Instance.actScene;
            if (idlePoint == null)
            {
                var go = new GameObject("KK_StoryNPCBridge.IdlePoint");
                if (scene != null) go.transform.SetParent(scene.transform, false);
                idlePoint = go.AddComponent<WaitPoint>();
                idlePoint.parameterList = new List<WaitPoint.Parameter>();
                var parameter = new WaitPoint.Parameter(); parameter.layer = "Wait";
                parameter.motionList = new List<WaitPoint.Parameter.Motion>();
                parameter.motionList.Add(new WaitPoint.Parameter.Motion { motion = "Idle", state = 0 });
                idlePoint.parameterList.Add(parameter);
            }
            int mapNo = npc != null && npc.mapNo >= 0 ? npc.mapNo : previous != null && previous.point != null ? previous.point.MapNo : scene.Map.no;
            Vector3 pos = npc != null ? npc.calcPosition : Vector3.zero;
            if (previous != null && previous.point != null)
            {
                if (ownedReady && ownedMap >= 0) { mapNo = ownedMap; pos = lastNativePosition; }
                else { mapNo = previous.point.MapNo; pos = previous.point.position; }
            }
            idlePoint.MapNo = mapNo; idlePoint.transform.position = pos;
            idlePoint.transform.rotation = npc != null ? npc.rotation : Quaternion.identity;
            AccessTools.Field(typeof(WaitPoint), "_position").SetValue(idlePoint, null);
            AccessTools.Field(typeof(WaitPoint), "_angle").SetValue(idlePoint, null);
            var targets = (IDictionary)AccessTools.Field(typeof(ActionControl), "dicTarget").GetValue(control);
            object desire = targets[ownedHeroine];
            if (desire != null) { AccessTools.Method(desire.GetType(), "SetWaitPoint", new Type[] { typeof(WaitPoint), typeof(NPC) }, null).Invoke(desire, new object[] { idlePoint, npc }); AccessTools.Property(desire.GetType(), "action").SetValue(desire, -1, null); }
            ownedState = "idle";
            return new ActionControl.ResultInfo { point = idlePoint, pointIndex = 0, actionNo = -1, actionName = "Bridge idle", layerName = "Wait", actionTime = 100000000f, totalTime = 100000000f, notMap = new HashSet<int>(), notAction = new int[0], plansNo = -1, plansName = "" };
        }
        ActionControl.ResultInfo DecideNativeAction(ActionControl control)
        {
            decisionCount++;
            if (Approaching) FinishApproach("failed", "native_action_interrupted");
            if (activeTask != null) { AddEvent("native_action_completed", activeTask.Read()); activeTask = null; }
            while (nativeQueue.Count > 0)
            {
                var task = nativeQueue.Dequeue();
                var result = ResolveNativeTask(control, task);
                if (result != null) return result;
            }
            return NativeIdle(control, null);
        }
        ActionControl.ResultInfo ResolveNativeTask(ActionControl control, NativeTask task)
        {
            var result = control.SetAction(ownedHeroine, task.Action, task.Map);
            if (result == null || result.actionNo != task.Action || task.Map >= 0 && result.point != null && result.point.MapNo != task.Map)
            { AddEvent("native_action_failed", Json.Map("task", task.Read(), "reason", "native_conditions_or_point_unavailable", "fallbackAction", result == null ? null : (object)result.actionNo, "fallbackMap", result == null || result.point == null ? null : (object)result.point.MapNo)); return null; }
            if (task.Duration > 0) { result.actionTime = task.Duration; result.totalTime = task.Duration + 600; }
            if (activeTask != null) AddEvent("native_action_cancelled", activeTask.Read());
            activeTask = task; ownedState = "native_action";
            AddEvent("native_action_started", task.Read()); return result;
        }
        void BindNativeActor()
        {
            if (ownedHeroine == null) return;
            NPC next = ownedHeroine.charaBase as NPC;
            if (next == null)
            {
                if (OwnedTalkActive && ownedHeroine.chaCtrl != null)
                { ownedCha = ownedHeroine.chaCtrl; ownedReady = ownedCha.loadEnd; SyncOwnedStats(); }
                return;
            }
            bool changed = ownedNpc != next;
            ownedNpc = next; ownedRoot = next.gameObject;
            ownedCha = OwnedTalkActive && ownedHeroine.chaCtrl != null ? ownedHeroine.chaCtrl : next.chaCtrl;
            ownedAgent = next.agent; ownedMap = next.mapNo;
            lastNativePosition = next.calcPosition;
            ownedReady = next.initialized && ownedCha != null && ownedCha.loadEnd;
            if (changed) { rosterCountAtSpawn = Manager.Game.Instance.HeroineList.Count; npcCountAtSpawn = action.npcList.Count; AddEvent("actor_spawned", Json.Map("native", true)); }
            SyncOwnedStats();
        }
        object ReadNativeActions()
        {
            var result = new List<object>();
            if (action == null || action.actCtrl == null || ownedHeroine == null) return result;
            var infos = (IDictionary)AccessTools.Field(typeof(ActionControl), "dicBaseInfo").GetValue(action.actCtrl);
            foreach (DictionaryEntry pair in infos)
            {
                string layer = (string)AccessTools.Field(pair.Value.GetType(), "layer").GetValue(pair.Value);
                var maps = new List<object>();
                if (action.PosSet != null && action.PosSet.waitPointDic != null)
                    foreach (var points in action.PosSet.waitPointDic)
                    {
                        bool found = false;
                        foreach (var point in points.Value) if (point != null) foreach (var parameter in point.parameterList) if (parameter.layer == layer) found = true;
                        if (found) maps.Add(points.Key);
                    }
                var entry = ActionReference((int)pair.Key); entry["desire"] = action.actCtrl.GetDesire((int)pair.Key, ownedHeroine); entry["mapsWithPoints"] = maps;
                var mapDetails = new List<object>(); foreach (int mapId in maps) mapDetails.Add(MapReference(mapId)); entry["mapDetails"] = mapDetails;
                entry["request"] = Json.Map("op", "set_action", "target", ActorId, "generation", ownedGeneration, "action", Json.Map("id", pair.Key, "key", entry["key"]));
                entry["queueRequest"] = Json.Map("op", "queue_action", "target", ActorId, "generation", ownedGeneration, "action", Json.Map("id", pair.Key, "key", entry["key"])); result.Add(entry);
            }
            return result;
        }
        object ReadNativeStatus()
        {
            var queue = new List<object>(); foreach (var task in nativeQueue) queue.Add(task.Read());
            var r = ownedNpc != null && ownedNpc.AI != null ? ownedNpc.AI.result : null;
            return Json.Map("selection", ownedController == null ? "game" : "plugin", "executor", "native", "action", r == null ? -1 : r.actionNo, "name", r == null ? null : ActionName(r.actionNo), "nativeName", r == null ? null : r.actionName, "actionInfo", r == null ? null : ActionReference(r.actionNo),
                "plansNo", r == null ? -1 : r.plansNo, "plansName", r == null ? null : r.plansName,
                "arrival", ownedNpc != null && ownedNpc.AI != null && ownedNpc.AI.isArrival, "timer", ownedNpc != null && ownedNpc.AI != null ? ownedNpc.AI.actionTimer : 0,
                "motion", ownedNpc == null ? null : ownedNpc.motion.state, "props", ownedNpc == null ? 0 : ownedNpc.itemObjList.Count,
                "destination", r == null || r.point == null ? null : Json.Map("map", r.point.MapNo, "mapInfo", MapReference(r.point.MapNo), "position", Vector(r.point.position)),
                "canTalk", ownedReady && ownedNpc != null && ownedNpc.motion.state != null && ownedNpc.isAction, "talking", nativeTalkPending || currentTalk != null && ReferenceEquals(currentTalk.targetHeroine, ownedHeroine) || ownedNpc != null && ownedNpc.isTalking,
                "activeTask", activeTask == null ? null : activeTask.Read(), "queue", queue, "decisions", decisionCount);
        }
        NativeTask ParseTask(Dictionary<string, object> d)
        {
            object value; var source = d;
            if (d.TryGetValue("task", out value)) source = Object(d, "task");
            if (!source.TryGetValue("action", out value)) throw new BridgeException("invalid_argument", "action or a returned task object required");
            var task = new NativeTask(); task.Action = ActionId(value); task.Map = -1;
            if (source.TryGetValue("map", out value)) { task.Map = MapId(value); if (task.Map >= 0 && !action.Map.calcGateDic.ContainsKey(task.Map)) throw new BridgeException("unknown_map", "Map is not available to the native scheduler"); }
            if (source.TryGetValue("duration", out value)) task.Duration = (float)Number(value, 0, 3600, "duration");
            return task;
        }
        void SetNativeIdle()
        {
            if (npcInvitationPending && DialogueActive && dialoguePhase != "ending") EndAdvDialogue("new_command");
            FinishApproach("cancelled", "new_command");
            nativeQueue.Clear();
            if (activeTask != null) AddEvent("native_action_cancelled", activeTask.Read());
            activeTask = null; animationOverride = false;
            if (ownedNpc != null && ownedNpc.AI != null) { ownedNpc.Pause(false); ownedNpc.AI.NextAction(new int[0]); }
        }
        object ExecuteNativeCommand(string op, Dictionary<string, object> d)
        {
            if (op == "summon" || op == "walk_to_player") return WalkToPlayer();
            if (op == "native_talk")
            {
                RequireActor();
                if (!ownedNpc.isAction) throw new BridgeException("not_interactable", "The current native action prevents conversation");
                SetNativeIdle(); faceOverride = false;
                nativeTalkPending = true;
                if (panelVisible) TogglePanel();
                var routine = (IEnumerator)AccessTools.Method(typeof(ActionScene), "_SceneEventNPC", new Type[] { typeof(NPC) }, null).Invoke(action, new object[] { ownedNpc });
                action.StartCoroutine(RunNativeTalk(routine)); return Json.Map("status", "starting", "channel", "native");
            }
            if (op == "clear_queue") { nativeQueue.Clear(); return ReadNativeStatus(); }
            if (op == "set_desire")
            {
                var task = ParseTask(d); object value;
                if (!d.TryGetValue("value", out value)) throw new BridgeException("invalid_argument", "value required");
                double weight = Number(value, 0, 100, "value");
                if (weight != Math.Floor(weight)) throw new BridgeException("invalid_argument", "value must be an integer");
                if (!action.actCtrl.SetDesire(task.Action, ownedHeroine, (int)weight)) throw new BridgeException("unsupported_desire", "This action has no mutable desire");
                return ReadNativeActions();
            }
            var next = ParseTask(d);
            if (op == "queue_action")
            {
                if (nativeQueue.Count >= 32) throw new BridgeException("queue_full", "At most 32 tasks");
                nativeQueue.Enqueue(next); AddEvent("native_action_queued", next.Read());
                if (activeTask == null && !Approaching && Mode == "roaming") { animationOverride = false; ownedNpc.Pause(false); ownedNpc.AI.NextAction(new int[0]); }
                return ReadNativeStatus();
            }
            FinishApproach("cancelled", "new_task");
            var resolved = ResolveNativeTask(action.actCtrl, next);
            if (resolved == null) { SetNativeIdle(); throw new BridgeException("action_unavailable", "The native game cannot schedule this action at the requested map/time"); }
            nativeQueue.Clear(); animationOverride = false;
            ownedNpc.Pause(false);
            // Use the reactive result setter used by AI.SetAction, preserving native execution.
            AssignNativeResult(resolved);
            return ReadNativeStatus();
        }
        IEnumerator RunNativeTalk(IEnumerator routine)
        {
            if (ownedController == null) AppendNativeActivity("conversation_started", CaptureAutonomyState());
            try { while (routine.MoveNext()) yield return routine.Current; }
            finally { if (ownedController == null) AppendNativeActivity("conversation_ended", CaptureAutonomyState()); nativeTalkPending = false; }
        }
        static void TrackNativeTalk(ActionGame.Chara.Base __0, ref IEnumerator __result)
        {
            Plugin p = Current;
            if (p == null || !ReferenceEquals(__0, p.ownedNpc)) return;
            p.nativeTalkPending = true; __result = p.RunNativeTalk(__result);
        }
        void StartNpcInvitation()
        {
            if (action == null || action.Player == null || Mode != "roaming" || action.Player.isActionNow_Origin || ownedNpc.isTalking)
                throw new BridgeException("not_interactable", "The player or NPC is busy with another native interaction");
            SetNativeIdle(); faceOverride = false;
            var task = new NativeTask { Action = 8, Map = -1 };
            var result = ResolveNativeTask(action.actCtrl, task);
            if (result == null) throw new BridgeException("invitation_unavailable", "Native NPC invitation conditions are not satisfied");
            ownedNpc.Pause(false); AssignNativeResult(result);
            if (panelVisible) TogglePanel();
        }
        object SayNative(string text)
        {
            TalkScene talk = UnityEngine.Object.FindObjectOfType<TalkScene>();
            if (talk == null || !ReferenceEquals(talk.targetHeroine, ownedHeroine)) throw new BridgeException("wrong_context", "Open the native conversation with the bridge NPC first");
            if (!NativeTalkReady(talk)) throw new BridgeException("busy", "Wait for the native conversation options");
            ValidateDialogueText(text);
            if (text.Length > 1000) throw new BridgeException("invalid_argument", "Text exceeds 1000 characters");
            var list = ADV.Program.Transfer.NewList(false, false);
            list.Add(ADV.Program.Transfer.Create(true, ADV.Command.CharaChange, "-2", "true"));
            foreach (string page in BeginAdvPages(text)) list.Add(ADV.Program.Transfer.Text("[H名]", page));
            list.Add(ADV.Program.Transfer.Close());
            nativeTextBusy = true;
            try
            {
                AccessTools.Field(typeof(TalkScene), "isUpdateCamera").SetValue(talk, false);
                AccessTools.Method(typeof(TalkScene), "StartADV", new Type[] { list.GetType() }, null).Invoke(talk, new object[] { list });
                StartCoroutine(WaitNativeText());
            }
            catch { nativeTextBusy = false; throw; }
            AddEvent("npc_dialogue", Json.Map("speaker", ownedName, "text", text, "channel", "native"));
            return Json.Map("status", "running", "channel", "native_dialogue");
        }
        IEnumerator WaitNativeText()
        {
            try { yield return null; yield return ADV.Program.Wait("Talk"); AddEvent("native_dialogue_completed", null); }
            finally { nativeTextBusy = false; }
        }
    }
}


