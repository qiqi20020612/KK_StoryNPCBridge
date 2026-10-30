using System;
using UnityEngine;
using UnityEngine.UI;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        GameObject advControlsCanvas, advWaitingNotice;
        Button advComposeButton;
        bool waitingIndicatorRequested;
        TalkScene waitingIndicatorTalk;
        string waitingIndicatorGeneration;
        bool ExternalWaitVisible { get { return waitingIndicatorRequested && ownedController != null && OwnedTalkActive && !Recovering && dialoguePhase != "ending"; } }
        bool CanComposeAdvText { get { return ownedController != null && OwnedTalkActive && !Recovering && !NativeEventBusy && !ExternalWaitVisible && !WaitingModel && !NativeReactionActive && NativeInputReady(currentTalk) && (!DialogueActive || dialoguePhase == "ready"); } }
        Rect AdvComposeRect { get { Rect area = AdvWaitingArea; return new Rect(area.xMax - 184, area.y - 40, 184, 34); } }
        object ReadAdvControls()
        { return Json.Map("playerInputAvailable", CanComposeAdvText, "waitingIndicatorRequested", waitingIndicatorRequested, "waitingIndicatorVisible", ExternalWaitVisible,
            "description", "The owner explicitly shows/hides the waiting indicator. This does not create a reply request or supply dialogue."); }
        void ClearAdvControls()
        { waitingIndicatorRequested = false; waitingIndicatorTalk = null; waitingIndicatorGeneration = null; }
        object SetWaitingIndicator(bool visible)
        {
            if (visible && NativeEventBusy) throw new BridgeException("native_event_busy", "Wait for the current native activity to finish");
            if (visible && !OwnedTalkActive && !nativeTalkPending)
                throw new BridgeException("wrong_context", "Open a native conversation with the bridge NPC before showing the waiting indicator");
            waitingIndicatorRequested = visible; waitingIndicatorTalk = OwnedTalkActive ? currentTalk : null; waitingIndicatorGeneration = visible ? ownedGeneration : null;
            SyncAdvInputLock(); TickAdvControls(); return ReadAdvControls();
        }
        void BeginPlayerAdvInput()
        {
            if (!CanComposeAdvText) return;
            if (DialogueActive) { heldAfter = "awaiting_input"; SetDialoguePhase("awaiting_input"); return; }
            StopNativeMenuActivity(currentTalk);
            InitializeDialogueSession(currentTalk.necessaryInfo != null && currentTalk.necessaryInfo.isAttack ? "npc" : "player", defaultModelTimeoutSeconds);
            dialogueHostSeen = true;
            AddEvent("dialogue_opened", Json.Map("conversationId", dialogueId, "channel", "native_adv", "initiator", dialogueInitiator, "source", "player_text_button"));
            QueueAdvLine("none", "", "awaiting_input"); SetDialoguePhase("opening");
        }
        Text AdvControlText(GameObject parent, string label)
        {
            var child = new GameObject("Label", typeof(RectTransform), typeof(Text)); child.transform.SetParent(parent.transform, false);
            var rect = child.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            if (panelFont == null) panelFont = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 16);
            var text = child.GetComponent<Text>(); text.font = panelFont; text.fontSize = 16; text.color = Color.white; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; text.text = T(label);
            return text;
        }
        static void PlaceAdvControl(GameObject obj, Rect rect)
        {
            var transform = obj.GetComponent<RectTransform>(); transform.anchorMin = transform.anchorMax = Vector2.zero; transform.pivot = Vector2.zero;
            transform.anchoredPosition = new Vector2(rect.x, Screen.height - rect.yMax); transform.sizeDelta = new Vector2(rect.width, rect.height);
        }
        void TickAdvControls()
        {
            if (waitingIndicatorRequested)
            {
                if (ownedController == null || waitingIndicatorGeneration != ownedGeneration || waitingIndicatorTalk != null && currentTalk != waitingIndicatorTalk || !OwnedTalkActive && !nativeTalkPending || dialoguePhase == "ending") ClearAdvControls();
                else if (waitingIndicatorTalk == null && OwnedTalkActive) waitingIndicatorTalk = currentTalk;
            }
            bool input = CanComposeAdvText, waiting = ExternalWaitVisible;
            if ((input || waiting) && advControlsCanvas == null)
            {
                advControlsCanvas = new GameObject("KK_StoryNPCBridge.AdvControls", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)); advControlsCanvas.transform.SetParent(transform, false);
                var canvas = advControlsCanvas.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.overrideSorting = true; canvas.sortingOrder = short.MaxValue - 1;
                var button = new GameObject("PlayerTextInput", typeof(RectTransform), typeof(Image), typeof(Button)); button.transform.SetParent(advControlsCanvas.transform, false);
                button.GetComponent<Image>().color = new Color(.08f, .17f, .24f, .98f); advComposeButton = button.GetComponent<Button>(); advComposeButton.targetGraphic = button.GetComponent<Image>();
                advComposeButton.onClick.AddListener(BeginPlayerAdvInput); AdvControlText(button, "输入文字对话");
                advWaitingNotice = new GameObject("WaitingIndicator", typeof(RectTransform), typeof(Image)); advWaitingNotice.transform.SetParent(advControlsCanvas.transform, false);
                var image = advWaitingNotice.GetComponent<Image>(); image.color = new Color(.04f, .065f, .09f, .94f); image.raycastTarget = false; AdvControlText(advWaitingNotice, "等待模型响应…");
            }
            if (advControlsCanvas == null) return;
            advComposeButton.gameObject.SetActive(input); advWaitingNotice.SetActive(waiting);
            PlaceAdvControl(advComposeButton.gameObject, AdvComposeRect);
            Rect area = AdvWaitingArea; PlaceAdvControl(advWaitingNotice, new Rect(area.x, area.y, area.width, 34));
            advControlsCanvas.SetActive(input || waiting);
        }
    }
}
