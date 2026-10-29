using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        readonly Session panelOwner = new Session();
        bool panelVisible, cursorSaved;
        CursorLockMode oldLock;
        bool oldCursorVisible, oldActionCursor;
        ActionScene cursorAction;
        Rect panelRect = new Rect(18, 48, 480, 620);
        Vector2 panelScroll;
        string uiNotice = "未接管时由原生 AI 管理；开始控制时返回期间行为记录。";
        string favorText = "0", intimacyText = "0", angerText = "0";
        string eyesText = "0", eyebrowText = "0", mouthText = "0", mouthOpenText = "0";
        string xText = "0", yText = "0", zText = "0", yawText = "0", animationText = "Idle";
        string taskActionText = "10", taskMapText = "-1", taskDurationText = "10", desireText = "50";
        bool showActionCatalog;
        string sayText = "";
        Font panelFont;
        Texture2D panelBackground;
        GUIStyle panelStyle;

        void AcquireCursor()
        {
            if (cursorSaved) return;
            oldLock = Cursor.lockState; oldCursorVisible = Cursor.visible; cursorAction = action;
            if (cursorAction != null)
            {
                oldActionCursor = cursorAction.isCursorLock;
                HarmonyLib.AccessTools.Field(typeof(ActionScene), "_isCursorLock").SetValue(cursorAction, false);
            }
            cursorSaved = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        void RestoreCursor()
        {
            if (!cursorSaved) return; cursorSaved = false;
            if (cursorAction != null) HarmonyLib.AccessTools.Field(typeof(ActionScene), "_isCursorLock").SetValue(cursorAction, oldActionCursor);
            Cursor.lockState = oldLock; Cursor.visible = oldCursorVisible;
        }
        void TogglePanel()
        {
            panelVisible = !panelVisible;
            if (panelVisible) { AcquireCursor(); panelOwner.Alive = true; FillOwnedFields(); }
            else
            {
                if (!DialogueActive) RestoreCursor();
            }
        }
        void OnGUI()
        {
            if (server == null) return;
            if (panelFont == null) panelFont = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 16);
            Font original = GUI.skin.font; bool guiEnabled = GUI.enabled; GUI.skin.font = panelFont;
            try
            {
                SyncAdvInputLock(); ConsumeBlockedAdvMouse();
                GUI.enabled = guiEnabled && !AdvMouseBlocked;
                if (panelVisible && !AdvMouseBlocked)
                {
                    System.Threading.Interlocked.Exchange(ref panelOwner.LastSeen, DateTime.UtcNow.Ticks);
                    if (panelStyle == null)
                    {
                        panelBackground = new Texture2D(1, 1);
                        panelBackground.SetPixel(0, 0, new Color(0.035f, 0.055f, 0.075f, 0.97f)); panelBackground.Apply();
                        panelStyle = new GUIStyle(GUI.skin.window);
                        panelStyle.normal.background = panelBackground; panelStyle.onNormal.background = panelBackground;
                        panelStyle.normal.textColor = Color.white; panelStyle.onNormal.textColor = Color.white;
                    }
                    panelRect.width = Mathf.Min(480, Screen.width - 24); panelRect.height = Mathf.Min(720, Screen.height - 50);
                    panelRect = GUI.Window(1731643, panelRect, DrawPanel, T("KK_StoryNPCBridge 开发调试 ") + Version + " · " + panelShortcut, panelStyle);
                }
                GUI.enabled = guiEnabled;
                DrawAdvDialogueOverlay();
            }
            finally { GUI.skin.font = original; GUI.enabled = guiEnabled; }
        }
        void DrawPanel(int window)
        {
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            GUILayout.Label(ownedName + T(" · 专属 NPC · ") + ownedState);
            GUILayout.Label((ownedController == null ? T("原生 AI 管理") : T("插件控制中")) + T("   |   地图 ") + ownedMap + T("   |   身份 ") + ActorId);
            GUILayout.Label(T("结束控制后恢复原生行为，未受控期间持续记录。数值独立保存。"));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("开始控制"))) Ui("claim", null);
            if (GUILayout.Button(T("结束控制"))) Ui("release", null);
            if (GUILayout.Button(T("回读"))) FillOwnedFields();
            if (GUILayout.Button(T("走到我附近"))) Ui("summon", null);
            GUILayout.EndHorizontal();
            if (Approaching) GUILayout.Label(ownedMap == map ? T("正在走近你") : T("正在从地图 ") + ownedMap + T(" 前往地图 ") + map);
            else if (approachState == "arrived") GUILayout.Label(T("已走到你附近"));
            else if (approachState == "failed") GUILayout.Label(T("无法到达：") + approachError);
            if (!ownedReady)
            {
                GUILayout.Label(T("角色当前未在本地图加载；接管后可使用“走到我附近”。"));
                if (!string.IsNullOrEmpty(ownedError)) GUILayout.Label(ownedError);
            }
            else
            {
                if (ownedNpc != null && ownedNpc.AI != null && ownedNpc.AI.result != null)
                    GUILayout.Label(T("当前行动 ") + ownedNpc.AI.actionNo + " · " + ownedNpc.AI.result.actionName + T(" · 队列 ") + nativeQueue.Count);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(T("玩家发起 ADV 对话"))) Ui("dialogue_open", Json.Map("text", sayText));
                if (GUILayout.Button(T("NPC 主动邀聊"))) Ui("dialogue_open", Json.Map("text", sayText, "initiator", "npc"));
                GUILayout.EndHorizontal();
                if (DialogueActive)
                {
                    GUILayout.Label(T("对话状态：") + dialoguePhase);
                    if (WaitingModel)
                    {
                        manualModelText = GUILayout.TextArea(manualModelText, 4000, GUILayout.MinHeight(50));
                        if (GUILayout.Button(T("提交外部响应"))) UiModelReply();
                    }
                    if (dialoguePhase == "ready")
                    {
                        if (GUILayout.Button(T("请求用户输入"))) DialogueControl("dialogue_request_input", Json.Map("conversationId", dialogueId));
                        if (GUILayout.Button(T("等待外部响应"))) DialogueControl("dialogue_wait", Json.Map("conversationId", dialogueId));
                    }
                    if (GUILayout.Button(T("结束 ADV 对话"))) EndAdvDialogue("user_closed");
                }
                taskActionText = Field(T("原生行动 ID"), taskActionText);
                taskMapText = Field(T("目标地图（-1自动）"), taskMapText);
                taskDurationText = Field(T("执行秒数（0为本体默认）"), taskDurationText);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(T("立即执行"))) UiNativeTask("set_action");
                if (GUILayout.Button(T("加入接下来队列"))) UiNativeTask("queue_action");
                if (GUILayout.Button(T("清空队列"))) Ui("clear_queue", null);
                GUILayout.EndHorizontal();
                desireText = Field(T("行动欲望 0–100"), desireText);
                if (GUILayout.Button(T("修改该行动欲望"))) UiParsed(delegate { return Json.Map("op", "set_desire", "action", ParseNumber(taskActionText), "value", ParseNumber(desireText)); });
                showActionCatalog = GUILayout.Toggle(showActionCatalog, T("显示本体行动编号"));
                if (showActionCatalog) foreach (var item in (System.Collections.IEnumerable)ReadNativeActions())
                { var entry = (Dictionary<string, object>)item; GUILayout.Label(entry["id"] + " · " + entry["name"] + T(" · 欲望 ") + entry["desire"] + T(" · 地图 ") + Json.Stringify(entry["mapsWithPoints"])); }
                favorText = Field(T("好感"), favorText); intimacyText = Field(T("亲密"), intimacyText); angerText = Field(T("怒气"), angerText);
                if (GUILayout.Button(T("保存数值"))) UiParsed(delegate { return Json.Map("op", "set_stats", "changes", Json.Map("favor", ParseNumber(favorText), "intimacy", ParseNumber(intimacyText), "anger", ParseNumber(angerText))); });
                GUILayout.Label(T("表情（保持至下一次插件命令）"));
                eyesText = Field(T("眼"), eyesText); eyebrowText = Field(T("眉"), eyebrowText); mouthText = Field(T("嘴"), mouthText); mouthOpenText = Field(T("嘴张开 0–1"), mouthOpenText);
                if (GUILayout.Button(T("应用表情"))) UiParsed(delegate { return Json.Map("op", "set_expression", "changes", Json.Map("eyes", ParseNumber(eyesText), "eyebrow", ParseNumber(eyebrowText), "mouth", ParseNumber(mouthText), "mouthOpen", ParseNumber(mouthOpenText))); });
                if (GUILayout.Button(T("恢复默认表情"))) Ui("clear_expression", null);
                GUILayout.Label(T("世界坐标（当前地图导航区域）"));
                xText = Field("X", xText); yText = Field("Y", yText); zText = Field("Z", zText); yawText = Field(T("朝向"), yawText);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(T("走过去"))) UiPosition("move");
                if (GUILayout.Button(T("传送"))) UiPosition("teleport");
                if (GUILayout.Button(T("站住"))) Ui("stop", null);
                if (GUILayout.Button(T("转向"))) UiParsed(delegate { return Json.Map("op", "turn", "yaw", ParseNumber(yawText)); });
                GUILayout.EndHorizontal();
                animationText = Field(T("动画状态"), animationText);
                if (GUILayout.Button(T("播放动画"))) Ui("play_animation", Json.Map("state", animationText));
                sayText = GUILayout.TextArea(sayText, GUILayout.MinHeight(40));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(T("ADV 台词"))) Ui("say", Json.Map("text", sayText));
                if (GUILayout.Button(T("开始 ADV 对话"))) Ui("dialogue_open", Json.Map("text", sayText));
                if (GUILayout.Button(T("NPC 主动邀聊"))) Ui("dialogue_open", Json.Map("text", sayText, "initiator", "npc"));
                GUILayout.EndHorizontal();
                GUILayout.Label(T("重新载入故事可重建专属 NPC。"));
            }
            GUILayout.Space(6); GUILayout.Label(T(uiNotice)); GUILayout.EndScrollView(); GUI.DragWindow(new Rect(0, 0, 450, 24));
        }
        static string Field(string label, string value)
        { GUILayout.BeginHorizontal(); GUILayout.Label(label, GUILayout.Width(120)); value = GUILayout.TextField(value); GUILayout.EndHorizontal(); return value; }
        static double ParseNumber(string text)
        {
            double value;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsInfinity(value) || double.IsNaN(value)) throw new BridgeException("invalid_argument", "Enter a valid number");
            return value;
        }
        void UiModelReply()
        {
            try { ReceiveExternalReply(Json.Map("conversationId", dialogueId, "requestId", modelRequestId, "text", manualModelText), false); uiNotice = "已提交回复。"; }
            catch (BridgeException e) { uiNotice = e.Message; }
        }
        void FillOwnedFields()
        {
            favorText = Convert.ToString(ownedStats["favor"], CultureInfo.InvariantCulture); intimacyText = Convert.ToString(ownedStats["intimacy"], CultureInfo.InvariantCulture); angerText = Convert.ToString(ownedStats["anger"], CultureInfo.InvariantCulture);
            if (!ownedReady || ownedRoot == null || !FaceReady(ownedCha)) return;
            Vector3 p = ownedRoot.transform.position;
            xText = p.x.ToString("F3", CultureInfo.InvariantCulture); yText = p.y.ToString("F3", CultureInfo.InvariantCulture); zText = p.z.ToString("F3", CultureInfo.InvariantCulture);
            yawText = ownedRoot.transform.eulerAngles.y.ToString("F1", CultureInfo.InvariantCulture);
            eyesText = ownedCha.GetEyesPtn().ToString(); eyebrowText = ownedCha.GetEyebrowPtn().ToString(); mouthText = ownedCha.GetMouthPtn().ToString(); mouthOpenText = ownedCha.GetMouthOpenMax().ToString("F2", CultureInfo.InvariantCulture); animationText = ownedAnimation;
        }
        void UiNativeTask(string op) { UiParsed(delegate { return Json.Map("op", op, "action", ParseNumber(taskActionText), "map", ParseNumber(taskMapText), "duration", ParseNumber(taskDurationText)); }); }
        void UiPosition(string op) { UiParsed(delegate { return Json.Map("op", op, "position", new double[] { ParseNumber(xText), ParseNumber(yText), ParseNumber(zText) }); }); }
        void Ui(string op, Dictionary<string, object> extra)
        {
            var command = Json.Map("op", op); if (extra != null) foreach (var p in extra) command.Add(p.Key, p.Value);
            UiParsed(delegate { return command; });
        }
        void UiParsed(Func<Dictionary<string, object>> build)
        {
            try
            {
                var command = build(); command["target"] = ActorId; command["generation"] = ownedGeneration;
                Execute(command, panelOwner); uiNotice = "命令已执行。";
            }
            catch (BridgeException e) { uiNotice = e.Code + ": " + e.Message; }
            catch (Exception e) { uiNotice = e.Message; Logger.LogWarning(e); }
        }
    }
}
