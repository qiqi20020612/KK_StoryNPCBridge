using System;
using HarmonyLib;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        SaveData.Heroine NativeDateHeroine()
        { return action == null || action.Cycle == null ? null : (SaveData.Heroine)AccessTools.Property(typeof(ActionGame.Cycle), "dateHeroine").GetValue(action.Cycle, null); }
        void SetNativeDateHeroine(SaveData.Heroine heroine)
        { AccessTools.Property(typeof(ActionGame.Cycle), "dateHeroine").SetValue(action.Cycle, heroine, null); }
        object NativeRanking()
        { return Manager.Game.IsInstance() ? AccessTools.Property(typeof(Manager.Game), "rankSaveData").GetValue(Manager.Game.Instance, null) : null; }
        static object EventProperty(object value, string name)
        { return value == null ? null : AccessTools.Property(value.GetType(), name).GetValue(value, null); }
        static void IncrementEventRanking(object ranking, string name)
        { var property = AccessTools.Property(ranking.GetType(), name); property.SetValue(ranking, unchecked(Convert.ToUInt32(property.GetValue(ranking, null)) + 1u), null); }
        bool NativeHLocationReady(TalkScene talk)
        {
            object info = talk == null ? null : AccessTools.Property(typeof(TalkScene), "info").GetValue(talk, null);
            return info != null && (bool)EventProperty(info, "isHPossible");
        }
        object CaptureNativeEventEffects()
        {
            SyncOwnedStats(); object ranking = NativeRanking();
            object report = ownedSave == null ? null : AccessTools.Field(typeof(SaveData), "clubReport").GetValue(ownedSave);
            return Json.Map("stats", new System.Collections.Generic.Dictionary<string, object>(ownedStats),
                "relationship", ownedHeroine == null ? null : RelationshipReference(ownedHeroine.relation), "dateScheduled", ownedHeroine != null && ReferenceEquals(NativeDateHeroine(), ownedHeroine),
                "girlfriendCount", EventProperty(ranking, "girlfriendCount"), "staffCount", EventProperty(ranking, "staffCount"),
                "playerGirlfriendedCount", ownedSave == null ? null : AccessTools.Field(typeof(SaveData.Player), "girlfriendedCnt").GetValue(ownedSave.player),
                "clubReportStaffAdd", EventProperty(report, "staffAdd"));
        }
        void ApplyNativeEventState(int id)
        {
            // Mirror the accepted native state branch without running an invitation/refusal roll or its dialogue.
            if (id == 1 && !ownedHeroine.isGirlfriend)
            {
                object ranking = NativeRanking();
                if (!ownedHeroine.isFirstGirlfriend) IncrementEventRanking(ranking, "girlfriendCount");
                ownedHeroine.talkEvent.Add(7); ownedHeroine.isGirlfriend = true; ownedHeroine.isFirstGirlfriend = true;
                var count = AccessTools.Field(typeof(SaveData.Player), "girlfriendedCnt");
                count.SetValue(ownedSave.player, unchecked((int)count.GetValue(ownedSave.player) + 1));
                if (OwnedTalkActive)
                {
                    object gauge = AccessTools.Field(typeof(TalkScene), "favorGaugeInfo").GetValue(currentTalk);
                    if (gauge != null) AccessTools.Method(gauge.GetType(), "SetStage", new Type[] { typeof(int) }, null).Invoke(gauge, new object[] { 2 });
                }
            }
            else if (id == 2)
            {
                if (ReferenceEquals(NativeDateHeroine(), ownedHeroine)) SetNativeDateHeroine(null);
                ownedHeroine.isGirlfriend = false; ownedHeroine.isDate = false; ownedHeroine.isAnger = true; ownedHeroine.anger = 100; ownedHeroine.talkTime = 0;
            }
            else if (id == 3) { ownedHeroine.talkEvent.Add(8); ownedHeroine.talkEvent.Add(12); }
            else if (id == 7)
            { SetNativeDateHeroine(ownedHeroine); ownedHeroine.talkEvent.Add(ownedHeroine.isGirlfriend ? 13 : 9); ownedHeroine.isDate = true; }
            else if (id == 10)
            {
                object ranking = NativeRanking();
                object report = AccessTools.Field(typeof(SaveData), "clubReport").GetValue(ownedSave);
                IncrementEventRanking(ranking, "staffCount");
                // The native ClubReport setter adds its argument; it does not assign an absolute total.
                AccessTools.Property(report.GetType(), "staffAdd").SetValue(report, 1, null);
                ownedHeroine.talkEvent.Add(10); ownedHeroine.isStaff = true;
            }
            SyncOwnedStats();
        }
        object NativeEventRequirements(int id)
        {
            int[] stages = id == 4 ? new int[] { 5 } : id == 5 ? new int[] { 8 } : id == 6 ? new int[] { 9 } : id == 8 || id == 9 ? new int[] { 5, 8, 9 } : new int[0];
            return Json.Map("minimumRelation", id == 1 || id == 3 || id == 4 || id == 7 ? (object)RelationshipReference(1) : null,
                "exactRelation", id == 2 ? RelationshipReference(2) : null, "clubMember", id == 5 ? (object)true : id == 10 ? (object)false : null,
                "stages", stages, "sameMap", true, "nativeHLocation", id == 3, "unusedLunch", id == 4, "unoccupiedDateSlot", id == 7);
        }
        static string NativeEventDescription(int id)
        {
            switch (id)
            {
                case 1: return "Become the player's girlfriend. Requires friend or girlfriend; already dating is idempotent. Updates native relationship markers and counters without opening or interrupting dialogue.";
                case 2: return "Break up with the player. Requires girlfriend; clears the date, sets native breakup anger and exits through the native breakup flow.";
                case 3: return "Start the native H scene. Requires friend or girlfriend and a valid native H location; location is checked again in the native event host.";
                case 4: return "Eat lunch together through the native activity flow. Requires friend or girlfriend, lunch time and no completed lunch today.";
                case 5: return "Do native club activities together. Requires club membership and the staff period.";
                case 6: return "Go home together through the native activity flow. Requires the after-school period.";
                case 7: return "Arrange a native date. Requires friend or girlfriend and no date with another NPC; updates the native calendar identity without opening or interrupting dialogue.";
                case 8: return "Study together through the native activity flow during free time. No relationship restriction.";
                case 9: return "Exercise together through the native activity flow during free time. No relationship restriction.";
                case 10: return "Admit the NPC to the club. Requires a nonmember; updates the native membership marker, ranking and club report without opening or interrupting dialogue.";
                default: return "Establish the native follow-player relation. No relationship or membership restriction; stop or a new task ends following.";
            }
        }
    }
}
