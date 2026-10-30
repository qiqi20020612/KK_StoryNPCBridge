using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        GameObject advInputShield, previousAdvSelection;
        Button advCancelButton;
        bool advInputLocked;
        float nextPlayerLineAdvance;
        int blockedAdvRequests;
        bool internalAdvAdvance;
        bool AdvMouseBlocked { get { return WaitingModel || ExternalWaitVisible || Recovering && OwnedTalkActive && dialoguePhase != "ending"; } }
        Rect AdvWaitingArea { get { return new Rect((Screen.width - Mathf.Min(1000, Screen.width - 48)) / 2, Mathf.Max(36, Screen.height - 410), Mathf.Min(1000, Screen.width - 48), 82); } }
        Rect AdvCancelRect { get { Rect area = AdvWaitingArea; return new Rect(area.x + 8, area.y + 34, area.width - 16, 30); } }

        void InstallAdvInputHooks()
        {
            foreach (string name in new string[] { "TextNext", "WindowNoneButton", "WindowNoneButtonCancel", "BackLogButton", "BackLogButtonCancel", "BackLogTextNext", "BackLogTextBack" })
                PatchNative(typeof(ADV.KeyInput), name, new Type[] { typeof(bool), typeof(bool) }, "AllowAdvKeyInput", null, null);
            foreach (string name in new string[] { "get_MouseWheel", "get_SkipButton", "get_BackLogTextPageNext", "get_BackLogTextPageBack", "get_BackLogTextFirst", "get_BackLogTextLast" })
                PatchNative(typeof(ADV.KeyInput), name, Type.EmptyTypes, "AllowAdvKeyInput", null, null);
            PatchNative(typeof(Button), "OnPointerClick", new Type[] { typeof(PointerEventData) }, "AllowAdvButtonInput", null, null);
            PatchNative(typeof(Button), "OnSubmit", new Type[] { typeof(BaseEventData) }, "AllowAdvButtonInput", null, null);
            PatchNative(typeof(ADV.TextScenario), "RequestNextLine", Type.EmptyTypes, "AllowAdvNextLine", null, null);
        }
        static bool AllowAdvKeyInput()
        { return Current == null || !Current.AdvMouseBlocked; }
        static bool AllowAdvButtonInput(Button __instance)
        { return Current == null || !Current.AdvMouseBlocked || __instance == Current.advCancelButton; }
        static bool AllowAdvNextLine(ADV.TextScenario __instance)
        {
            Plugin p = Current;
            if (p != null && p.internalAdvAdvance) return true;
            if (p != null && (p.Recovering || p.ExternalWaitVisible) && p.OwnedTalkActive && p.dialoguePhase != "ending") { p.blockedAdvRequests++; return false; }
            if (p == null || p.managedWait == null || p.releaseAdvHold) return true;
            ADV.TextScenario held = p.ManagedAdvScenario();
            if (held != __instance) return true;
            p.blockedAdvRequests++; return false;
        }
        ADV.TextScenario ManagedAdvScenario()
        {
            if (managedWait != null)
                return AccessTools.Property(typeof(ADV.CommandBase), "scenario").GetValue(managedWait, null) as ADV.TextScenario;
            foreach (ADV.TextScenario scenario in UnityEngine.Object.FindObjectsOfType<ADV.TextScenario>())
                if (scenario.heroineList != null && scenario.heroineList.Contains(ownedHeroine)) return scenario;
            return null;
        }
        void AdvanceBridgeAdvLine(ADV.TextScenario scenario)
        {
            internalAdvAdvance = true;
            try { AccessTools.Method(typeof(ADV.TextScenario), "RequestNextLine", Type.EmptyTypes, null).Invoke(scenario, new object[0]); }
            finally { internalAdvAdvance = false; }
        }
        void SyncAdvInputLock()
        {
            bool locked = AdvMouseBlocked;
            if (locked && advInputShield == null)
            {
                advInputShield = new GameObject("KK_StoryNPCBridge.AdvInputShield", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                advInputShield.transform.SetParent(transform, false);
                var canvas = advInputShield.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.overrideSorting = true; canvas.sortingOrder = short.MaxValue;
                var blocker = new GameObject("PointerBlocker", typeof(RectTransform), typeof(Image)); blocker.transform.SetParent(advInputShield.transform, false);
                var full = blocker.GetComponent<RectTransform>(); full.anchorMin = Vector2.zero; full.anchorMax = Vector2.one; full.offsetMin = full.offsetMax = Vector2.zero;
                blocker.GetComponent<Image>().color = new Color(0, 0, 0, 0.001f); blocker.GetComponent<Image>().raycastTarget = true;
                var cancel = new GameObject("EndDialogue", typeof(RectTransform), typeof(Image), typeof(Button)); cancel.transform.SetParent(advInputShield.transform, false);
                cancel.GetComponent<Image>().color = new Color(0.07f, 0.1f, 0.15f, 0.98f);
                advCancelButton = cancel.GetComponent<Button>(); advCancelButton.targetGraphic = cancel.GetComponent<Image>();
                advCancelButton.onClick.AddListener(delegate { if (AdvMouseBlocked) CancelDisplayedDialogue(); });
                var label = new GameObject("Label", typeof(RectTransform), typeof(Text)); label.transform.SetParent(cancel.transform, false);
                var labelRect = label.GetComponent<RectTransform>(); labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
                if (panelFont == null) panelFont = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 16);
                var text = label.GetComponent<Text>(); text.font = panelFont; text.fontSize = 16; text.text = T("结束对话"); text.color = Color.white; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            }
            if (locked && advCancelButton != null)
            {
                Rect rect = AdvCancelRect; var rt = advCancelButton.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero;
                rt.anchoredPosition = new Vector2(rect.x, Screen.height - rect.yMax); rt.sizeDelta = new Vector2(rect.width, rect.height);
            }
            if (advInputLocked != locked)
            {
                var system = EventSystem.current;
                if (locked)
                { previousAdvSelection = system == null ? null : system.currentSelectedGameObject; if (system != null) system.SetSelectedGameObject(null); }
                else if (system != null && (system.currentSelectedGameObject == null || advCancelButton != null && system.currentSelectedGameObject == advCancelButton.gameObject))
                    system.SetSelectedGameObject(previousAdvSelection != null && previousAdvSelection.activeInHierarchy ? previousAdvSelection : null);
                if (!locked) previousAdvSelection = null;
                advInputLocked = locked;
            }
            if (advInputShield != null) advInputShield.SetActive(locked);
        }
        void ConsumeBlockedAdvMouse()
        {
            if (!AdvMouseBlocked || Event.current == null) return;
            Event e = Event.current;
            if ((e.isMouse || e.type == EventType.ScrollWheel) && !(e.button == 0 && AdvCancelRect.Contains(e.mousePosition) && e.type != EventType.ScrollWheel)) e.Use();
        }
        void TickPlayerLineWhileWaiting()
        {
            if (Recovering) return;
            // Native preparation and the submitted player's text must not require locked input.
            bool opening = dialoguePhase == "opening" && WaitingModel;
            if ((!opening && dialoguePhase != "user_speaking") || advSawHold || SceneBusy || Time.realtimeSinceStartup < nextPlayerLineAdvance) return;
            if (opening && currentTalk != null && ReferenceEquals(currentTalk.targetHeroine, ownedHeroine))
            { Tutorial tutorial = UnityEngine.Object.FindObjectOfType<Tutorial>(); if (tutorial != null) tutorial.EndScene(true); }
            ADV.TextScenario scenario = ManagedAdvScenario();
            if (scenario == null || scenario.textController == null || !scenario.textController.NameMessageVisible || !scenario.textController.IsCompleteDisplayText) return;
            AdvanceBridgeAdvLine(scenario);
            nextPlayerLineAdvance = Time.realtimeSinceStartup + 0.2f;
        }
        object AdvPointerProbe(Dictionary<string, object> command)
        {
            var system = EventSystem.current;
            if (system == null) throw new BridgeException("not_ready", "No game UI event system");
            object x, y;
            if (!command.TryGetValue("x", out x) || !command.TryGetValue("y", out y)) throw new BridgeException("invalid_argument", "Normalized x/y required");
            var data = new PointerEventData(system); data.position = new Vector2((float)Number(x, 0, 1, "x") * Screen.width, (1 - (float)Number(y, 0, 1, "y")) * Screen.height);
            string button = OptionalText(command, "button", "left");
            if (button != "left" && button != "right" && button != "middle" && button != "scroll") throw new BridgeException("invalid_argument", "Unknown pointer button");
            data.button = button == "right" ? PointerEventData.InputButton.Right : button == "middle" ? PointerEventData.InputButton.Middle : PointerEventData.InputButton.Left;
            var hits = new List<RaycastResult>(); system.RaycastAll(data, hits); GameObject top = hits.Count == 0 ? null : hits[0].gameObject;
            bool cancel = top != null && advCancelButton != null && (top == advCancelButton.gameObject || top.transform.IsChildOf(advCancelButton.transform));
            bool blocked = top != null && advInputShield != null && top.transform.IsChildOf(advInputShield.transform) && !cancel;
            if (top != null && OptionalBool(command, "click", false))
            {
                if (button == "scroll") { data.scrollDelta = new Vector2(0, 1); ExecuteEvents.ExecuteHierarchy(top, data, ExecuteEvents.scrollHandler); }
                else
                {
                    ExecuteEvents.ExecuteHierarchy(top, data, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.ExecuteHierarchy(top, data, ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.ExecuteHierarchy(top, data, ExecuteEvents.pointerClickHandler);
                }
            }
            return Json.Map("hit", top == null ? null : top.name, "blocked", blocked, "endButton", cancel, "mouseBlocked", AdvMouseBlocked);
        }
        object AdvInputTargets()
        {
            var controls = new List<object>(); ADV.TextScenario scenario = ManagedAdvScenario();
            if (scenario != null)
            {
                Transform root = scenario.advScene != null ? scenario.advScene.transform : scenario.transform;
                foreach (Selectable control in root.GetComponentsInChildren<Selectable>(false))
                {
                    Canvas canvas = control.GetComponentInParent<Canvas>(); var rect = control.GetComponent<RectTransform>();
                    if (rect == null || canvas == null || !canvas.enabled) continue;
                    var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                    Vector2 center = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, (corners[0] + corners[2]) / 2);
                    if (center.x < 0 || center.y < 0 || center.x > Screen.width || center.y > Screen.height) continue;
                    Text label = control.GetComponentInChildren<Text>();
                    controls.Add(Json.Map("name", control.name, "kind", control.GetType().Name, "label", label == null ? null : label.text, "x", center.x / Screen.width, "y", 1 - center.y / Screen.height));
                }
            }
            Rect end = AdvCancelRect, compose = AdvComposeRect;
            return Json.Map("locked", AdvMouseBlocked, "shieldActive", advInputShield != null && advInputShield.activeInHierarchy,
                "controls", controls, "endX", end.center.x / Screen.width, "endY", end.center.y / Screen.height,
                "composeX", compose.center.x / Screen.width, "composeY", compose.center.y / Screen.height,
                "line", scenario == null ? -1 : scenario.CurrentLine,
                "isChoice", scenario != null && scenario.isChoice,
                "messageVisible", scenario != null && scenario.textController != null && scenario.textController.NameMessageVisible,
                "text", scenario == null || scenario.textController == null ? null : scenario.textController.MessageWindow.text,
                "auto", scenario != null && scenario.isAuto, "skip", scenario != null && scenario.isSkip);
        }
    }
}
