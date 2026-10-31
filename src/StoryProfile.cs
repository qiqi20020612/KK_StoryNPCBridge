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
        bool newStoryProfile, writingStorySidecars;
        sealed class NativeStorySaveScope
        {
            internal NativeStorySaveScope Previous;
            internal SaveData Save;
            internal string Path;
            internal bool Serialized;
        }
        NativeStorySaveScope nativeStorySave;
        static bool IsStorySavePath(string filename)
        {
            string directory = Path.GetFullPath(Path.Combine(Paths.GameRootPath, "UserData/save/game")).TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(Path.GetDirectoryName(Path.GetFullPath(filename)), directory, StringComparison.OrdinalIgnoreCase);
        }
        static void BeforeStorySave(SaveData __instance, string __0, string __1, ref NativeStorySaveScope __state)
        {
            var p = Current;
            if (p == null || !ReferenceEquals(__instance, p.ownedSave) || p.ownedHeroine == null) return;
            string filename = Path.GetFullPath(__0 + __1);
            if (!IsStorySavePath(filename)) return;
            __state = new NativeStorySaveScope { Previous = p.nativeStorySave, Save = __instance, Path = filename };
            p.nativeStorySave = __state;
        }
        static Exception AfterStorySave(NativeStorySaveScope __state, Exception __exception)
        {
            var p = Current;
            if (p == null || __state == null) return __exception;
            p.nativeStorySave = __state.Previous;
            if (__exception == null && __state.Serialized && ReferenceEquals(__state.Save, p.ownedSave))
            {
                try { p.SaveProfileForNativeSlot(__state.Path); }
                catch (Exception e)
                {
                    p.Logger.LogWarning("Story profile save: " + e.Message);
                    p.AddEvent("profile_save_failed", Json.Map("save", Path.GetFileName(__state.Path), "error", e.Message));
                }
            }
            return __exception;
        }
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
            string directory = StoryProfileDirectory(storySaveName);
            ownedStatePath = Path.Combine(directory, "owned-actor.json");
            newStoryProfile = !File.Exists(NativeProfilePath) && !File.Exists(ownedStatePath);
            ownedStats = ZeroStats(); ownedFace = NeutralFace(); observedDayIndex = 0; observedCycle = null; newDayPending = false;
            lastHandoverReport = null; activityPeriodId = null;
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
                    foreach (var pair in (Dictionary<string, object>)value) if (IsFaceField(pair.Key)) ownedFace[pair.Key] = pair.Value;
                if (data.TryGetValue("observedDayIndex", out value)) observedDayIndex = (int)Number(value, 0, int.MaxValue, "observedDayIndex");
            }
            InitializeAutonomyJournal();
        }
        void SaveProfileForNativeSlot(string filename)
        {
            if (ownedHeroine == null || !IsStorySavePath(filename)) return;
            string name = Path.GetFileName(filename);
            if (!string.Equals(storySaveName, name, StringComparison.OrdinalIgnoreCase))
            {
                storySaveName = name; string directory = StoryProfileDirectory(name);
                ownedStatePath = Path.Combine(directory, "owned-actor.json"); newStoryProfile = false;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ownedStatePath));
            SyncOwnedStats(); writingStorySidecars = true;
            try { SaveNativeProfile(); SaveOwnedProfile(); FlushAutonomyJournal(); }
            finally { writingStorySidecars = false; }
            AddEvent("profile_save_completed", Json.Map("save", storySaveName, "key", storyProfileKey, "source", "native_story_save"));
        }
        object ReadStoryProfile()
        { return Json.Map("save", storySaveName, "key", storyProfileKey, "initializedThisLoad", newStoryProfile, "scope", "story_save_slot", "savePolicy", "native_save_only"); }
    }
}
