using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        const string AdvPageMarker = "KK_StoryNPCBridge/page/";
        sealed class AdvTextGroup
        {
            internal string Id, Source, Expanded, Signature, Generation, World;
            internal TalkScene Talk;
            internal ADV.TextScenario Scenario;
            internal List<AdvTextToken> Tokens;
            internal List<AdvTextPage> Pages;
            internal AdvTextPage Current;
            internal ADV.Commands.Base.Text Command;
            internal int Index, Cursor;
        }
        readonly Dictionary<string, AdvTextGroup> advTextGroups = new Dictionary<string, AdvTextGroup>();
        int presentedPageIndex, advPageCount, advLayoutRevision, nonBmpFallbacks;
        Dictionary<string, object> advLayoutInfo;
        void BeginAdvPages()
        {
            advTextGroups.Clear(); presentedPageIndex = 0; advPageCount = 0;
            advLayoutRevision = 0; nonBmpFallbacks = 0; advLayoutInfo = null;
        }
        void AddAdvText(List<ADV.Program.Transfer> list, string speaker, string text)
        {
            var group = new AdvTextGroup { Id = AdvPageMarker + Guid.NewGuid().ToString("N"), Source = text, Generation = ownedGeneration, World = world, Talk = currentTalk };
            // The menu can unload its ADV scenario. Measure when the native Text command has its real host.
            advPageCount++; advTextGroups.Add(group.Id, group);
            // Native Text.Data consumes name/text and ignores trailing arguments.
            list.Add(ADV.Program.Transfer.Text(speaker, text, group.Id));
        }
        AdvTextGroup BridgeAdvText(ADV.Commands.Base.Text command)
        {
            AdvTextGroup group;
            return command.args.Length > 2 && advTextGroups.TryGetValue(command.args[2], out group) ? group : null;
        }
        static bool FormatBridgeAdvPage(HyphenationJpn __instance, UnityEngine.UI.Text __0, ref string __result)
        {
            var p = Current; if (p == null || p.recordingCommand == null) return true;
            var group = p.BridgeAdvText(p.recordingCommand);
            if (group == null || group.Current == null || group.Scenario == null || group.Scenario.textController == null) return true;
            var controller = group.Scenario.textController;
            if (!ReferenceEquals(AccessTools.Field(typeof(TextController), "hypJpn").GetValue(controller), __instance) ||
                !ReferenceEquals(AccessTools.Field(typeof(TextController), "messageWindow").GetValue(controller), __0)) return true;
            __result = group.Current.Display; return false;
        }
        void PrepareBridgeAdvText(ADV.Commands.Base.Text command)
        {
            var group = BridgeAdvText(command);
            if (group == null)
            { if (command.args.Length > 2 && command.args[2].StartsWith(AdvPageMarker, StringComparison.Ordinal)) command.args[1] = ""; return; }
            var scenario = AccessTools.Property(typeof(ADV.CommandBase), "scenario").GetValue(command, null) as ADV.TextScenario;
            try
            {
                if (group.Generation != ownedGeneration || group.World != world || group.Talk != currentTalk || !OwnedTalkActive || group.Scenario != null && group.Scenario != scenario)
                    throw new BridgeException("dialogue_context_changed", "The native page no longer belongs to this conversation");
                group.Scenario = scenario;
                bool first = group.Pages == null;
                if (first)
                {
                    group.Expanded = scenario.ReplaceText(group.Source, true); group.Tokens = AdvTokens(group.Expanded);
                    for (int i = 0; i < group.Expanded.Length - 1; i++)
                        if (char.IsHighSurrogate(group.Expanded[i]) && char.IsLowSurrogate(group.Expanded[i + 1])) { nonBmpFallbacks++; i++; }
                }
                using (var layout = NativeAdvPageLayout(scenario))
                {
                    if (first || group.Signature != layout.Signature)
                    {
                        var pages = layout.Pages(group.Expanded, group.Tokens, group.Cursor);
                        advPageCount += pages.Count - (first ? 1 : group.Pages.Count - group.Index);
                        group.Pages = pages; group.Index = 0; group.Signature = layout.Signature; advLayoutRevision++;
                        if (AdvSequenceBusy) advSequence.PageCount = advPageCount;
                    }
                    advLayoutInfo = layout.Info;
                }
                group.Current = group.Pages[group.Index]; group.Command = command;
                command.args[1] = group.Current.Argument;
            }
            catch (Exception e)
            {
                command.args[1] = ""; group.Current = null;
                PaginationFailed(e); StartCoroutine(CloseFailedAdvPage(scenario, currentTalk, ownedGeneration));
            }
        }
        void CompleteBridgeAdvText(ADV.Commands.Base.Text command, Exception exception)
        {
            var group = BridgeAdvText(command);
            if (group == null || group.Command != command || group.Current == null) return;
            if (exception != null) { group.Current = null; PaginationFailed(exception); return; }
            var page = group.Current; group.Current = null; group.Cursor = page.End; group.Index++;
            if (group.Index < group.Pages.Count)
                group.Scenario.CommandAdd(false, group.Scenario.CurrentLine + 1, false, ADV.Command.Text, new string[] { command.args[0], "", group.Id });
            else advTextGroups.Remove(group.Id);
            presentedPageIndex++;
            var data = (Dictionary<string, object>)ReadAdvPagination();
            data["conversationId"] = DialogueActive ? dialogueId : null; data["turn"] = dialogueTurn;
            data["text"] = page.Original; data["displayText"] = page.Display;
            TrackAdvSequencePage(data); AddEvent("dialogue_page", data);
        }
        void PaginationFailed(Exception exception)
        {
            AddEvent("dialogue_pagination_failed", Json.Map("error", exception.GetBaseException().Message, "pagination", ReadAdvPagination()));
            if (AdvSequenceBusy) FailAdvSequence(exception);
            else if (DialogueActive) { advLineActive = false; EndAdvDialogue("pagination_failed"); }
        }
        IEnumerator CloseFailedAdvPage(ADV.TextScenario scenario, TalkScene talk, string generation)
        {
            yield return null;
            if (scenario != null && talk == currentTalk && generation == ownedGeneration && !SceneBusy && !NativeEventBusy)
                scenario.CommandAdd(true, scenario.CurrentLine + 1, false, ADV.Command.Close, new string[0]);
        }
        object ReadAdvPagination()
        {
            bool estimated = false; foreach (var group in advTextGroups.Values) if (group.Pages == null) estimated = true;
            return Json.Map("page", presentedPageIndex, "pageCount", advPageCount, "pageCountEstimated", estimated, "mode", "native_layout",
                "layoutRevision", advLayoutRevision, "layout", advLayoutInfo, "nonBmpFallbacks", nonBmpFallbacks);
        }
    }
}
