using System;
using System.Collections.Generic;
using HarmonyLib;
using KKAPI.MainGame;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        HSceneReportController hSceneReports;

        static void HSceneAnimationChanged(HFlag ___flags, string __0, bool __result)
        {
            Plugin p = Current;
            if (p == null || p.hSceneReports == null || !__result) return;
            try { p.hSceneReports.AnimationChanged(___flags, __0); }
            catch (Exception e) { p.Logger.LogWarning("H observation: " + e.Message); }
        }

        // KKAPI owns the lifetime of this controller. It owns all H-report state, not game state.
        public sealed class HSceneReportController : GameCustomFunctionController
        {
            sealed class Participant
            {
                internal SaveData.CharaData Native;
                internal Dictionary<string, object> Actor, Before, AtEnd, Final, ModelAtEnd;
                internal string Slot, FirstObservedUtc;
            }
            sealed class Run
            {
                internal readonly string Id = Guid.NewGuid().ToString("N");
                internal MonoBehaviour Proc;
                internal HFlag Flags;
                internal SaveData Save;
                internal SaveData.Heroine BridgeHeroine;
                internal string World, Generation, StartedUtc, EndedUtc, LastState, InitialState;
                internal float Started, Ended;
                internal bool FreeH, Vr, NativeEnd, Bridge, InitialClimax, CapturingStart;
                internal SaveData.Player ContextPlayer;
                internal Dictionary<string, object> PlayerBefore;
                internal bool? PlayerParticipated;
                internal int StartMode, EndMode, Transitions, SoloClimaxes, LesbianClimaxes;
                internal object StartTime, EndTime, EndState;
                internal Dictionary<string, object> CountBefore, CountAfter;
                internal readonly List<int> Modes = new List<int>();
                internal readonly List<Participant> People = new List<Participant>();
            }
            Run active, pending;
            MonoBehaviour lastStartedProc;
            string lastGeneration;
            bool stopped;
            internal object Last;

            void Awake() { SceneManager.sceneUnloaded += SceneUnloaded; }
            void OnDestroy() { StopObserving(); }
            static Plugin Owner { get { return Current; } }

            protected override void OnStartH(MonoBehaviour proc, HFlag hFlag, bool vr)
            {
                Plugin p = Owner;
                if (p == null || stopped || ReferenceEquals(proc, lastStartedProc)) return;
                try
                {
                    if (active != null) EndWithoutCallback("next_h_scene_started");
                    if (pending != null) Complete("unavailable", "next_h_scene_started");
                    lastStartedProc = proc;
                    var save = Manager.Game.IsInstance() ? Manager.Game.Instance.saveData : null;
                    active = new Run { Proc = proc, Flags = hFlag, Save = save, World = p.world, Generation = p.ownedGeneration,
                        BridgeHeroine = ReferenceEquals(save, p.ownedSave) ? p.ownedHeroine : null,
                        StartedUtc = DateTime.UtcNow.ToString("o"), Started = Time.realtimeSinceStartup,
                        FreeH = hFlag != null && hFlag.isFreeH, Vr = vr, StartMode = hFlag == null ? -1 : (int)hFlag.mode,
                        StartTime = p.ReadGameTime(), CountBefore = Counts(hFlag), InitialState = hFlag == null ? null : hFlag.nowAnimStateName };
                    active.LastState = active.InitialState; active.InitialClimax = active.InitialState == "Orgasm";
                    active.ContextPlayer = hFlag == null ? null : hFlag.player; active.PlayerBefore = Attributes(active.ContextPlayer);
                    active.CapturingStart = true; ObserveParticipants(); active.CapturingStart = false;
                    p.AddEvent("game_h_started", Json.Map("sessionId", active.Id, "world", active.World, "startedUtc", active.StartedUtc,
                        "gameTime", active.StartTime, "mode", ModeRef(active.StartMode), "isFreeH", active.FreeH,
                        "bridgeParticipated", active.Bridge, "participants", ParticipantActors(active)));
                }
                catch (Exception e) { p.Logger.LogWarning("H start report: " + e.Message); }
            }

            protected override void OnEndH(MonoBehaviour proc, HFlag hFlag, bool vr)
            {
                Plugin p = Owner;
                if (p == null || stopped || active == null || !ReferenceEquals(active.Proc, proc)) return;
                try
                {
                    if (!ReferenceEquals(active.Flags, hFlag)) { EndWithoutCallback("native_flags_changed"); return; }
                    ObserveParticipants();
                    active.NativeEnd = true; active.Ended = Time.realtimeSinceStartup; active.EndedUtc = DateTime.UtcNow.ToString("o");
                    active.EndMode = hFlag == null ? -1 : (int)hFlag.mode; active.EndTime = p.ReadGameTime();
                    active.CountAfter = Counts(hFlag); active.EndState = NativeState(hFlag, proc as HSceneProc);
                    foreach (var person in active.People) { person.AtEnd = Attributes(person.Native); person.ModelAtEnd = Model(person.Native); }
                    pending = active; active = null;
                    p.AddEvent("game_h_ended", Json.Map("sessionId", pending.Id, "world", pending.World, "endedUtc", pending.EndedUtc, "settlement", "pending"));
                }
                catch (Exception e) { p.Logger.LogWarning("H end report: " + e.Message); EndWithoutCallback("end_snapshot_failed"); }
            }

            internal void AnimationChanged(HFlag flags, string state)
            {
                if (stopped || active == null || !ReferenceEquals(active.Flags, flags)) return;
                ObserveParticipants();
                if (state == active.LastState) return;
                active.Transitions++; active.LastState = state;
                if (state == "Orgasm" && (int)flags.mode == 3) active.SoloClimaxes++;
                if (state == "Orgasm" && (int)flags.mode == 5) active.LesbianClimaxes++;
            }

            void ObserveParticipants()
            {
                var run = active; var flags = run.Flags;
                if (flags == null) return;
                int mode = (int)flags.mode;
                if (!run.Modes.Contains(mode)) run.Modes.Add(mode);
                if (flags.lstHeroine != null)
                    for (int i = 0; i < flags.lstHeroine.Count; i++) AddParticipant(flags.lstHeroine[i], "female:" + i, null);
                // A context player object is created even for solo/lesbian/peeping scenes.
                bool independent = mode == 3 || mode == 4 || mode == 5;
                // EndProc/NewHeroineEndProc count player participation in these modes even when his model is hidden.
                bool interactive = mode == 0 || mode == 1 || mode == 2 || mode >= 6 && mode <= 9;
                if (interactive && flags.player != null)
                { run.PlayerParticipated = true; AddParticipant(flags.player, "male:0", null); }
                else if (independent && run.PlayerParticipated != true) run.PlayerParticipated = false;
                var proc = run.Proc as HSceneProc;
                if (proc != null && (mode == 8 || mode == 9))
                {
                    var male = AccessTools.Field(typeof(HSceneProc), "male1").GetValue(proc) as ChaControl;
                    if (male != null) AddParticipant(null, "male:1", male.fileParam.lastname + male.fileParam.firstname);
                }
            }

            void AddParticipant(SaveData.CharaData data, string slot, string modelName)
            {
                if (data == null && modelName == null) return;
                foreach (var person in active.People)
                    if (person.Slot == slot && ReferenceEquals(person.Native, data)) return;
                bool bridge = data != null && ReferenceEquals(data, active.BridgeHeroine);
                bool player = data != null && active.Save != null && ReferenceEquals(data, active.Save.player);
                var heroine = data as SaveData.Heroine;
                int index = heroine == null || active.Save == null ? -1 : active.Save.heroineList.IndexOf(heroine);
                string id = bridge ? ActorId : player ? "player:main" : index >= 0 ? "native:" + active.World + ":" + index
                    : "h:" + active.Id + ":" + slot;
                active.Bridge |= bridge;
                active.People.Add(new Participant { Native = data, Slot = slot, FirstObservedUtc = DateTime.UtcNow.ToString("o"),
                    Before = active.CapturingStart ? Attributes(data) : data != null && ReferenceEquals(data, active.ContextPlayer) ? active.PlayerBefore : null,
                    Actor = Json.Map("id", id, "role", bridge ? "bridge" : player ? "player" : data == null ? "unresolved_model" : data is SaveData.Player ? "scene_player" : "npc",
                        "name", data == null ? modelName : data.Name, "world", active.World, "generation", bridge ? active.Generation : null,
                        "schoolClass", data == null ? null : (object)data.schoolClass, "schoolClassIndex", data == null ? null : (object)data.schoolClassIndex,
                        "identitySource", bridge || player ? "native_reference" : index >= 0 ? "native_roster_reference" : data == null ? "model_only" : "scene_local_reference") });
            }

            static object[] ParticipantActors(Run run)
            { var people = new List<object>(); foreach (var person in run.People) people.Add(Json.Map("slot", person.Slot, "actor", person.Actor)); return people.ToArray(); }

            internal object Status()
            {
                var run = active ?? pending;
                return run == null ? null : Json.Map("sessionId", run.Id, "world", run.World, "phase", active == null ? "settling" : "active",
                    "bridgeParticipated", run.Bridge, "startedUtc", run.StartedUtc, "nativeEndObserved", run.NativeEnd);
            }

            // Called from the existing world refresh; no H-scene discovery or extra polling loop.
            internal void WorldRefreshed()
            {
                if (stopped || Owner == null) return;
                if (Last != null && lastGeneration != Owner.ownedGeneration) Last = null;
                var run = active ?? pending;
                if (run != null && (run.World != Owner.world || run.Generation != Owner.ownedGeneration || !ReferenceEquals(run.Save, Owner.previousSave)))
                { WorldChanged(); return; }
                if (pending == null || active != null || GameAPI.InsideHScene || Owner.SceneBusy) return;
                if (Manager.Scene.IsInstance()) foreach (string scene in Manager.Scene.Instance.NowSceneNames)
                    if (scene == "H" || scene == "HProc" || scene == "HSceneResult") return;
                if (pending.FreeH) { Complete("not_applicable", "free_h_returned"); return; }
                string mode = Owner.Mode;
                if (mode != "roaming" && mode != "night_menu")
                {
                    if (mode == "menu") Complete("unavailable", "story_not_resumed");
                    return;
                }
                foreach (var person in pending.People) person.Final = Attributes(person.Native);
                Complete("final", "normal_story_returned");
            }

            internal void WorldChanged()
            {
                if (active != null) EndWithoutCallback("world_changed");
                if (pending != null) Complete("unavailable", "world_changed");
                Last = null;
            }

            void SceneUnloaded(UnityEngine.SceneManagement.Scene scene)
            {
                if (stopped || active == null || active.Proc != null) return;
                EndWithoutCallback("scene_unloaded_without_end");
            }

            void EndWithoutCallback(string reason)
            {
                if (active == null) return;
                active.Ended = Time.realtimeSinceStartup; active.EndedUtc = DateTime.UtcNow.ToString("o");
                active.EndMode = -1; pending = active; active = null;
                Complete("unavailable", reason);
            }

            internal void StopObserving()
            {
                if (stopped) return;
                if (active != null) EndWithoutCallback("plugin_unloaded");
                if (pending != null) Complete("unavailable", "plugin_unloaded");
                stopped = true; SceneManager.sceneUnloaded -= SceneUnloaded;
            }

            void Complete(string settlement, string reason)
            {
                Run run = pending;
                if (run == null) return;
                pending = null; // Clear before publication so reentrant/duplicate callbacks cannot complete twice.
                var people = new List<object>();
                foreach (var person in run.People)
                    people.Add(Json.Map("slot", person.Slot, "actor", person.Actor, "firstObservedUtc", person.FirstObservedUtc,
                        "statsAtStart", person.Before, "statsAtNativeEnd", person.AtEnd, "statsAfterSettlement", person.Final,
                        "baselineUnavailableReason", person.Before == null ? person.Native == null ? "no_native_character_record" : "participant_joined_after_kkapi_start" : null,
                        "deltaAtNativeEnd", Delta(person.Before, person.AtEnd, false), "delta", Delta(person.Before, person.Final, false),
                        "modelAtNativeEnd", person.ModelAtEnd, "unavailableReason", person.Native == null ? "no_native_character_record" : person.Final == null ? reason : null));
                object nativeOrgasms = run.CountAfter == null ? null : (object)(Convert.ToInt32(run.CountAfter["aibuOrg"]) + Convert.ToInt32(run.CountAfter["sonyuOrg"]) + Convert.ToInt32(run.CountAfter["sonyuAnalOrg"]));
                object nativeFinishes = run.CountAfter == null ? null : (object)(Convert.ToInt32(run.CountAfter["houshiOutside"]) + Convert.ToInt32(run.CountAfter["houshiInside"]) + Convert.ToInt32(run.CountAfter["sonyuCondomInside"]) + Convert.ToInt32(run.CountAfter["sonyuInside"]) + Convert.ToInt32(run.CountAfter["sonyuOutside"]) + Convert.ToInt32(run.CountAfter["sonyuAnalCondomInside"]) + Convert.ToInt32(run.CountAfter["sonyuAnalInside"]) + Convert.ToInt32(run.CountAfter["sonyuAnalOutside"]));
                bool countedMode = false; foreach (int mode in run.Modes) if (mode == 0 || mode == 1 || mode == 2 || mode >= 6 && mode <= 9) countedMode = true;
                if (!countedMode) { nativeOrgasms = null; nativeFinishes = null; }
                var saturated = new List<object>();
                if (run.CountAfter != null) foreach (var value in run.CountAfter) if (value.Value is int && (int)value.Value >= 10000) saturated.Add(value.Key);
                var modes = new List<object>(); foreach (int mode in run.Modes) modes.Add(ModeRef(mode));
                Last = Json.Map("schema", 1, "sessionId", run.Id, "world", run.World, "bridgeGeneration", run.Bridge ? run.Generation : null,
                    "state", run.NativeEnd ? "completed" : "aborted", "reason", reason, "nativeEndObserved", run.NativeEnd,
                    "startedUtc", run.StartedUtc, "endedUtc", run.EndedUtc, "completedUtc", DateTime.UtcNow.ToString("o"),
                    "durationSeconds", Math.Max(0, run.Ended - run.Started), "durationScope", run.NativeEnd ? "kkapi_start_to_native_end" : "kkapi_start_to_abort_detection",
                    "gameTimeStart", run.StartTime, "gameTimeEnd", run.EndTime, "isFreeH", run.FreeH, "vr", run.Vr,
                    "startMode", ModeRef(run.StartMode), "endMode", run.NativeEnd ? ModeRef(run.EndMode) : null, "modesObserved", modes.ToArray(),
                    "playerParticipated", run.PlayerParticipated, "bridgeParticipated", run.Bridge, "participantCount", people.Count, "participants", people.ToArray(),
                    "statistics", Json.Map("source", "HFlag.count", "scope", "native_hproc", "atStart", run.CountBefore, "atEnd", run.CountAfter,
                        "observedDelta", Delta(run.CountBefore, run.CountAfter, true), "nativeOrgasmTotal", nativeOrgasms, "nativeFinishTotal", nativeFinishes,
                        "nativeTotalsUnavailableReason", !run.NativeEnd ? "native_end_not_observed" : !countedMode ? "not_counted_in_independent_modes" : run.CountAfter == null ? "native_count_unavailable" : null,
                        "saturatedCounters", saturated.ToArray(),
                        "perParticipant", null, "perParticipantUnavailableReason", "native_counters_are_scene_aggregate"),
                    "observations", Json.Map("source", "HActionBase.SetPlay", "scope", "after_kkapi_start", "stateTransitions", run.Transitions,
                        "masturbationClimaxEntries", run.Modes.Contains(3) ? (object)run.SoloClimaxes : null,
                        "lesbianClimaxEntries", run.Modes.Contains(5) ? (object)run.LesbianClimaxes : null,
                        "initialState", run.InitialState, "initialClimaxInProgress", run.InitialClimax, "lastState", run.LastState, "perParticipant", null),
                    "nativeEndState", run.EndState, "settlement", Json.Map("state", settlement, "reason", reason, "scope", "observed_attribute_changes",
                        "sampledUtc", settlement == "final" ? DateTime.UtcNow.ToString("o") : null));
                Plugin p = Owner;
                lastGeneration = run.Generation;
                if (p != null) p.AddEvent("game_h_completed", Last);
            }

            static object ModeRef(int id)
            { return Json.Map("id", id, "nativeName", Enum.IsDefined(typeof(HFlag.EMode), id) ? ((HFlag.EMode)id).ToString() : null); }

            static object Finite(double value) { return double.IsNaN(value) || double.IsInfinity(value) ? null : (object)value; }
            static object[] Values(float[] values)
            { if (values == null) return null; var result = new object[values.Length]; for (int i = 0; i < values.Length; i++) result[i] = Finite(values[i]); return result; }

            static Dictionary<string, object> Attributes(SaveData.CharaData data)
            {
                var h = data as SaveData.Heroine;
                if (h != null) return Json.Map("favor", h.favor, "intimacy", h.intimacy, "lewdness", h.lewdness, "anger", h.anger, "hCount", h.hCount,
                    "relation", h.relation, "isGirlfriend", h.isGirlfriend, "isKiss", h.isKiss, "isVirgin", h.isVirgin, "isAnalVirgin", h.isAnalVirgin,
                    "countNamaInsert", h.countNamaInsert, "countHoushi", h.countHoushi, "houshiExp", Finite(h.houshiExp),
                    "countKokanH", Finite(h.countKokanH), "countAnalH", Finite(h.countAnalH), "hAreaExps", Values(h.hAreaExps), "massageExps", Values(h.massageExps));
                var p = data as SaveData.Player;
                return p == null ? null : Json.Map("hCount", p.hcount, "orgCount", p.orgCount, "hPeopleCount", p.hPeopleCount, "hentai", p.hentai);
            }

            static Dictionary<string, object> Model(SaveData.CharaData data)
            {
                try
                {
                    var c = data == null ? null : data.chaCtrl;
                    if (c == null || c.fileStatus == null) return null;
                    return Json.Map("loaded", c.loadEnd, "visible", c.visibleAll, "coordinateType", (int)c.fileStatus.coordinateType,
                        "clothesState", c.fileStatus.clothesState == null ? null : c.fileStatus.clothesState.Clone(),
                        "expression", FaceReady(c) ? ReadFace(c) : null);
                }
                catch (Exception) { return null; } // Optional visuals can already be unavailable; attribute data stays independent.
            }

            static object NativeState(HFlag f, HSceneProc proc)
            {
                if (f == null) return null;
                var a = f.nowAnimationInfo;
                return Json.Map("mode", ModeRef((int)f.mode), "gaugeFemale", Finite(f.gaugeFemale), "gaugeMale", Finite(f.gaugeMale),
                    "isCondom", f.isCondom, "isAnalPlay", f.isAnalPlay, "finishKind", Json.Map("id", (int)f.finish, "nativeName", f.finish.ToString()),
                    "isHSceneEnd", f.isHSceneEnd, "numEnd", f.numEnd, "immediateFinish", f.isUseImmediatelyFinishButton,
                    "animationState", f.nowAnimStateName, "animation", a == null ? null : Json.Map("id", a.id, "mode", ModeRef((int)a.mode), "name", a.nameAnimation),
                    "finishADV", proc == null ? null : (object)proc.finishADV,
                    "pendingExperience", proc == null || proc.addParameter == null ? null : Json.Map("aibus", Values(proc.addParameter.aibus), "houshi", Finite(proc.addParameter.houshi), "sonyus", Values(proc.addParameter.sonyus)));
            }

            static Dictionary<string, object> Counts(HFlag f)
            {
                if (f == null || f.count == null) return null;
                var c = f.count; var mouse = new List<object>();
                if (c.mouseAction != null)
                    for (int i = 0; i < c.mouseAction.GetLength(0); i++) for (int j = 0; j < c.mouseAction.GetLength(1); j++)
                    { var m = c.mouseAction[i, j]; if (m != null && (m.click || m.drag)) mouse.Add(Json.Map("item", i, "area", j, "click", m.click, "drag", m.drag)); }
                return Json.Map("aibuOrg", c.aibuOrg, "kiss", c.kiss, "notKiss", c.notKiss, "notPowerful", c.notPowerful,
                    "dontTouchAnal", c.dontTouchAnal, "dontTouchMassage", c.dontTouchMassage,
                    "houshiOutside", c.houshiOutside, "houshiInside", c.houshiInside, "handFinish", c.handFinish, "nameFinish", c.nameFinish,
                    "kuwaeFinish", c.kuwaeFinish, "paizuriFinish", c.paizuriFinish, "paizurinameFinish", c.paizurinameFinish, "paizurikuwaeFinish", c.paizurikuwaeFinish,
                    "houshiDrink", c.houshiDrink, "houshiVomit", c.houshiVomit, "splash", c.splash,
                    "sonyuCondomInside", c.sonyuCondomInside, "sonyuOrg", c.sonyuOrg, "sonyuInside", c.sonyuInside, "sonyuOutside", c.sonyuOutside,
                    "sonyuSame", c.sonyuSame, "sonyuCondomSame", c.sonyuCondomSame, "sonyuTare", c.sonyuTare,
                    "sonyuAnalCondomInside", c.sonyuAnalCondomInside, "sonyuAnalOrg", c.sonyuAnalOrg, "sonyuAnalInside", c.sonyuAnalInside,
                    "sonyuAnalOutside", c.sonyuAnalOutside, "sonyuAnalSame", c.sonyuAnalSame, "sonyuAnalCondomSame", c.sonyuAnalCondomSame,
                    "sonyuAnalTare", c.sonyuAnalTare, "notCondomPlay", c.notCondomPlay, "notAnalPlay", c.notAnalPlay,
                    "sonyuKokanPlay", c.sonyuKokanPlay, "sonyuAnalPlay", c.sonyuAnalPlay,
                    "isInsertKokan", c.isInsertKokan, "isInsertAnal", c.isInsertAnal, "isNameInsertKokan", c.isNameInsertKokan, "isHoushiPlay", c.isHoushiPlay,
                    "isInsertKokanVoiceCondition", c.isInsertKokanVoiceCondition, "isInsertAnalVoiceCondition", c.isInsertAnalVoiceCondition,
                    "selectMassages", Values(c.selectMassages), "selectAreas", Values(c.selectAreas), "selectAreasGlobal", Values(c.selectAreasGlobal),
                    "selectHobby", Finite(c.selectHobby), "mouseActions", c.mouseAction == null ? null : mouse.ToArray());
            }

            static bool Numeric(object value) { return value is int || value is long || value is float || value is double; }
            static Dictionary<string, object> Delta(Dictionary<string, object> before, Dictionary<string, object> after, bool monotonic)
            {
                if (before == null || after == null) return null;
                var result = new Dictionary<string, object>();
                foreach (var pair in before)
                {
                    object end; if (!after.TryGetValue(pair.Key, out end)) continue;
                    if (Numeric(pair.Value) && Numeric(end))
                    { double change = Convert.ToDouble(end) - Convert.ToDouble(pair.Value); result[pair.Key] = monotonic && (change < 0 || end is int && (int)end >= 10000) ? null : Finite(change); }
                    else if (pair.Value is object[] && end is object[] && pair.Key != "mouseActions")
                    {
                        var a = (object[])pair.Value; var b = (object[])end;
                        if (a.Length != b.Length) { result[pair.Key] = null; continue; }
                        var values = new object[a.Length];
                        for (int i = 0; i < values.Length; i++)
                        { double change = Numeric(a[i]) && Numeric(b[i]) ? Convert.ToDouble(b[i]) - Convert.ToDouble(a[i]) : double.NaN; values[i] = monotonic && change < 0 ? null : Finite(change); }
                        result[pair.Key] = values;
                    }
                }
                return result;
            }
        }
    }
}
