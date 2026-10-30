using System;
using System.Collections.Generic;
using ActionGame;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        Cycle observedCycle;
        int observedDayIndex, observedStage = -1, observedWeekday = -1;
        bool newDayPending;
        static readonly string[] StageKeys = { "wake_up", "morning", "going_to_school", "homeroom_1", "lesson_1", "lunch_time", "lesson_2", "homeroom_2", "staff_time", "after_school", "going_home", "my_house" };
        static readonly string[] StageNames = { "Wake up", "Morning", "Going to school", "Morning homeroom", "Morning classes", "Lunch", "Afternoon classes", "Afternoon homeroom", "Staff period", "After school", "Going home", "At home / night" };
        static readonly string[] WeekdayNames = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday / holiday" };
        static int PeriodId(int stage) { return stage < 0 || stage > 11 ? -1 : stage <= 4 ? 0 : stage <= 7 ? 1 : stage == 8 ? 2 : stage <= 10 ? 3 : 4; }
        static Dictionary<string, object> StageReference(int id)
        { return Json.Map("id", id, "key", id >= 0 && id < StageKeys.Length ? StageKeys[id] : "unknown", "name", id >= 0 && id < StageNames.Length ? StageNames[id] : "Unknown", "nativeName", id >= 0 ? ((Cycle.Type)id).ToString() : null,
            "description", "Exact native story cycle phase; period groups these stages under the native time UI."); }
        static Dictionary<string, object> PeriodReference(int id)
        {
            string[] keys = { "morning", "midday", "staff_time", "after_school", "night" };
            string[] names = { "Morning", "Lunch / afternoon classes", "Staff period", "After school", "Night" };
            return Json.Map("id", id, "key", id >= 0 && id < keys.Length ? keys[id] : "unknown", "name", id >= 0 && id < names.Length ? names[id] : "Unknown",
                "description", "Native TimeUIControl time-zone index; stage gives the exact native cycle phase.");
        }
        static Dictionary<string, object> WeekdayReference(int id)
        { return Json.Map("id", id, "key", id >= 0 && id < 7 ? ((Cycle.Week)id).ToString().ToLowerInvariant() : "unknown", "name", id >= 0 && id < 7 ? WeekdayNames[id] : "Unknown", "nativeName", id >= 0 ? ((Cycle.Week)id).ToString() : null, "isWeekend", id == 5 || id == 6,
            "description", "Native weekday index, Monday through Sunday/holiday; this is not an absolute calendar date."); }
        void InstallTimeHooks()
        {
            PatchNative(typeof(Cycle), "Change", new Type[] { typeof(Cycle.Type) }, null, "NativeStageChanged", null);
            PatchNative(typeof(Cycle), "Change", new Type[] { typeof(Cycle.Week) }, "NativeDayChanging", "NativeDayChanged", null);
        }
        void TickGameTime()
        {
            Cycle cycle = action == null ? null : action.Cycle;
            if (cycle == null) { observedCycle = null; return; }
            if (observedCycle != cycle)
            {
                if (SceneBusy || Mode != "roaming" && Mode != "night_menu") return;
                observedCycle = cycle; observedStage = (int)cycle.nowType; observedWeekday = (int)cycle.nowWeek; newDayPending = false;
                AddEvent("game_time_initialized", ReadGameTime()); return;
            }
            ObserveStage(cycle);
        }
        static void NativeStageChanged(Cycle __instance)
        { if (Current != null && Current.observedCycle == __instance) Current.ObserveStage(__instance); }
        void ObserveStage(Cycle cycle)
        {
            int stage = (int)cycle.nowType;
            if (stage == observedStage) return;
            int before = observedStage; observedStage = stage;
            if (stage != 0) newDayPending = false;
            AddEvent("game_stage_changed", Json.Map("world", world, "from", StageReference(before), "to", StageReference(stage), "time", ReadGameTime()));
            if (PeriodId(before) != PeriodId(stage))
                AddEvent("game_time_period_changed", Json.Map("world", world, "from", PeriodReference(PeriodId(before)), "to", PeriodReference(PeriodId(stage)), "time", ReadGameTime()));
            if (stage == 11) AddEvent("game_day_ending", Json.Map("world", world, "day", ReadDay(), "reason", "native_night_entered"));
        }
        static void NativeDayChanging(Cycle __instance, ref int __state)
        { __state = Current != null && Current.observedCycle == __instance ? (int)__instance.nowWeek : -1; }
        static void NativeDayChanged(Cycle __instance, int __state)
        {
            Plugin p = Current;
            if (p == null || __state < 0 || p.observedCycle != __instance) return;
            int next = (int)__instance.nowWeek, advanced = (next - __state + 7) % 7;
            if (advanced == 0) advanced = 7;
            var previous = p.ReadDay();
            p.AddEvent("game_day_ended", Json.Map("world", p.world, "day", previous, "nextWeekday", WeekdayReference(next), "daysAdvanced", advanced, "reason", "native_calendar_advanced"));
            p.observedDayIndex += advanced; p.observedWeekday = next;
            p.newDayPending = true;
            p.SyncOwnedStats(); p.SaveNativeProfile(); p.SaveOwnedProfile();
            p.AddEvent("game_day_started", Json.Map("world", p.world, "day", p.ReadDay(), "previousWeekday", WeekdayReference(__state), "daysAdvanced", advanced));
        }
        object ReadDay()
        { return Json.Map("id", world + ":" + observedDayIndex, "observedDayIndex", observedDayIndex, "weekday", WeekdayReference(observedWeekday), "calendarDate", null,
            "dateSource", "native_weekday_with_observed_day_counter", "isDayEnding", !newDayPending && observedStage == 11, "isNewDay", newDayPending || observedStage == 0); }
        object ReadGameTime()
        {
            bool ready = action != null && action.Cycle != null && observedCycle == action.Cycle;
            return Json.Map("available", ready, "stage", StageReference(ready ? (int)observedCycle.nowType : -1), "period", PeriodReference(ready ? PeriodId((int)observedCycle.nowType) : -1),
                "day", ready ? ReadDay() : null, "timerSeconds", ready ? observedCycle.timer : 0,
                "description", "The game exposes a weekday and cycle stages, not a Gregorian date. observedDayIndex counts observed native calendar advances in this save profile.");
        }
        object ReadTimeCatalog()
        {
            var stages = new List<object>(); for (int id = 0; id < StageKeys.Length; id++) stages.Add(StageReference(id));
            var periods = new List<object>(); for (int id = 0; id < 5; id++) periods.Add(PeriodReference(id));
            var weekdays = new List<object>(); for (int id = 0; id < 7; id++) weekdays.Add(WeekdayReference(id));
            return Json.Map("stages", stages, "periods", periods, "weekdays", weekdays);
        }
        object ReadPlayer()
        {
            var save = Manager.Game.IsInstance() ? Manager.Game.Instance.saveData : null;
            var player = save == null ? null : save.player;
            if (player == null || !player.charFileInitialized) return null;
            bool loaded = action != null && action.Player != null;
            return Json.Map("name", player.Name, "lastName", player.lastname, "firstName", player.firstname, "nickname", player.nickname, "loaded", loaded,
                "map", loaded ? (object)action.Player.mapNo : null, "mapInfo", loaded ? MapReference(action.Player.mapNo) : null, "position", loaded ? Vector(action.Player.position) : null);
        }
    }
}
