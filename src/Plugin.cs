using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace KK_StoryNPCBridge
{
    internal sealed class BridgeException : Exception
    {
        public readonly string Code;
        public BridgeException(string code, string message) : base(message) { Code = code; }
    }

    [BepInPlugin(PluginGuid, PluginName, Version)]
    [BepInProcess("Koikatu.exe")]
    [BepInProcess("Koikatsu Party.exe")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Version = "1.5.0";
        public const string PluginGuid = "KK_StoryNPCBridge";
        public const string PluginName = "KK_StoryNPCBridge";
        public const int Protocol = 2;
        public const string ActorId = "bridge:main";
        internal static Plugin Current;
        BridgeServer server;
        Harmony harmony;
        ActionScene action;
        SaveData previousSave;
        ActionGame.NightMainMenu nightMenu;
        TalkScene currentTalk;
        string world = "none";
        int map = -1, port;
        long eventSequence;
        float nextSnapshot, sampleInterval, nextContextScan;
        string lastError;
        KeyboardShortcut panelShortcut;
        readonly List<object> events = new List<object>();
        bool allowGameCommands;
        ConfigEntry<string> uiLanguage;

        void Awake()
        {
            Current = this;
            if (!Config.Bind("Bridge", "Enabled", true, "Enable the local NPC bridge").Value) return;
            uiLanguage = Config.Bind("UI", "Language", "en", new ConfigDescription("Plugin UI language: en or zh-CN. API descriptions remain English. Restart the game after editing this file.", new AcceptableValueList<string>("en", "zh-CN")));
            port = Config.Bind("Bridge", "Port", 64321, "IPv4 loopback TCP port").Value;
            string token = Config.Bind("Bridge", "Token", Guid.NewGuid().ToString("N"), "Local authentication token; do not publish").Value;
            panelShortcut = Config.Bind("Controls", "PanelShortcut", new KeyboardShortcut(KeyCode.F10, KeyCode.LeftControl, KeyCode.LeftShift), "Toggle dedicated NPC panel; F8 is reserved for the game").Value;
            sampleInterval = 1f / Mathf.Clamp(Config.Bind("Bridge", "SnapshotHz", 5, "Snapshots per second, 1-20").Value, 1, 20);
            allowGameCommands = Config.Bind("Development", "AllowGameCommands", true, "Allow authenticated story loading, map transitions, dialogue continuation and screenshots for testing").Value;
            if (port < 1024 || port > 65535 || token.Length < 24) { Logger.LogError("Invalid bridge port or token length"); return; }
            try
            {
                InitializeCharacterFactory(); InitializeOwnedProfile(); InitializeAutonomyJournal();
                server = new BridgeServer(port, token); server.Start();
                harmony = new Harmony(PluginGuid);
                harmony.Patch(AccessTools.Method(typeof(TextController), "Set", new Type[] { typeof(string), typeof(string) }, null), null,
                    new HarmonyMethod(typeof(Plugin).GetMethod("TextSetPostfix", BindingFlags.Static | BindingFlags.NonPublic)), null, null, null);
                InstallNativeHooks(); InstallDialogueHooks(); InstallAdvInputHooks(); InstallNativeCommandHooks(); InstallTimeHooks(); InitializeControlRecovery(); InstallAdvRecordHooks(); InstallNativeEventHooks(); InstallAdvSequenceHooks(); InstallExpressionHooks();
                Logger.LogInfo(PluginName + " " + Version + " dedicated actor " + ActorId + "; 127.0.0.1:" + port + "; " + panelShortcut);
            }
            catch (Exception e) { Logger.LogError(e); if (server != null) server.Dispose(); server = null; }
        }

        void Update()
        {
            if (server == null) return;
            try
            {
                if (panelShortcut.IsDown()) TogglePanel();
                if (ownedController == panelOwner) System.Threading.Interlocked.Exchange(ref panelOwner.LastSeen, DateTime.UtcNow.Ticks);
                RefreshWorld(); TickOwnedActor(); TickGameTime(); TickAutonomyJournal(); TickNativeInteractions(); TickNativeEvents(); TickAdvDialogue(); TickPlayerLineWhileWaiting(); TickAdvControls(); SyncAdvInputLock();
                for (int i = 0; i < 8; i++)
                {
                    Request request = server.Pop(); if (request == null) break;
                    if (!request.Session.Alive || request.Session.CleanExit) continue;
                    object id = request.Data["id"];
                    if ((DateTime.UtcNow - request.Received).TotalSeconds > 5)
                    { request.Session.Reply(BridgeServer.Error(id, "expired", "Command expired before execution")); continue; }
                    try
                    {
                        request.Session.Reply(BridgeServer.Ok(id, Execute(request.Data, request.Session)));
                        if ((string)request.Data["op"] == "disconnect") request.Session.CleanExit = true;
                    }
                    catch (BridgeException e) { request.Session.Reply(BridgeServer.Error(id, e.Code, e.Message)); }
                    catch (Exception e) { Logger.LogWarning(e); request.Session.Reply(BridgeServer.Error(id, "game_error", e.Message)); }
                }
                if (Time.realtimeSinceStartup >= nextSnapshot)
                { server.Snapshot = Json.Stringify(Snapshot()); nextSnapshot = Time.realtimeSinceStartup + sampleInterval; }
            }
            catch (Exception e)
            { if (lastError != e.ToString()) { lastError = e.ToString(); Logger.LogError(e); } }
        }
        void LateUpdate()
        {
            try { TickAdvRecords(); } catch (Exception e) { Logger.LogWarning("ADV record: " + e.Message); }
            if (ownedCha == null || !ownedReady || ownedRoot == null || !ownedRoot.activeInHierarchy) return;
            try
            {
                if (faceOverride && (Mode == "roaming" || OwnedTalkActive)) ApplyFace(ownedCha, ownedFace);
                if (animationOverride && (Mode == "roaming" || OwnedTalkActive) && ownedCha.animBody.GetCurrentAnimatorStateInfo(0).shortNameHash != Animator.StringToHash(ownedAnimation))
                    ownedCha.animBody.Play(ownedAnimation, 0, 0);
            }
            catch (Exception e) { if (lastError != e.ToString()) { lastError = e.ToString(); Logger.LogError(e); } }
        }
        void OnDestroy()
        {
            try { DestroyOwnedActor("plugin_unloaded"); RestoreCursor(); }
            finally
            {
                if (server != null) server.Dispose();
                if (harmony != null) harmony.UnpatchSelf();
                if (panelBackground != null) UnityEngine.Object.Destroy(panelBackground);
                if (dialogueOverlayTexture != null) UnityEngine.Object.Destroy(dialogueOverlayTexture);
                if (advInputShield != null) UnityEngine.Object.Destroy(advInputShield);
                if (advControlsCanvas != null) UnityEngine.Object.Destroy(advControlsCanvas);
                if (Current == this) Current = null;
            }
        }
        void RefreshWorld()
        {
            if (Time.realtimeSinceStartup >= nextContextScan)
            {
                nightMenu = UnityEngine.Object.FindObjectOfType<ActionGame.NightMainMenu>();
                currentTalk = UnityEngine.Object.FindObjectOfType<TalkScene>();
                nextContextScan = Time.realtimeSinceStartup + 0.2f;
            }
            Manager.Game game = Manager.Game.IsInstance() ? Manager.Game.Instance : null;
            SaveData save = game != null ? game.saveData : null;
            ActionScene nextAction = game != null ? game.actScene : null;
            int nextMap = nextAction != null && nextAction.Map != null ? nextAction.Map.no : -1;
            bool newSave = !ReferenceEquals(previousSave, save);
            if (newSave || nextAction != action || nextMap != map)
            {
                if (ownedRoot == null) { ownedNpc = null; ownedCha = null; ownedAgent = null; ownedReady = false; }
                if (panelVisible) { RestoreCursor(); panelVisible = false; }
            }
            if (newSave)
            {
                if (ownedSave != save) { ReleaseOwned("world_changed", false); ClearControlRecovery(); observedCycle = null; }
                previousSave = save; world = Guid.NewGuid().ToString("N").Substring(0, 12);
                AddEvent("world_changed", Json.Map("world", world));
            }
            action = nextAction; map = nextMap;
        }
        bool SceneBusy { get { return Manager.Scene.IsInstance() && Manager.Scene.Instance.IsNowLoadingFade; } }
        string Mode
        {
            get
            {
                if (action == null) return "menu";
                if (nativeTalkPending) return "talk";
                if (nightMenu != null) return "night_menu";
                if (Manager.Scene.IsInstance() && Manager.Scene.Instance.AddSceneName == "Talk") return "talk";
                return action.isEventNow || (action.Cycle != null && !action.Cycle.isAction) ? "event" : "roaming";
            }
        }
        object Snapshot()
        {
            var actors = new object[] { ReadOwnedActor() };
            return Json.Map("pluginName", PluginName, "pluginGuid", PluginGuid, "protocol", Protocol, "version", Version, "world", world, "mode", Mode, "map", map, "sceneBusy", SceneBusy,
                "utc", DateTime.UtcNow.ToString("o"), "clients", server.ClientCount, "descriptionLanguage", "en", "uiLanguage", uiLanguage.Value,
                "player", ReadPlayer(), "gameTime", ReadGameTime(),
                "panelVisible", panelVisible, "panelShortcut", panelShortcut.ToString(), "actors", actors,
                "nativeRosterCount", Manager.Game.IsInstance() && Manager.Game.Instance.HeroineList != null ? Manager.Game.Instance.HeroineList.Count : 0,
                "nativeNpcCount", action != null && action.npcList != null ? action.npcList.Count : 0,
                "eventSequence", eventSequence, "events", events.ToArray());
        }
        void AddEvent(string kind, object data)
        {
            bool actorEvent = !kind.StartsWith("game_", StringComparison.Ordinal) && kind != "world_changed";
            events.Add(Json.Map("seq", ++eventSequence, "kind", kind, "target", actorEvent ? ActorId : null, "generation", actorEvent ? ownedGeneration : null,
                "utc", DateTime.UtcNow.ToString("o"), "data", data));
            if (events.Count > 40) events.RemoveAt(0);
            ObserveUncontrolledEvent(kind, data);
        }
        static void TextSetPostfix(TextController __instance, string __0, string __1)
        {
            if (Current == null) return;
            try { Current.QueueAdvRecord(__instance, __0, __1); }
            catch (Exception e) { Current.Logger.LogWarning(e.Message); }
        }
        static object Vector(Vector3 value) { return new float[] { value.x, value.y, value.z }; }
        static bool FaceReady(ChaControl c)
        {
            try { return c != null && c.eyesCtrl != null && c.eyebrowCtrl != null && c.mouthCtrl != null && c.eyesCtrl.GetMaxPtn() > 0 && c.eyebrowCtrl.GetMaxPtn() > 0 && c.mouthCtrl.GetMaxPtn() > 0; }
            catch (NullReferenceException) { return false; }
        }
        static Dictionary<string, object> ReadFace(ChaControl c)
        { return Json.Map("eyes", c.GetEyesPtn(), "eyebrow", c.GetEyebrowPtn(), "mouth", c.GetMouthPtn(), "eyesOpen", c.GetEyesOpenMax(), "mouthOpen", c.GetMouthOpenMax(), "blink", c.GetEyesBlinkFlag(),
            "eyebrowOpen", c.GetEyebrowOpenMax(), "mouthFixed", c.GetMouthFixed(), "blush", c.fileStatus.hohoAkaRate, "tears", c.tearsLv, "highlight", !c.fileStatus.hideEyesHighlight, "gaze", c.GetLookEyesPtn()); }
        static void ApplyFaceValues(ChaControl c, Dictionary<string, object> face)
        {
            c.enableExpression = false;
            c.ChangeEyesPtn(Convert.ToInt32(face["eyes"]), true); c.ChangeEyebrowPtn(Convert.ToInt32(face["eyebrow"]), true); c.ChangeMouthPtn(Convert.ToInt32(face["mouth"]), true);
            c.ChangeEyesOpenMax(Convert.ToSingle(face["eyesOpen"]));
            c.ChangeEyesBlinkFlag((bool)face["blink"]); c.ChangeEyesShaking(false);
            foreach (string key in ExtraFaceFields) { object value; if (face.TryGetValue(key, out value)) WriteExtraFace(c, key, value); }
        }
    }
}
