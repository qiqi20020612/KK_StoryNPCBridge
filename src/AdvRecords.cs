using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        sealed class AdvRecord
        {
            internal ADV.TextScenario Scenario;
            internal TextController Controller;
            internal TalkScene Talk;
            internal string Text, Speaker, RawSpeaker, ConversationId, Generation, World, Kind;
            internal object SpeakerActor;
            internal object[] Participants;
            internal int Line;
            internal float Created;
        }
        ADV.Commands.Base.Text recordingCommand;
        readonly List<AdvRecord> pendingAdvRecords = new List<AdvRecord>();
        readonly Dictionary<ADV.TextScenario, SaveData.Heroine> advHeroineBindings = new Dictionary<ADV.TextScenario, SaveData.Heroine>();
        readonly Dictionary<ADV.TextScenario, string> advEpisodeIds = new Dictionary<ADV.TextScenario, string>();
        TalkScene recordedTalk;
        string recordedTalkId, recordingGeneration;
        object lastAdvConversation;
        void ResetAdvRecords()
        {
            pendingAdvRecords.Clear(); advHeroineBindings.Clear(); advEpisodeIds.Clear(); recordedTalk = null; recordedTalkId = null;
            recordingGeneration = ownedGeneration; lastAdvConversation = null;
        }

        void InstallAdvRecordHooks()
        {
            PatchNative(typeof(ADV.Commands.Base.Text), "Do", Type.EmptyTypes, "BeforeAdvText", null, "AfterAdvText");
            PatchNative(typeof(ADV.TextScenario), "Initialize", Type.EmptyTypes, "BeforeAdvEpisode", null, null);
            PatchNative(typeof(SaveData.Heroine), "SetADVParam", new Type[] { typeof(ADV.TextScenario) }, null, "AdvHeroineBound", null);
        }
        static void BeforeAdvText(ADV.Commands.Base.Text __instance, ref ADV.Commands.Base.Text __state)
        { if (Current != null) { __state = Current.recordingCommand; Current.recordingCommand = __instance; } }
        static void AfterAdvText(ADV.Commands.Base.Text __state)
        { if (Current != null) Current.recordingCommand = __state; }
        static void BeforeAdvEpisode(ADV.TextScenario __instance)
        {
            if (Current == null) return;
            Current.advHeroineBindings.Remove(__instance);
            Current.advEpisodeIds[__instance] = Guid.NewGuid().ToString("N");
        }
        static void AdvHeroineBound(SaveData.Heroine __instance, ADV.TextScenario __0)
        { if (Current != null && __0 != null) Current.advHeroineBindings[__0] = __instance; }
        Dictionary<string, object> AdvActorReference(SaveData.CharaData data)
        {
            bool bridge = ReferenceEquals(data, ownedHeroine), player = data is SaveData.Player;
            int index = ownedSave == null ? -1 : ownedSave.heroineList.IndexOf(data as SaveData.Heroine);
            return Json.Map("id", bridge ? ActorId : player ? "player:main" : "native:" + world + ":" + (index >= 0 ? index.ToString() : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(data).ToString()),
                "role", bridge ? "bridge" : player ? "player" : "npc", "name", data.Name, "world", world,
                "generation", bridge ? ownedGeneration : null, "schoolClass", data.schoolClass, "schoolClassIndex", data.schoolClassIndex);
        }
        static void AddAdvParticipant(List<SaveData.CharaData> people, SaveData.CharaData person)
        { if (person != null && !people.Contains(person)) people.Add(person); }
        object ResolveAdvSpeaker(ADV.TextScenario scenario, List<SaveData.CharaData> people, string raw, string name, string text)
        {
            SaveData.CharaData person = null; string source = "unresolved";
            if (AdvSequenceBusy && advSequence.Scenario == scenario && advSequence.Segment > 0 && displayPages.Contains(text))
            {
                string role = advSequence.Segments[advSequence.Segment - 1].Speaker;
                if (role == "narrator") return Json.Map("id", null, "role", "narrator", "name", "", "identitySource", "bridge_sequence", "candidates", new object[0]);
                person = role == "player" ? (SaveData.CharaData)ownedSave.player : ownedHeroine; source = "bridge_sequence";
            }
            else if (advLineActive && displayPages.Contains(text) && (queuedAdvSpeaker == "player" || queuedAdvSpeaker == "npc"))
            { person = queuedAdvSpeaker == "player" ? (SaveData.CharaData)ownedSave.player : ownedHeroine; source = "bridge_transfer"; }
            else if (raw.IndexOf("[P", StringComparison.Ordinal) >= 0) { person = scenario.player; source = "native_parameter"; }
            else if (raw.IndexOf("[H", StringComparison.Ordinal) >= 0)
            {
                SaveData.Heroine bound;
                if (advHeroineBindings.TryGetValue(scenario, out bound)) person = bound;
                source = "native_parameter";
            }
            var matches = new List<object>();
            var candidates = new List<SaveData.CharaData>(people); AddAdvParticipant(candidates, scenario.player);
            if (person == null && !string.IsNullOrEmpty(name))
                foreach (var candidate in candidates)
                    if (name == candidate.Name || name == candidate.nickname)
                    { person = candidate; matches.Add(AdvActorReference(candidate)); }
            if (matches.Count == 1) source = "participant_name";
            if (matches.Count > 1) person = null;
            if (person != null)
            {
                AddAdvParticipant(people, person);
                var reference = AdvActorReference(person); reference["identitySource"] = source; return reference;
            }
            return Json.Map("id", null, "role", string.IsNullOrEmpty(name) ? "narrator" : "unknown", "name", name,
                "identitySource", string.IsNullOrEmpty(name) ? "narration" : "unresolved", "candidates", matches.ToArray());
        }
        void QueueAdvRecord(TextController controller, string speaker, string text)
        {
            if (recordingCommand == null || string.IsNullOrEmpty(text) || ownedHeroine == null) return;
            var scenario = AccessTools.Property(typeof(ADV.CommandBase), "scenario").GetValue(recordingCommand, null) as ADV.TextScenario;
            if (scenario == null || scenario.textController != controller) return;
            var talk = UnityEngine.Object.FindObjectOfType<TalkScene>();
            var people = new List<SaveData.CharaData>();
            if (scenario.heroineList != null) foreach (var heroine in scenario.heroineList) AddAdvParticipant(people, heroine);
            if (scenario.commandController != null)
                foreach (var character in scenario.commandController.Characters.Values) if (character != null && character.data is SaveData.Heroine) AddAdvParticipant(people, character.data);
            if (talk != null)
            {
                if (!ReferenceEquals(talk.targetHeroine, ownedHeroine)) return;
                AddAdvParticipant(people, talk.targetHeroine); AddAdvParticipant(people, scenario.player);
            }
            if (!people.Contains(ownedHeroine)) return;
            string raw = recordingCommand.args.Length > 0 ? recordingCommand.args[0] ?? "" : "";
            object speakerActor = ResolveAdvSpeaker(scenario, people, raw, speaker, text);
            var participants = new List<object>(); foreach (var person in people) participants.Add(AdvActorReference(person));
            string id;
            if (talk != null)
            {
                if (recordedTalk != talk || recordingGeneration != ownedGeneration)
                { recordedTalk = talk; recordedTalkId = Guid.NewGuid().ToString("N"); }
                id = recordedTalkId;
            }
            else if (!advEpisodeIds.TryGetValue(scenario, out id)) advEpisodeIds[scenario] = id = Guid.NewGuid().ToString("N");
            pendingAdvRecords.RemoveAll(delegate(AdvRecord line) { return line.Controller == controller; });
            pendingAdvRecords.Add(new AdvRecord { Scenario = scenario, Controller = controller, Talk = talk, Text = text, Speaker = speaker, RawSpeaker = raw,
                ConversationId = id, Generation = ownedGeneration, World = world, Kind = talk != null ? "native_talk" : "native_adv_event",
                Participants = participants.ToArray(), SpeakerActor = speakerActor, Line = scenario.CurrentLine, Created = Time.realtimeSinceStartup });
            recordingGeneration = ownedGeneration;
        }
        void TickAdvRecords()
        {
            for (int i = pendingAdvRecords.Count - 1; i >= 0; i--)
            {
                AdvRecord line = pendingAdvRecords[i];
                if (line.Generation != ownedGeneration || line.World != world || line.Scenario == null || line.Controller == null || line.Scenario.CurrentLine != line.Line || Time.realtimeSinceStartup - line.Created > 15)
                { pendingAdvRecords.RemoveAt(i); continue; }
                if (line.Scenario.MessageWindow == null || !line.Scenario.MessageWindow.gameObject.activeInHierarchy || !line.Controller.messageVisible || line.Controller.MessageWindow == null || !line.Controller.MessageWindow.gameObject.activeInHierarchy) continue;
                Canvas canvas = line.Controller.MessageWindow.GetComponentInParent<Canvas>();
                if (canvas == null || !canvas.isActiveAndEnabled || !canvas.rootCanvas.isActiveAndEnabled) continue;
                string managed = DialogueActive && line.Talk != null && ReferenceEquals(line.Talk.targetHeroine, ownedHeroine) ? dialogueId : null;
                lastAdvConversation = Json.Map("id", line.ConversationId, "kind", line.Kind, "participants", line.Participants, "dialogueConversationId", managed);
                AddEvent("game_dialogue", Json.Map("speaker", line.Speaker, "speakerActor", line.SpeakerActor, "text", line.Text,
                    "conversationId", line.ConversationId, "dialogueConversationId", managed, "conversation", lastAdvConversation,
                    "source", "native_adv_text", "nativeLine", line.Line, "gameTime", ReadGameTime(), "bundle", line.Scenario.LoadBundleName, "asset", line.Scenario.LoadAssetName));
                TrackAdvPage(line.Text); pendingAdvRecords.RemoveAt(i);
            }
            var stale = new List<ADV.TextScenario>();
            foreach (var scenario in advEpisodeIds.Keys) if (scenario == null) stale.Add(scenario);
            foreach (var scenario in stale) { advEpisodeIds.Remove(scenario); advHeroineBindings.Remove(scenario); }
        }
    }
}
