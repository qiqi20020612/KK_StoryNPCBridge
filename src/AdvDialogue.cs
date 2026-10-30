using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        const string DialogueWaitMarker = "982451653";
        string dialogueId, dialoguePhase = "closed", dialogueError, modelRequestId, requestUserText, requestKind;
        string queuedAdvText, queuedAdvSpeaker, queuedAdvAfter, heldAfter;
        string receivedModelText;
        string dialogueInitiator = "player";
        bool npcInvitationPending;
        bool modelReceived, receivedRequestInput, advLineActive, advSawHold, releaseAdvHold, endingSent, dialogueHostSeen;
        bool focusDialogueInput;
        float dialogueOpenedAt, modelRequestedAt, modelTimeoutSeconds, defaultModelTimeoutSeconds, nextEndingAdvance, advLineStartedAt, lastHoldProcess;
        Texture2D dialogueOverlayTexture;
        GUIStyle dialogueOverlayStyle;
        int dialogueTurn;
        ADV.Commands.Base.Wait managedWait;
        readonly List<object> dialogueHistory = new List<object>();
        readonly List<CanvasGroup> dialogueMenuGroups = new List<CanvasGroup>();
        readonly List<float> dialogueMenuAlphas = new List<float>();
        readonly List<bool> dialogueMenuInteractions = new List<bool>(), dialogueMenuRaycasts = new List<bool>();
        string dialogueInput = "", manualModelText = "";
        bool DialogueActive { get { return dialoguePhase != "closed"; } }
        bool WaitingModel { get { return modelRequestId != null && !modelReceived && dialogueError == null && dialoguePhase != "ending"; } }

        void InstallDialogueHooks()
        {
            modelTimeoutSeconds = Mathf.Clamp(Config.Bind("Dialogue", "ModelReplyTimeoutSeconds", 180f, "Seconds to wait for an external model reply before showing retry").Value, 5, 600);
            defaultModelTimeoutSeconds = modelTimeoutSeconds;
            PatchNative(typeof(ADV.Commands.Base.Wait), "Do", Type.EmptyTypes, null, "AdvHoldStarted", null);
            PatchNative(typeof(ADV.Commands.Base.Wait), "Process", Type.EmptyTypes, "ProcessAdvHold", null, null);
        }
        static void AdvHoldStarted(ADV.Commands.Base.Wait __instance)
        {
            Plugin p = Current;
            if (p == null || !p.DialogueActive || !p.advLineActive || __instance.args.Length == 0 || __instance.args[0] != DialogueWaitMarker) return;
            if (p.currentTalk == null || !ReferenceEquals(p.currentTalk.targetHeroine, p.ownedHeroine)) return;
            p.managedWait = __instance; p.advSawHold = true; p.lastHoldProcess = Time.realtimeSinceStartup;
            if (p.dialoguePhase != "ending") p.SetDialoguePhase(p.dialogueError != null ? "model_error" : p.heldAfter);
        }
        static bool ProcessAdvHold(ADV.Commands.Base.Wait __instance, ref bool __result)
        {
            if (Current == null || !ReferenceEquals(Current.managedWait, __instance)) return true;
            Current.lastHoldProcess = Time.realtimeSinceStartup;
            __result = Current.releaseAdvHold; return false;
        }
        void SetDialoguePhase(string phase)
        {
            if (dialoguePhase == phase) return;
            dialoguePhase = phase; focusDialogueInput = phase == "awaiting_input";
            SyncAdvInputLock();
            SyncNativeCommandMenus();
            AddEvent("dialogue_state", Json.Map("conversationId", dialogueId, "turn", dialogueTurn, "state", phase));
        }
        object ReadDialogue()
        {
            return Json.Map("conversationId", dialogueId, "state", dialoguePhase, "channel", "native_adv", "turn", dialogueTurn,
                "phase", dialoguePhase == "waiting_model" ? "waiting_reply" : dialoguePhase == "model_error" ? "reply_error" : dialoguePhase,
                "initiator", dialogueInitiator, "invitationPending", npcInvitationPending,
                "nativeNpcInitiated", currentTalk != null && ReferenceEquals(currentTalk.targetHeroine, ownedHeroine) && currentTalk.necessaryInfo != null && currentTalk.necessaryInfo.isAttack,
                "inputEnabled", dialoguePhase == "awaiting_input", "waitingReply", WaitingModel, "waitingModel", WaitingModel, "mouseBlocked", AdvMouseBlocked, "advHeld", managedWait != null && !releaseAdvHold,
                "endButtonRect", AdvMouseBlocked ? new float[] { AdvCancelRect.x, AdvCancelRect.y, AdvCancelRect.width, AdvCancelRect.height, Screen.width, Screen.height } : null,
                "blockedAdvRequests", blockedAdvRequests,
                "pagination", ReadAdvPagination(),
                "requestId", modelRequestId, "requestKind", requestKind, "userText", requestUserText,
                "nativeCommand", requestNativeCommand, "nativeMenusAvailable", nativeCommandMenusShown,
                "responseReceived", modelReceived, "modelReplyReceived", modelReceived, "error", dialogueError, "errorCode", dialogueError == "model_timeout" ? "reply_timeout" : dialogueError,
                "waitSeconds", modelRequestId == null ? 0 : (Recovering ? recoveryStartedAt : Time.realtimeSinceStartup) - modelRequestedAt,
                "timeoutSeconds", modelTimeoutSeconds, "history", dialogueHistory.ToArray());
        }
        static string OptionalText(Dictionary<string, object> d, string key, string fallback)
        { object value; if (!d.TryGetValue(key, out value)) return fallback; if (!(value is string)) throw new BridgeException("invalid_argument", key + " must be text"); return (string)value; }
        static bool OptionalBool(Dictionary<string, object> d, string key, bool fallback)
        { object value; return d.TryGetValue(key, out value) ? Boolean(value, key) : fallback; }
        static void ValidateDialogueText(string text)
        {
            if (text == null || text.Trim().Length == 0 || text.Length > 4000) throw new BridgeException("invalid_argument", "Dialogue text must contain 1-4000 characters");
            for (int i = 0; i < text.Length; i++)
                if (char.IsSurrogate(text[i]))
                {
                    if (!char.IsHighSurrogate(text[i]) || i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                        throw new BridgeException("invalid_argument", "Dialogue text contains an unpaired UTF-16 surrogate");
                    i++;
                }
        }
        void RequireConversation(Dictionary<string, object> d)
        {
            if (!DialogueActive || Text(d, "conversationId") != dialogueId) throw new BridgeException("stale_conversation", "Refresh the current conversation ID");
        }
        object OpenAdvDialogue(Dictionary<string, object> d)
        {
            RequireAfterInteraction(d);
            if (DialogueActive) throw new BridgeException("dialogue_active", "End the current conversation first");
            string text = OptionalText(d, "text", "");
            if (text.Length > 0) ValidateDialogueText(text);
            bool requestInput = OptionalBool(d, "requestInput", true); object timeout;
            string initiator = OptionalText(d, "initiator", "player");
            if (initiator != "player" && initiator != "npc") throw new BridgeException("invalid_argument", "initiator must be player or npc");
            float replyTimeout = d.TryGetValue("timeoutSeconds", out timeout) ? (float)Number(timeout, 5, 600, "timeoutSeconds") : defaultModelTimeoutSeconds;
            bool reuse = currentTalk != null && ReferenceEquals(currentTalk.targetHeroine, ownedHeroine);
            if (reuse)
            {
                if (!NativeTalkReady(currentTalk)) throw new BridgeException("busy", "Wait for the native conversation options");
                initiator = currentTalk.necessaryInfo != null && currentTalk.necessaryInfo.isAttack ? "npc" : "player";
            }
            else if (initiator == "npc") StartNpcInvitation();
            else ExecuteNativeCommand("native_talk", Json.Map());
            InitializeDialogueSession(initiator, replyTimeout);
            npcInvitationPending = !reuse && initiator == "npc";
            queuedAdvText = text; queuedAdvSpeaker = "npc";
            queuedAdvAfter = requestInput ? "awaiting_input" : "ready";
            SetDialoguePhase(npcInvitationPending ? "inviting" : "opening");
            if (text.Length == 0)
            { queuedAdvAfter = "waiting_model"; if (!npcInvitationPending) RequestExternalReply("greeting", null); }
            AddEvent("dialogue_opened", Json.Map("conversationId", dialogueId, "channel", "native_adv", "initiator", dialogueInitiator));
            if (npcInvitationPending) AddEvent("npc_invitation_started", Json.Map("conversationId", dialogueId, "nativeAction", 8));
            return ReadDialogue();
        }
        void InitializeDialogueSession(string initiator, float replyTimeout)
        {
            dialogueInitiator = initiator; modelTimeoutSeconds = replyTimeout;
            dialogueId = Guid.NewGuid().ToString("N"); dialogueTurn = 0; dialogueError = null;
            dialogueInput = ""; dialogueHistory.Clear(); modelRequestId = null; requestKind = null; requestUserText = null; modelReceived = false;
            requestNativeCommand = null; nativeCommandMenusShown = false;
            advLineActive = false; advSawHold = false; releaseAdvHold = false; managedWait = null;
            endingSent = false; dialogueHostSeen = false; dialogueOpenedAt = Time.realtimeSinceStartup;
        }
        void AddDialogueMessage(string role, string text)
        {
            dialogueHistory.Add(Json.Map("role", role, "text", text, "turn", dialogueTurn, "utc", DateTime.UtcNow.ToString("o")));
            if (dialogueHistory.Count > 20) dialogueHistory.RemoveAt(0);
        }
        void RequestExternalReply(string kind, string userText)
        {
            if (kind != "native_command") requestNativeCommand = null;
            modelRequestId = Guid.NewGuid().ToString("N"); modelRequestedAt = Time.realtimeSinceStartup;
            requestUserText = userText; requestKind = kind; modelReceived = false; receivedModelText = null; dialogueError = null;
            SyncAdvInputLock();
            AddEvent("dialogue_request", Json.Map("conversationId", dialogueId, "requestId", modelRequestId, "turn", dialogueTurn,
                "kind", kind, "userText", userText, "nativeCommand", requestNativeCommand, "initiator", dialogueInitiator));
        }
        object SubmitAdvInput(Dictionary<string, object> d)
        {
            RequireConversation(d);
            object turn;
            if (!d.TryGetValue("turn", out turn) || Number(turn, 0, int.MaxValue, "turn") != dialogueTurn) throw new BridgeException("stale_turn", "The input turn has changed");
            if (dialoguePhase != "awaiting_input") throw new BridgeException("input_not_requested", "Wait until user input is requested");
            string text = Text(d, "text"); ValidateDialogueText(text);
            dialogueTurn++; dialogueInput = ""; AddDialogueMessage("user", text);
            AddEvent("user_dialogue", Json.Map("conversationId", dialogueId, "turn", dialogueTurn, "text", text));
            RequestExternalReply("reply", text);
            QueueAdvLine("player", text, "waiting_model"); SetDialoguePhase("user_speaking");
            return ReadDialogue();
        }
        object ReceiveExternalReply(Dictionary<string, object> d, bool failed)
        {
            RequireConversation(d);
            if (modelRequestId == null || Text(d, "requestId") != modelRequestId) throw new BridgeException("stale_request", "This model request is no longer active");
            if (!WaitingModel) throw new BridgeException("not_waiting_model", "The reply was already received or the request ended");
            if (failed)
            {
                dialogueError = Text(d, "error");
                if (dialogueError.Length > 1000) dialogueError = dialogueError.Substring(0, 1000);
                if (managedWait != null) SetDialoguePhase("model_error");
                AddEvent("dialogue_response_error", Json.Map("conversationId", dialogueId, "requestId", modelRequestId, "error", dialogueError));
            }
            else
            {
                string text = Text(d, "text"); ValidateDialogueText(text);
                receivedModelText = text; receivedRequestInput = OptionalBool(d, "requestInput", requestKind != "native_command"); modelReceived = true;
                AddEvent("dialogue_response_received", Json.Map("conversationId", dialogueId, "requestId", modelRequestId, "turn", dialogueTurn));
            }
            SyncAdvInputLock(); return ReadDialogue();
        }
        object DialogueControl(string op, Dictionary<string, object> d)
        {
            RequireConversation(d);
            if (op == "dialogue_close") { EndAdvDialogue("user_closed"); return ReadDialogue(); }
            if (op == "dialogue_retry")
            {
                if (dialogueError == null) throw new BridgeException("wrong_context", "No failed model request to retry");
                RequestExternalReply(requestKind, requestUserText); heldAfter = "waiting_model";
                SetDialoguePhase(managedWait != null ? "waiting_model" : "user_speaking"); return ReadDialogue();
            }
            if (dialoguePhase != "ready") throw new BridgeException("wrong_context", "Wait until the current ADV line finishes");
            if (op == "dialogue_request_input") { heldAfter = "awaiting_input"; SetDialoguePhase("awaiting_input"); }
            else { dialogueTurn++; heldAfter = "waiting_model"; RequestExternalReply("continue", null); SetDialoguePhase("waiting_model"); }
            return ReadDialogue();
        }
        void QueueAdvLine(string speaker, string text, string after)
        {
            queuedAdvSpeaker = speaker; queuedAdvText = text; queuedAdvAfter = after;
            releaseAdvHold = true;
        }
        void StartManagedAdvLine()
        {
            string text = queuedAdvText, speaker = queuedAdvSpeaker;
            heldAfter = queuedAdvAfter; queuedAdvText = null;
            releaseAdvHold = false; advSawHold = false; managedWait = null; advLineActive = true; advLineStartedAt = Time.realtimeSinceStartup;
            SetDialoguePhase(speaker == "npc" && text.Length > 0 ? "npc_speaking" : speaker == "player" ? "user_speaking" : "waiting_model");
            var list = ADV.Program.Transfer.NewList(false, false);
            list.Add(ADV.Program.Transfer.Create(true, ADV.Command.CharaChange, "-2", "true"));
            if (text.Length > 0)
            {
                foreach (string page in BeginAdvPages(text)) list.Add(ADV.Program.Transfer.Text(speaker == "player" ? Manager.Game.Instance.saveData.player.Name : "[H名]", page));
                if (speaker == "npc") { AddDialogueMessage("assistant", text); AddEvent("npc_dialogue", Json.Map("conversationId", dialogueId, "turn", dialogueTurn, "speaker", ownedName, "text", text, "channel", "native_adv")); }
            }
            else { list.Add(ADV.Program.Transfer.Create(true, ADV.Command.TextClear)); list.Add(ADV.Program.Transfer.Create(true, ADV.Command.WindowActive, "true", "1")); }
            list.Add(ADV.Program.Transfer.Create(false, ADV.Command.Wait, DialogueWaitMarker));
            list.Add(ADV.Program.Transfer.Close());
            AccessTools.Field(typeof(TalkScene), "isUpdateCamera").SetValue(currentTalk, false);
            AccessTools.Method(typeof(TalkScene), "StartADV", new Type[] { list.GetType() }, null).Invoke(currentTalk, new object[] { list });
            StartCoroutine(WaitManagedAdvLine(dialogueId));
        }
        IEnumerator WaitManagedAdvLine(string id)
        {
            while (id == dialogueId && !releaseAdvHold) yield return null;
            if (id != dialogueId) yield break;
            yield return null; yield return ADV.Program.Wait("Talk");
            if (id != dialogueId) yield break;
            advLineActive = false; managedWait = null;
        }
        void HideNativeDialogueMenus()
        {
            if (dialogueMenuGroups.Count > 0) return;
            foreach (string field in new string[] { "canvas", "canvasFemale" })
            {
                var canvas = AccessTools.Field(typeof(TalkScene), field).GetValue(currentTalk) as Canvas;
                if (canvas == null) continue;
                CanvasGroup group = canvas.GetComponent<CanvasGroup>(); if (group == null) group = canvas.gameObject.AddComponent<CanvasGroup>();
                dialogueMenuGroups.Add(group); dialogueMenuAlphas.Add(group.alpha); dialogueMenuInteractions.Add(group.interactable); dialogueMenuRaycasts.Add(group.blocksRaycasts);
                group.alpha = 0; group.interactable = false; group.blocksRaycasts = false;
            }
        }
        void RestoreNativeDialogueMenus()
        {
            for (int i = 0; i < dialogueMenuGroups.Count; i++) if (dialogueMenuGroups[i] != null)
            { dialogueMenuGroups[i].alpha = dialogueMenuAlphas[i]; dialogueMenuGroups[i].interactable = dialogueMenuInteractions[i]; dialogueMenuGroups[i].blocksRaycasts = dialogueMenuRaycasts[i]; }
            dialogueMenuGroups.Clear(); dialogueMenuAlphas.Clear(); dialogueMenuInteractions.Clear(); dialogueMenuRaycasts.Clear();
        }
        void TickAdvDialogue()
        {
            if (!DialogueActive) return;
            bool host = currentTalk != null && ReferenceEquals(currentTalk.targetHeroine, ownedHeroine);
            if (host) dialogueHostSeen = true;
            if (action == null || dialogueHostSeen && !host && !nativeTalkPending) { ResetAdvDialogue("world_or_conversation_closed"); return; }
            if (Recovering && dialoguePhase != "ending") return;
            if (!dialogueHostSeen && Time.realtimeSinceStartup - dialogueOpenedAt > (npcInvitationPending ? 180 : 45)) { EndAdvDialogue("opening_timeout"); }
            if (npcInvitationPending && !host && !nativeTalkPending && !SceneBusy && dialoguePhase != "ending" &&
                Time.realtimeSinceStartup - dialogueOpenedAt > 1 && ownedNpc != null && ownedNpc.AI.actionNo != 8 && ownedNpc.AI.actionNo != 28)
                EndAdvDialogue("native_invitation_interrupted");
            if (WaitingModel && Time.realtimeSinceStartup - modelRequestedAt >= modelTimeoutSeconds)
            {
                dialogueError = "model_timeout"; if (managedWait != null) SetDialoguePhase("model_error");
                AddEvent("dialogue_request_timeout", Json.Map("conversationId", dialogueId, "requestId", modelRequestId));
            }
            if (dialoguePhase == "ending")
            {
                if (!host && !nativeTalkPending) { ResetAdvDialogue("ended"); return; }
                if (SceneBusy || !host) return;
                Tutorial tutorial = UnityEngine.Object.FindObjectOfType<Tutorial>(); if (tutorial != null) tutorial.EndScene(true);
                if (!advLineActive && NativeTalkReady(currentTalk) && !endingSent)
                {
                    RestoreNativeDialogueMenus();
                    var button = AccessTools.Field(typeof(TalkScene), "buttonEnd").GetValue(currentTalk) as UnityEngine.UI.Button;
                    endingSent = true; button.onClick.Invoke();
                }
                if (Time.realtimeSinceStartup >= nextEndingAdvance)
                {
                    foreach (ADV.TextScenario scenario in UnityEngine.Object.FindObjectsOfType<ADV.TextScenario>())
                        if (!scenario.isChoice && scenario.MessageWindow != null && scenario.MessageWindow.gameObject.activeInHierarchy)
                        { scenario.textController.ForceCompleteDisplayText(); AccessTools.Method(typeof(ADV.TextScenario), "RequestNextLine", Type.EmptyTypes, null).Invoke(scenario, new object[0]); }
                    nextEndingAdvance = Time.realtimeSinceStartup + 0.3f;
                }
                return;
            }
            if (!host || SceneBusy) return;
            if (dialoguePhase == "native_reaction") return;
            if (advLineActive && !releaseAdvHold && !ADV.Program.isADVProcessing && NativeTalkReady(currentTalk) &&
                Time.realtimeSinceStartup - (advSawHold ? lastHoldProcess : advLineStartedAt) > 1.5f)
            { EndAdvDialogue("native_window_closed"); return; }
            if (!advLineActive && NativeTalkReady(currentTalk))
            {
                if (npcInvitationPending)
                {
                    if (currentTalk.necessaryInfo == null || !currentTalk.necessaryInfo.isAttack)
                    { EndAdvDialogue("native_initiator_mismatch"); return; }
                    npcInvitationPending = false;
                    AddEvent("npc_invitation_accepted", Json.Map("conversationId", dialogueId, "nativeNpcInitiated", true));
                    if (queuedAdvText != null && queuedAdvText.Length == 0) RequestExternalReply("greeting", null);
                }
                HideNativeDialogueMenus();
                if (queuedAdvText != null) StartManagedAdvLine();
            }
            if (managedWait != null && heldAfter == "waiting_model" && modelReceived && queuedAdvText == null)
            {
                string reply = receivedModelText; bool input = receivedRequestInput;
                modelRequestId = null; modelReceived = false; receivedModelText = null; dialogueError = null;
                QueueAdvLine("npc", reply, input ? "awaiting_input" : "ready");
            }
            SyncNativeCommandMenus();
        }
        void CancelDisplayedDialogue()
        {
            if (!DialogueActive && OwnedTalkActive)
            {
                InitializeDialogueSession("player", defaultModelTimeoutSeconds);
                dialogueHostSeen = true; SetDialoguePhase("ready");
            }
            EndAdvDialogue("user_closed");
        }
        void EndAdvDialogue(string reason)
        {
            if (!DialogueActive || dialoguePhase == "ending") return;
            AddEvent("dialogue_ending", Json.Map("conversationId", dialogueId, "reason", reason));
            if (modelRequestId != null) AddEvent("dialogue_request_cancelled", Json.Map("conversationId", dialogueId, "requestId", modelRequestId, "reason", reason));
            modelRequestId = null; modelReceived = false; receivedModelText = null; queuedAdvText = null;
            releaseAdvHold = true; dialogueError = null; SetDialoguePhase("ending");
            if (npcInvitationPending)
            {
                npcInvitationPending = false;
                AddEvent("npc_invitation_cancelled", Json.Map("conversationId", dialogueId, "reason", reason));
                if (!nativeTalkPending && !SceneBusy && Mode == "roaming" && ownedNpc != null && ownedNpc.AI != null && (ownedNpc.AI.actionNo == 8 || ownedNpc.AI.actionNo == 28)) SetNativeIdle();
                else stopOnRoam = true;
            }
        }
        void ResetAdvDialogue(string reason)
        {
            RestoreNativeMenuRaycasts();
            if (managedWait != null) AccessTools.Field(typeof(ADV.Commands.Base.Wait), "time").SetValue(managedWait, 0f);
            RestoreNativeDialogueMenus();
            if (npcInvitationPending) AddEvent("npc_invitation_cancelled", Json.Map("conversationId", dialogueId, "reason", reason));
            npcInvitationPending = false;
            managedWait = null; advLineActive = false; releaseAdvHold = true; queuedAdvText = null; modelRequestId = null; modelReceived = false;
            nativeCommandMenusShown = false;
            presentedPages.Clear(); displayPages.Clear(); presentedPageIndex = 0; nonBmpFallbacks = 0;
            SetDialoguePhase("closed"); AddEvent("dialogue_closed", Json.Map("conversationId", dialogueId, "reason", reason));
            if (!panelVisible) RestoreCursor();
        }
        void DrawAdvDialogueOverlay()
        {
            if (!DialogueActive) return;
            if (Recovering) return;
            if (dialoguePhase == "native_reaction") return;
            if (npcInvitationPending && Mode == "roaming" && !nativeTalkPending) return;
            AcquireCursor();
            if (dialogueOverlayStyle == null)
            {
                dialogueOverlayTexture = new Texture2D(1, 1); dialogueOverlayTexture.SetPixel(0, 0, new Color(0.04f, 0.065f, 0.09f, 0.94f)); dialogueOverlayTexture.Apply();
                dialogueOverlayStyle = new GUIStyle(GUI.skin.box); dialogueOverlayStyle.normal.background = dialogueOverlayTexture;
            }
            if (AdvMouseBlocked)
            {
                Rect area = AdvWaitingArea;
                GUI.Box(new Rect(area.x, area.y, area.width, 34), "", dialogueOverlayStyle);
                GUI.Label(new Rect(area.x + 8, area.y + 6, area.width - 16, 24), T("等待外部响应… ") + (int)(Time.realtimeSinceStartup - modelRequestedAt) + T(" 秒"));
                return;
            }
            float width = Mathf.Min(1000, Screen.width - 48), height = dialoguePhase == "awaiting_input" ? 132 : 82;
            GUILayout.BeginArea(new Rect((Screen.width - width) / 2, Mathf.Max(36, Screen.height - 410), width, height), dialogueOverlayStyle);
            if (dialoguePhase == "awaiting_input")
            {
                GUILayout.Label(T("输入回复")); GUI.SetNextControlName("BridgeAdvInput");
                dialogueInput = GUILayout.TextField(dialogueInput, 4000);
                if (focusDialogueInput) { GUI.FocusControl("BridgeAdvInput"); focusDialogueInput = false; }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(T("发送")) && dialogueInput.Trim().Length > 0)
                    SubmitAdvInput(Json.Map("conversationId", dialogueId, "turn", dialogueTurn, "text", dialogueInput));
            }
            else
            {
                string label = dialoguePhase == "inviting" ? T("NPC 正在主动邀聊，请按本体提示回应…") : dialoguePhase == "opening" ? T("正在进入对话…") : dialoguePhase == "ending" ? T("正在结束对话…") : dialogueError == "model_timeout" ? T("等待响应超时") : dialogueError != null ? T("响应失败：") + dialogueError : WaitingModel ? T("等待外部响应… ") + (int)(Time.realtimeSinceStartup - modelRequestedAt) + T(" 秒") : dialoguePhase == "ready" ? T("等待下一步") : T("请阅读 ADV 台词并点击继续");
                GUILayout.Label(label); GUILayout.BeginHorizontal();
                if (dialogueError != null && GUILayout.Button(T("重试"))) DialogueControl("dialogue_retry", Json.Map("conversationId", dialogueId));
            }
            if (dialoguePhase != "ending" && GUILayout.Button(T("结束对话"))) EndAdvDialogue("user_closed");
            GUILayout.EndHorizontal(); GUILayout.EndArea();
        }
    }
}
