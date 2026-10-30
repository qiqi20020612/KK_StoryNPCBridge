using System;
using System.Collections.Generic;
using System.Reflection;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        static readonly string[] ScoreFields = { "favor", "intimacy", "anger", "lewdness" };
        static readonly string[] CounterFields = { "hCount", "nickNameTalkCount", "myRoomCount", "eventAfterDay" };
        static readonly string[] FlagFields = { "isAnger", "isGirlfriend", "isStaff", "isDate", "isTaked", "confessed", "isFirstGirlfriend", "isLunch", "isTalkPoint", "isKiss", "isVirgin", "isAnalVirgin" };
        static bool Contains(string[] fields, string key) { return Array.IndexOf(fields, key) >= 0; }
        static FieldInfo StatField(string name) { return typeof(SaveData.Heroine).GetField(name, BindingFlags.Instance | BindingFlags.Public); }
        static Dictionary<string, object> ZeroStats()
        {
            var result = new Dictionary<string, object>();
            foreach (string key in ScoreFields) result[key] = 0;
            foreach (string key in CounterFields) result[key] = 0;
            foreach (string key in FlagFields) result[key] = false;
            result["hasMet"] = false; result["isFriend"] = false; result["relation"] = -1;
            return result;
        }
        static void ValidateStats(Dictionary<string, object> changes)
        {
            if (changes.Count == 0) throw new BridgeException("invalid_argument", "changes is empty");
            foreach (var pair in changes)
            {
                string key = pair.Key;
                if (Contains(FlagFields, key) || key == "hasMet" || key == "isFriend") Boolean(pair.Value, key);
                else if (Contains(ScoreFields, key) || Contains(CounterFields, key) || key == "relation")
                {
                    double value = Number(pair.Value, key == "relation" ? -1 : 0, key == "relation" ? 2 : Contains(ScoreFields, key) ? 100 : int.MaxValue, key);
                    if (value != Math.Floor(value)) throw new BridgeException("invalid_argument", key + " must be an integer");
                }
                else throw new BridgeException("unsupported_field", key);
            }
        }
        Dictionary<string, object> RelationshipReference(int id)
        {
            if (id < -1 || id > 2) throw new BridgeException("unknown_reference", "Unknown relationship id");
            string[] keys = { "stranger", "acquaintance", "friend", "girlfriend" };
            string[] names = { "Not met", "Acquaintance", "Friend", "Girlfriend" };
            return Json.Map("id", id, "key", keys[id + 1], "name", names[id + 1],
                "description", "Native relationship derived from meeting/friend talk-event flags and isGirlfriend. Setting this reference updates those flags together.");
        }
        Dictionary<string, object> NormalizeStats(Dictionary<string, object> changes)
        {
            var result = new Dictionary<string, object>(changes); object value;
            if (result.TryGetValue("relation", out value))
            {
                int id = ReferenceId(value, "relationship", RelationshipReference, delegate(string key) {
                    for (int i = -1; i <= 2; i++) if ((string)RelationshipReference(i)["key"] == key) return i;
                    return int.MinValue;
                });
                result["relation"] = id;
                foreach (string key in new string[] { "hasMet", "isFriend", "isGirlfriend" })
                {
                    bool expected = key == "hasMet" ? id >= 0 : key == "isFriend" ? id >= 1 : id == 2;
                    if (result.TryGetValue(key, out value) && Boolean(value, key) != expected)
                        throw new BridgeException("reference_mismatch", "relation conflicts with " + key);
                    result[key] = expected;
                }
            }
            if (result.ContainsKey("anger") && !result.ContainsKey("isAnger")) result["isAnger"] = Number(result["anger"], 0, 100, "anger") > 0;
            ValidateStats(result); return result;
        }
        void SyncOwnedStats()
        {
            if (ownedHeroine == null) return;
            foreach (string key in ScoreFields) ownedStats[key] = StatField(key).GetValue(ownedHeroine);
            foreach (string key in CounterFields) ownedStats[key] = StatField(key).GetValue(ownedHeroine);
            foreach (string key in FlagFields) ownedStats[key] = StatField(key).GetValue(ownedHeroine);
            ownedStats["hasMet"] = ownedHeroine.talkEvent.Contains(0) || ownedHeroine.talkEvent.Contains(1);
            ownedStats["isFriend"] = ownedHeroine.talkEvent.Contains(2);
            ownedStats["relation"] = ownedHeroine.relation;
        }
        void WriteNativeStats(Dictionary<string, object> changes)
        {
            foreach (var pair in changes)
            {
                if (pair.Key == "relation") continue;
                if (pair.Key == "hasMet")
                {
                    if ((bool)pair.Value) { ownedHeroine.talkEvent.Add(0); ownedHeroine.talkEvent.Add(1); }
                    else { ownedHeroine.talkEvent.Remove(0); ownedHeroine.talkEvent.Remove(1); }
                }
                else if (pair.Key == "isFriend")
                { if ((bool)pair.Value) ownedHeroine.talkEvent.Add(2); else ownedHeroine.talkEvent.Remove(2); }
                else StatField(pair.Key).SetValue(ownedHeroine, Contains(FlagFields, pair.Key) ? pair.Value : (object)Convert.ToInt32(pair.Value));
            }
        }
        void ApplyOwnedStats() { if (ownedHeroine != null) WriteNativeStats(ownedStats); }
        object SetOwnedStats(Dictionary<string, object> input)
        {
            if (ownedHeroine == null || !Manager.Game.IsInstance() || !ReferenceEquals(ownedSave, Manager.Game.Instance.saveData))
                throw new BridgeException("not_ready", "Load a story before changing native attributes");
            var changes = NormalizeStats(input); SyncOwnedStats();
            var before = new Dictionary<string, object>(ownedStats);
            var eventsBefore = new HashSet<int>(ownedHeroine.talkEvent);
            try { WriteNativeStats(changes); SyncOwnedStats(); SaveNativeProfile(); SaveOwnedProfile(); }
            catch
            {
                WriteNativeStats(before); ownedHeroine.talkEvent.Clear(); foreach (int id in eventsBefore) ownedHeroine.talkEvent.Add(id);
                SyncOwnedStats();
                try { SaveNativeProfile(); SaveOwnedProfile(); } catch (Exception e) { Logger.LogWarning("Attribute rollback persistence: " + e.Message); }
                throw;
            }
            AddEvent("stats_changed", Json.Map("requested", changes, "before", before, "actual", new Dictionary<string, object>(ownedStats)));
            return ReadOwnedActor();
        }
        object ReadStatCatalog()
        {
            var fields = new List<object>();
            foreach (string key in ScoreFields) fields.Add(Json.Map("key", key, "type", "integer", "min", 0, "max", 100, "description", StatDescription(key)));
            foreach (string key in CounterFields) fields.Add(Json.Map("key", key, "type", "integer", "min", 0, "max", int.MaxValue, "description", StatDescription(key)));
            foreach (string key in FlagFields) fields.Add(Json.Map("key", key, "type", "boolean", "description", StatDescription(key)));
            fields.Add(Json.Map("key", "hasMet", "type", "boolean", "description", "Meeting talk-event markers 0/1; writes add or remove both markers."));
            fields.Add(Json.Map("key", "isFriend", "type", "boolean", "description", "Friendship talk-event marker 2."));
            fields.Add(Json.Map("key", "relation", "type", "reference", "description", "Relationship id, key or returned relationship object."));
            var relations = new List<object>(); for (int id = -1; id <= 2; id++) relations.Add(RelationshipReference(id));
            return Json.Map("fields", fields, "relationships", relations, "sceneRestricted", false, "ownershipRequired", true);
        }
        static string StatDescription(string key)
        {
            switch (key)
            {
                case "favor": return "Affection toward the player.";
                case "intimacy": return "Intimacy with the player.";
                case "anger": return "Anger score; writing this alone also updates isAnger.";
                case "lewdness": return "Native lewdness score.";
                case "hCount": return "Native intimate encounter count.";
                case "nickNameTalkCount": return "Native nickname conversation count.";
                case "myRoomCount": return "Native player-room visit count.";
                case "eventAfterDay": return "Native days-after-event counter.";
                case "isAnger": return "Whether the heroine is angry.";
                case "isGirlfriend": return "Whether the heroine is the player's girlfriend.";
                case "isStaff": return "Whether the heroine is a Koikatsu club member.";
                case "isDate": return "Whether a date has been promised.";
                case "isTaked": return "Native isTaked state flag, preserved independently of the relationship.";
                case "confessed": return "Native confession state flag.";
                case "isFirstGirlfriend": return "Native first-girlfriend state flag.";
                case "isLunch": return "Native lunch state flag.";
                case "isTalkPoint": return "Native conversation-point state flag.";
                case "isKiss": return "Native kiss state flag.";
                case "isVirgin": return "Native vaginal virginity flag.";
                case "isAnalVirgin": return "Native anal virginity flag.";
                default: return key;
            }
        }
    }
}
