using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        object ExecuteGameCommand(string op, Dictionary<string, object> command)
        {
            if (!allowGameCommands) throw new BridgeException("disabled", "Development game commands are disabled in configuration");
            if (SceneBusy) throw new BridgeException("busy", "The game is changing scenes");
            if (op == "game.pointer_probe") return AdvPointerProbe(command);
            if (op == "game.adv_input_targets") return AdvInputTargets();
            if (op == "game.native_menu") return ReadNativeMenu();
            if (op == "game.native_touch_probe")
            {
                TalkScene talk = UnityEngine.Object.FindObjectOfType<TalkScene>();
                if (talk == null || talk.targetHeroine != ownedHeroine || !NativeInputReady(talk)) throw new BridgeException("wrong_context", "Requires the bridge NPC's native conversation menu");
                string kind = OptionalText(command, "kind", "touch");
                if (kind != "touch" && kind != "look") throw new BridgeException("invalid_argument", "kind must be touch or look");
                HarmonyLib.AccessTools.Property(typeof(TalkScene), "touchMode").SetValue(talk, kind == "touch" ? 0 : 1, null);
                HarmonyLib.AccessTools.Method(typeof(TalkScene), "TouchFunc", new Type[] { typeof(string), typeof(Vector3) }, null).Invoke(talk, new object[] { "Head", ownedNpc.HeadPos });
                return ReadNativeInteraction();
            }
            if (op == "game.saved_npc_talk")
            {
                if (action == null || Mode != "roaming") throw new BridgeException("wrong_context", "Requires story roaming");
                foreach (ActionGame.Chara.NPC npc in action.npcList)
                    if (npc != null && !ReferenceEquals(npc, ownedNpc) && npc.mapNo == map && npc.isAction && npc.gameObject.activeInHierarchy)
                    { action.SceneEvent(npc); return Json.Map("status", "starting", "ownedTarget", false, "name", npc.heroine.Name); }
                throw new BridgeException("not_interactable", "No saved NPC is currently available for a normal conversation here");
            }
            if (op == "game.talk_status")
            {
                TalkScene talk = UnityEngine.Object.FindObjectOfType<TalkScene>();
                return Json.Map("ownedTarget", talk != null && ReferenceEquals(talk.targetHeroine, ownedHeroine), "ready", NativeTalkReady(talk));
            }
            if (op == "game.save_probe")
            {
                if (Mode != "roaming" || ownedSave == null) throw new BridgeException("wrong_context", "Requires story roaming");
                string directory = Path.Combine(Paths.GameRootPath, "BepInEx/KK_StoryNPCBridgeCaptures"); Directory.CreateDirectory(directory);
                string filename = "native-save-probe.dat"; int before = ownedSave.heroineList.Count;
                using (var stream = File.Create(Path.Combine(directory, filename)))
                    HarmonyLib.AccessTools.Method(typeof(SaveData), "<Save>m__3", new Type[] { typeof(FileStream) }, null).Invoke(ownedSave, new object[] { stream });
                var probe = new SaveData(); bool loaded = probe.Load(directory + "/", filename); bool found = false;
                foreach (var h in probe.heroineList) if (h.Name == ownedName) found = true;
                return Json.Map("loaded", loaded, "runtimeRoster", before, "serializedRoster", probe.heroineList.Count, "pluginSerialized", found, "runtimeRestored", ownedSave.heroineList.Contains(ownedHeroine), "nativeProfileExists", File.Exists(NativeProfilePath));
            }
            if (op == "game.talk_end")
            {
                TalkScene talk = UnityEngine.Object.FindObjectOfType<TalkScene>();
                if (talk == null || talk.targetHeroine != ownedHeroine) throw new BridgeException("wrong_context", "No native conversation with the bridge NPC");
                var button = HarmonyLib.AccessTools.Field(typeof(TalkScene), "buttonEnd").GetValue(talk) as UnityEngine.UI.Button;
                if (button == null || !button.interactable || !button.gameObject.activeInHierarchy) throw new BridgeException("not_ready", "Continue the introduction before ending conversation");
                button.onClick.Invoke(); return Json.Map("status", "ending");
            }
            if (op == "game.load")
            {
                if (action != null || UnityEngine.Object.FindObjectOfType<TitleScene>() == null)
                    throw new BridgeException("wrong_context", "Load a story save from the title screen");
                string filename = Text(command, "save");
                if (Path.GetFileName(filename) != filename || !filename.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                    throw new BridgeException("invalid_argument", "Use a save filename from UserData/save/game");
                string fullPath = Path.Combine(Paths.GameRootPath, Path.Combine("UserData", Path.Combine("save", Path.Combine("game", filename))));
                if (!File.Exists(fullPath)) throw new BridgeException("not_found", "Story save does not exist");
                Manager.Game.SaveFileName = filename;
                if (!Manager.Game.Instance.saveData.Load(filename)) throw new BridgeException("load_failed", "The game rejected this story save");
                Manager.Scene.Instance.LoadReserve(new Manager.Scene.Data { levelName = "Action", fadeType = Manager.Scene.Data.FadeType.In }, true);
                return Json.Map("status", "loading", "save", filename);
            }
            if (op == "game.map")
            {
                if (action == null || Mode != "roaming") throw new BridgeException("wrong_context", "Map changes require story roaming");
                object value;
                if (!command.TryGetValue("map", out value)) throw new BridgeException("invalid_argument", "map required");
                int destination = MapId(value);
                if (!action.Map.calcGateDic.ContainsKey(destination)) throw new BridgeException("unknown_map", "Map is absent from the game's gate table");
                action.Player.mapNo = destination;
                action.Map.Change(destination, Manager.Scene.Data.FadeType.InOut);
                return Json.Map("status", "loading", "map", destination, "mapInfo", MapReference(destination));
            }
            if (op == "game.advance")
            {
                if (action == null) throw new BridgeException("wrong_context", "No story game is loaded");
                if (nightMenu != null)
                {
                    var button = HarmonyLib.AccessTools.Field(typeof(ActionGame.NightMainMenu), "nextButton").GetValue(nightMenu) as UnityEngine.UI.Button;
                    if (button == null || !button.interactable) throw new BridgeException("not_ready", "The sleep button is not ready");
                    button.onClick.Invoke();
                }
                else
                {
                    if (Mode != "roaming") throw new BridgeException("wrong_context", "Wait for story roaming");
                    action.Cycle.Next();
                }
                return Json.Map("status", "advancing");
            }
            if (op == "game.continue" || op == "game.complete_text")
            {
                Tutorial tutorial = UnityEngine.Object.FindObjectOfType<Tutorial>();
                if (tutorial != null) { tutorial.EndScene(true); return Json.Map("status", "tutorial_closed"); }
                var scenario = action != null && action.AdvScene != null ? action.AdvScene.Scenario : null;
                foreach (ADV.TextScenario candidate in UnityEngine.Object.FindObjectsOfType<ADV.TextScenario>())
                    if (candidate.MessageWindow != null && candidate.MessageWindow.gameObject.activeInHierarchy) { scenario = candidate; break; }
                if (scenario == null || scenario.textController == null || scenario.MessageWindow == null || !scenario.MessageWindow.gameObject.activeInHierarchy)
                    return Json.Map("status", "no_line");
                if (scenario.isChoice) throw new BridgeException("choice_pending", "A native story choice requires a separate selection");
                scenario.textController.ForceCompleteDisplayText();
                if (op == "game.complete_text") return Json.Map("status", "text_complete");
                HarmonyLib.AccessTools.Method(typeof(ADV.TextScenario), "RequestNextLine", Type.EmptyTypes, null).Invoke(scenario, new object[0]);
                return Json.Map("status", "continued");
            }
            if (op == "game.screenshot")
            {
                string directory = Path.Combine(Paths.GameRootPath, "BepInEx/KK_StoryNPCBridgeCaptures");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "bridge-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".png");
                Application.CaptureScreenshot(path, 1);
                return Json.Map("status", "pending", "path", path);
            }
            throw new BridgeException("unsupported_operation", op);
        }
        bool NativeTalkReady(TalkScene talk)
        {
            if (talk == null || nativeTextBusy || SceneBusy || UnityEngine.Object.FindObjectOfType<Tutorial>() != null) return false;
            var canvas = HarmonyLib.AccessTools.Field(typeof(TalkScene), "canvas").GetValue(talk) as Canvas;
            var button = HarmonyLib.AccessTools.Field(typeof(TalkScene), "buttonEnd").GetValue(talk) as UnityEngine.UI.Button;
            return canvas != null && canvas.enabled && canvas.gameObject.activeInHierarchy && button != null && button.interactable && button.gameObject.activeInHierarchy;
        }
    }
}
