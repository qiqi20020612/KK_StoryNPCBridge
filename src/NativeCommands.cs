using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        Dictionary<string, object> requestNativeCommand;
        bool nativeCommandMenusShown;
        readonly Dictionary<Graphic, bool> nativeMenuRaycasts = new Dictionary<Graphic, bool>();

        void InstallNativeCommandHooks()
        {
            PatchNative(typeof(TalkScene), "CommandFunc", new Type[] { typeof(int) }, "RouteNativeCommand", null, null);
            PatchNative(typeof(TalkScene), "TouchFunc", new Type[] { typeof(string), typeof(Vector3) }, "RouteNativeTouch", "NativeTouchReturned", "NativeTouchFailed");
            PatchNative(typeof(TalkScene), "LoadVoice", new Type[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(bool) }, null, "NativeReactionLoaded", null);
            PatchNative(typeof(TalkScene), "TouchEnd", Type.EmptyTypes, null, "TrackNativeReactionEnd", null);
        }
        static bool RouteNativeCommand(TalkScene __instance, int __0)
        {
            Plugin p = Current;
            if (p != null && p.nativeEventCommand) return true;
            if (p == null || !ReferenceEquals(__instance.targetHeroine, p.ownedHeroine) || p.ownedController == null && !p.DialogueActive) return true;
            if (__0 == -1)
            {
                if (!p.DialogueActive) return true;
                if (p.dialoguePhase == "ending") return p.endingSent;
                p.EndAdvDialogue("native_end_button"); return false;
            }
            if (__0 < 0) return true;
            if (!p.NativeInputReady(__instance)) return false;
            p.SubmitNativeIntent(__instance, p.DescribeNativeCommand(__instance, __0));
            return false;
        }
        bool NativeInputReady(TalkScene talk)
        {
            if (SceneBusy || NativeEventBusy || ExternalWaitVisible || !NativeTalkReady(talk)) return false;
            return !DialogueActive || (dialoguePhase == "ready" || dialoguePhase == "awaiting_input") && !WaitingModel && (!advLineActive || !releaseAdvHold);
        }
        void SubmitNativeIntent(TalkScene talk, Dictionary<string, object> intent)
        {
            if (!DialogueActive)
            {
                currentTalk = talk;
                InitializeDialogueSession(talk.necessaryInfo != null && talk.necessaryInfo.isAttack ? "npc" : "player", defaultModelTimeoutSeconds);
                npcInvitationPending = false; dialogueHostSeen = true; queuedAdvText = null;
                AddEvent("dialogue_opened", Json.Map("conversationId", dialogueId, "channel", "native_adv", "initiator", dialogueInitiator, "source", "native_ui"));
            }
            StopNativeMenuActivity(talk);
            AccessTools.Property(typeof(TalkScene), "activeTalkContents").SetValue(talk, false, null);
            AccessTools.Property(typeof(TalkScene), "activeEventContents").SetValue(talk, false, null);
            requestNativeCommand = intent;
            dialogueTurn++; dialogueInput = "";
            AddEvent("native_command_intercepted", Json.Map("conversationId", dialogueId, "turn", dialogueTurn, "nativeCommand", intent));
            RequestExternalReply("native_command", null);
            QueueAdvLine("none", "", "waiting_model"); SetDialoguePhase("waiting_model");
        }
        void StopNativeIdleTimer(TalkScene talk)
        {
            object idle = AccessTools.Field(typeof(TalkScene), "ladInfo").GetValue(talk);
            if (idle != null) AccessTools.Method(idle.GetType(), "End", Type.EmptyTypes, null).Invoke(idle, new object[0]);
        }
        void StopNativeMenuActivity(TalkScene talk)
        {
            StopNativeIdleTimer(talk);
            IEnumerable colliders = AccessTools.Field(typeof(TalkScene), "lstColDisposable").GetValue(talk) as IEnumerable;
            if (colliders != null) foreach (object item in colliders)
                AccessTools.Method(item.GetType(), "End", Type.EmptyTypes, null).Invoke(item, new object[0]);
            object touch = AccessTools.Field(typeof(TalkScene), "touchDisposableInfo").GetValue(talk);
            if (touch != null) AccessTools.Method(touch.GetType(), "End", Type.EmptyTypes, null).Invoke(touch, new object[0]);
            Transform voice = AccessTools.Field(typeof(TalkScene), "transVoice").GetValue(talk) as Transform;
            if (voice != null && Manager.Voice.IsInstance())
            { Manager.Voice.Instance.Stop(voice); AccessTools.Field(typeof(TalkScene), "transVoice").SetValue(talk, null); }
        }
        void SyncNativeCommandMenus()
        {
            if (!DialogueActive || npcInvitationPending || currentTalk == null || !ReferenceEquals(currentTalk.targetHeroine, ownedHeroine) || dialoguePhase == "opening") return;
            if (dialoguePhase == "native_reaction")
            { nativeCommandMenusShown = false; RestoreNativeMenuRaycasts(); RestoreNativeDialogueMenus(); return; }
            if (dialoguePhase == "ending") { nativeCommandMenusShown = false; RestoreNativeMenuRaycasts(); HideNativeDialogueMenus(); return; }
            bool show = (dialoguePhase == "ready" || dialoguePhase == "awaiting_input") && (!advLineActive || managedWait != null && !releaseAdvHold);
            if (show)
            {
                if (nativeCommandMenusShown) return;
                RestoreNativeDialogueMenus();
                AccessTools.Method(typeof(TalkScene), "StartSelection", Type.EmptyTypes, null).Invoke(currentTalk, new object[0]);
                StopNativeIdleTimer(currentTalk);
                ADV.TextScenario scenario = ManagedAdvScenario();
                if (scenario != null && scenario.advScene != null)
                    foreach (Graphic graphic in scenario.advScene.GetComponentsInChildren<Graphic>(true))
                        if (graphic.name == "FilterBack")
                        { nativeMenuRaycasts[graphic] = graphic.raycastTarget; graphic.raycastTarget = false; }
                nativeCommandMenusShown = true;
            }
            else if (advLineActive || queuedAdvText != null || WaitingModel)
            { nativeCommandMenusShown = false; RestoreNativeMenuRaycasts(); HideNativeDialogueMenus(); }
        }
        void RestoreNativeMenuRaycasts()
        {
            foreach (var pair in nativeMenuRaycasts) if (pair.Key != null) pair.Key.raycastTarget = pair.Value;
            nativeMenuRaycasts.Clear();
        }
        static Button NativeMenuButton(TalkScene talk, string field, int index)
        {
            object value = AccessTools.Field(typeof(TalkScene), field).GetValue(talk);
            Button[] buttons = value as Button[];
            return buttons == null ? value as Button : index >= 0 && index < buttons.Length ? buttons[index] : null;
        }
        static string NativeButtonLabel(Button button, string fallback)
        {
            // Native menus can use TextMeshPro rather than Unity UI Text.
            if (button != null) foreach (Component component in button.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().Name.IndexOf("Text", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var property = AccessTools.Property(component.GetType(), "text");
                if (property == null || !property.CanRead || property.PropertyType != typeof(string)) continue;
                string text = property.GetValue(component, null) as string;
                if (!string.IsNullOrEmpty(text) && text.Trim().Length > 0) return System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "").Trim();
            }
            return fallback;
        }
        Dictionary<string, object> DescribeNativeCommand(TalkScene talk, int command)
        {
            string category = command <= 2 ? "talk" : command == 3 ? "listen" : command == 10 ? "apologize" : "event";
            Button button = command <= 2 ? NativeMenuButton(talk, "buttonTalkContents", command) : command == 3 ? NativeMenuButton(talk, "buttonListen", -1) : NativeMenuButton(talk, "buttonEventContents", command - 10);
            string menu = category == "talk" ? "Small talk" : category == "listen" ? "Answer a question" : category == "apologize" ? "Apologize" : "Invite to an activity";
            string fallback = category == "talk" ? "Talk topic " + (command + 1) : category == "event" ? "Invitation option " + (command - 10) : menu;
            Image graphic = button == null ? null : button.targetGraphic as Image;
            return Json.Map("source", "native_button", "commandId", command, "category", category, "eventId", command >= 10 ? (object)(command - 10) : null,
                "menu", menu, "label", NativeButtonLabel(button, fallback), "object", button == null ? null : button.name,
                "sprite", graphic == null || graphic.sprite == null ? null : graphic.sprite.name);
        }
        object ReadNativeMenu()
        {
            TalkScene talk = UnityEngine.Object.FindObjectOfType<TalkScene>();
            ADV.TextScenario activeAdv = null;
            if (talk != null) foreach (ADV.TextScenario scenario in UnityEngine.Object.FindObjectsOfType<ADV.TextScenario>())
                if (scenario.heroineList != null && scenario.heroineList.Contains(talk.targetHeroine)) { activeAdv = scenario; break; }
            var controls = new List<object>();
            if (talk != null)
            {
                foreach (string field in new string[] { "buttonTalk", "buttonListen", "buttonEvent", "buttonEnd", "buttonTalkContents", "buttonEventContents" })
                {
                    object value = AccessTools.Field(typeof(TalkScene), field).GetValue(talk);
                    Button[] buttons = value as Button[] ?? new Button[] { value as Button };
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        Button b = buttons[i]; if (b == null) continue;
                        Canvas canvas = b.GetComponentInParent<Canvas>(); var rect = b.GetComponent<RectTransform>();
                        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                        Vector2 point = RectTransformUtility.WorldToScreenPoint(canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, (corners[0] + corners[2]) / 2);
                        object command = field == "buttonTalkContents" ? (object)i : field == "buttonEventContents" ? (object)(10 + i) : field == "buttonListen" ? (object)3 : field == "buttonEnd" ? (object)(-1) : null;
                        var groups = b.GetComponentsInParent<CanvasGroup>(); bool visible = b.gameObject.activeInHierarchy && canvas != null && canvas.enabled;
                        foreach (CanvasGroup group in groups) if (group.alpha < .01f || !group.blocksRaycasts || !group.interactable) visible = false;
                        controls.Add(Json.Map("field", field, "index", i, "name", b.name, "label", NativeButtonLabel(b, b.name), "active", visible, "interactable", b.IsInteractable(),
                            "commandId", command, "intent", command != null && (int)command >= 0 ? DescribeNativeCommand(talk, (int)command) : null,
                            "x", point.x / Screen.width, "y", 1 - point.y / Screen.height));
                    }
                }
            }
            return Json.Map("ownedTarget", talk != null && ReferenceEquals(talk.targetHeroine, ownedHeroine), "ready", NativeTalkReady(talk), "controls", controls,
                "nativeMode", talk == null ? null : (object)talk.touchMode, "interactionCounts", ReadNativeTouchCounts(talk), "nativeInteraction", ReadNativeInteraction(),
                "messageVisible", activeAdv != null && activeAdv.textController != null && activeAdv.textController.NameMessageVisible,
                "isChoice", activeAdv != null && activeAdv.isChoice,
                "nativeResult", talk == null || talk.resultInfo == null ? null : (object)(int)talk.resultInfo.result,
                "stats", talk == null || talk.targetHeroine == null ? null : Json.Map("favor", talk.targetHeroine.favor, "intimacy", talk.targetHeroine.intimacy, "anger", talk.targetHeroine.anger,
                    "talkTime", talk.targetHeroine.talkTime, "talkCommandCount", talk.targetHeroine.talkCommand.Count));
        }
        static object ReadNativeTouchCounts(TalkScene talk)
        {
            var result = new Dictionary<string, object>();
            if (talk != null)
            {
                IDictionary counts = AccessTools.Field(typeof(TalkScene), "dicTouchCount").GetValue(talk) as IDictionary;
                if (counts != null) foreach (DictionaryEntry entry in counts) result[entry.Key.ToString()] = entry.Value;
            }
            return result;
        }
    }
}
