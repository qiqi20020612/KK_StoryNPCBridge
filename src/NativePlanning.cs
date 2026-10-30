using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ActionGame;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        object NativeDesireInfo()
        {
            if (action == null || action.actCtrl == null || ownedHeroine == null) return null;
            var targets = (IDictionary)AccessTools.Field(typeof(ActionControl), "dicTarget").GetValue(action.actCtrl);
            return targets == null ? null : targets[ownedHeroine];
        }
        static object PlanningProperty(object source, string name)
        { return AccessTools.Property(source.GetType(), name).GetValue(source, null); }
        List<object> PlanningActions(IEnumerable values, bool sort)
        {
            var ids = new List<int>();
            if (values != null) foreach (object value in values) ids.Add((int)value);
            if (sort) ids.Sort();
            var result = new List<object>(); foreach (int id in ids) result.Add(ActionReference(id));
            return result;
        }
        object PlanningPoint(WaitPoint point)
        { return point == null ? null : Json.Map("map", point.MapNo, "mapInfo", MapReference(point.MapNo), "position", Vector(point.position)); }
        Dictionary<string, object> CaptureNativePlan()
        {
            object desire = NativeDesireInfo();
            var ai = ownedNpc == null ? null : ownedNpc.AI;
            var active = ai == null ? null : ai.result;
            var selected = new List<object>();
            var candidates = new List<Dictionary<string, object>>();
            var result = Json.Map("available", desire != null, "unavailableReason", desire == null ? "native_scheduler_not_ready" : null,
                "generation", ownedGeneration, "world", world, "capturedUtc", DateTime.UtcNow.ToString("o"), "gameTime", ReadGameTime(),
                "selection", ownedController == null ? "game" : "plugin", "futureQueueAvailable", false,
                "description", "The native AI selects one action at a time. Candidates describe current desires, not a guaranteed future task queue. Reading never advances AI or reserves a destination.",
                "plannedActions", selected, "candidates", candidates);
            if (desire == null) return result;
            int id = (int)PlanningProperty(desire, "action");
            var point = (WaitPoint)PlanningProperty(desire, "nextPoint");
            var partner = PlanningProperty(desire, "heroine") as SaveData.Heroine;
            if (id >= 0 || active != null && active.actionNo >= 0)
            {
                int selectedId = id >= 0 ? id : active.actionNo;
                selected.Add(Json.Map("action", selectedId, "actionInfo", ActionReference(selectedId), "source", "native_selected_action",
                    "isPriority", PlanningProperty(desire, "isPriority"), "destination", PlanningPoint(point != null ? point : active == null ? null : active.point),
                    "plansNo", active == null ? -1 : active.plansNo, "plansName", active == null ? null : active.plansName,
                    "partner", partner == null ? null : AdvActorReference(partner)));
            }
            result["reservedDestination"] = PlanningPoint(point);
            // These collections are scheduler memory. Do not invoke Sort or treat them as a future queue.
            result["recentActions"] = PlanningActions((IEnumerable)AccessTools.Field(desire.GetType(), "_queueAction").GetValue(desire), false);
            result["recentPrioritySelections"] = PlanningActions((IEnumerable)AccessTools.Field(desire.GetType(), "_queuePriority").GetValue(desire), false);
            result["deferredPriorityActions"] = PlanningActions((IEnumerable)AccessTools.Field(desire.GetType(), "hashPriority").GetValue(desire), true);
            var desires = (IDictionary)PlanningProperty(desire, "dicDesire");
            var infos = (IDictionary)AccessTools.Field(typeof(ActionControl), "dicBaseInfo").GetValue(action.actCtrl);
            foreach (DictionaryEntry entry in desires)
            {
                int actionId = (int)entry.Key;
                object info = infos[actionId];
                bool priority = (bool)PlanningProperty(entry.Value, "isPriority");
                int weight = (int)PlanningProperty(entry.Value, "desire");
                var probability = info == null ? null : info.GetType().GetField("probability", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var candidate = ActionReference(actionId);
                candidate["desire"] = weight; candidate["isPriority"] = priority;
                candidate["priority"] = PlanningProperty(entry.Value, "priority");
                candidate["selectionKind"] = priority ? "priority" : probability != null ? "weighted_random" : "special";
                candidate["weight"] = probability == null ? null : (object)((int)probability.GetValue(info) + weight);
                candidate["priorityThresholdMet"] = priority && weight >= 100;
                candidate["eligibility"] = "not_evaluated";
                candidate["request"] = Json.Map("op", "set_action", "target", ActorId, "generation", ownedGeneration, "action", ActionReference(actionId));
                candidate["queueRequest"] = Json.Map("op", "queue_action", "target", ActorId, "generation", ownedGeneration, "action", ActionReference(actionId));
                candidates.Add(candidate);
            }
            candidates.Sort(delegate(Dictionary<string, object> a, Dictionary<string, object> b) {
                int order = ((bool)b["isPriority"]).CompareTo((bool)a["isPriority"]);
                if (order == 0) order = ((int)b["desire"]).CompareTo((int)a["desire"]);
                if (order == 0) order = ((int)b["priority"]).CompareTo((int)a["priority"]);
                return order != 0 ? order : ((int)a["id"]).CompareTo((int)b["id"]);
            });
            result["candidateOrder"] = "Priority group first, then descending desire and priority; ties by action ID. This is an observation order, not execution order. Native eligibility, desire growth, point/target availability and weighted randomness are not simulated.";
            return result;
        }
        object ReadNativePlanning()
        {
            object before = null;
            if (lastHandoverReport != null && lastHandoverReport.ContainsKey("generation") && (string)lastHandoverReport["generation"] == ownedGeneration)
                lastHandoverReport.TryGetValue("nativePlan", out before);
            return Json.Map("live", CaptureNativePlan(), "beforeControl", before);
        }
        void ClearNativePlanning()
        {
            object desire = NativeDesireInfo();
            if (desire == null) return;
            // SetWaitPoint releases the old reservation and partner without resetting desires or native flags.
            AccessTools.Method(desire.GetType(), "SetWaitPoint", new Type[] { typeof(WaitPoint), typeof(ActionGame.Chara.NPC) }, null).Invoke(desire, new object[] { null, ownedNpc });
            AccessTools.Field(desire.GetType(), "_action").SetValue(desire, -1);
            AccessTools.Property(desire.GetType(), "isPriority").SetValue(desire, false, null);
            ((Queue<int>)AccessTools.Field(desire.GetType(), "_queuePriority").GetValue(desire)).Clear();
            ((HashSet<int>)AccessTools.Field(desire.GetType(), "hashPriority").GetValue(desire)).Clear();
        }
    }
}
