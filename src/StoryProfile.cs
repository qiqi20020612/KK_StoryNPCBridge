using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        string storySaveName, storyProfileKey;
        bool newStoryProfile;
        string StoryProfileDirectory(string saveName)
        {
            byte[] hash;
            using (var algorithm = SHA256.Create()) hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(saveName.ToLowerInvariant()));
            storyProfileKey = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return Path.Combine(Path.Combine(Paths.ConfigPath, "KK_StoryNPCBridge/profiles"), storyProfileKey);
        }
        void SelectStoryProfile()
        {
            string filename = Manager.Game.SaveFileName;
            storySaveName = string.IsNullOrEmpty(filename) ? "unsaved-" + Guid.NewGuid().ToString("N") : Path.GetFileName(filename);
            string directory = StoryProfileDirectory(storySaveName); Directory.CreateDirectory(directory);
            ownedStatePath = Path.Combine(directory, "owned-actor.json");
            newStoryProfile = !File.Exists(NativeProfilePath) && !File.Exists(ownedStatePath);
            ownedStats = ZeroStats(); ownedFace = NeutralFace(); observedDayIndex = 0; observedCycle = null; newDayPending = false;
            lastHandoverReport = null; activityPeriodId = null; activityDirty = false;
            if (File.Exists(ownedStatePath))
            {
                var data = Json.Parse(File.ReadAllText(ownedStatePath, Encoding.UTF8)) as Dictionary<string, object>;
                if (data == null || Text(data, "id") != ActorId) throw new FormatException("Invalid story NPC profile");
                var stats = Object(data, "stats");
                if (stats.Count == 0) ValidateStats(stats);
                foreach (var pair in stats)
                {
                    // Native effects can produce signed values outside the public write ranges.
                    if (Contains(ScoreFields, pair.Key) || Contains(CounterFields, pair.Key))
                    {
                        double number = Number(pair.Value, int.MinValue, int.MaxValue, pair.Key);
                        if (number != Math.Floor(number)) throw new FormatException("Invalid native integer in story NPC profile");
                    }
                    else ValidateStats(Json.Map(pair.Key, pair.Value));
                }
                foreach (var pair in stats) ownedStats[pair.Key] = pair.Value;
                object value;
                if (data.TryGetValue("face", out value) && value is Dictionary<string, object>)
                    foreach (var pair in (Dictionary<string, object>)value) if (ownedFace.ContainsKey(pair.Key)) ownedFace[pair.Key] = pair.Value;
                if (data.TryGetValue("observedDayIndex", out value)) observedDayIndex = (int)Number(value, 0, int.MaxValue, "observedDayIndex");
            }
            InitializeAutonomyJournal();
        }
        void SaveProfileForNativeSlot(FileStream stream)
        {
            if (stream == null || ownedHeroine == null) return;
            string saveDirectory = Path.GetFullPath(Path.Combine(Paths.GameRootPath, "UserData/save/game")).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(stream.Name)), saveDirectory, StringComparison.OrdinalIgnoreCase)) return;
            string name = Path.GetFileName(stream.Name);
            if (!string.Equals(storySaveName, name, StringComparison.OrdinalIgnoreCase))
            {
                storySaveName = name; string directory = StoryProfileDirectory(name); Directory.CreateDirectory(directory);
                ownedStatePath = Path.Combine(directory, "owned-actor.json"); newStoryProfile = false;
            }
            SyncOwnedStats(); SaveNativeProfile(); SaveOwnedProfile(); FlushAutonomyJournal();
        }
        object ReadStoryProfile()
        { return Json.Map("save", storySaveName, "key", storyProfileKey, "initializedThisLoad", newStoryProfile, "scope", "story_save_slot"); }
    }
}
