using System;
using System.Collections.Generic;
using System.Linq;
using KKAPI.MainGame;
using KK_StoryNPCBridge;

static class HSceneReportsTests
{
    static int assertions;
    static void Check(bool ok, string name) { assertions++; if (!ok) throw new Exception(name); }
    static Dictionary<string, object> Map(object value) { return (Dictionary<string, object>)value; }
    static object[] Array(object value) { return (object[])value; }
    static Plugin Setup()
    {
        GameAPI.InsideHScene = false; UnityEngine.Time.realtimeSinceStartup = 10;
        Manager.Scene.Instance = new Manager.Scene();
        var save = new SaveData(); var bridge = new SaveData.Heroine { Name = "Bridge NPC", schoolClassIndex = 24 };
        save.heroineList.Add(new SaveData.Heroine { Name = "Native NPC" }); save.heroineList.Add(bridge);
        Manager.Game.Instance = new Manager.Game { saveData = save };
        var p = new Plugin { ownedSave = save, previousSave = save, ownedHeroine = bridge }; p.Boot(); return p;
    }
    static HSceneProc Scene(Plugin p, int mode, params SaveData.Heroine[] females)
    {
        var flags = new HFlag { mode = (HFlag.EMode)mode, player = p.ownedSave.player };
        flags.lstHeroine.AddRange(females); if (mode == 3 || mode == 4 || mode == 5) flags.nowAnimationInfo.paramMale.path.assetpath = "";
        return new HSceneProc { flags = flags };
    }
    static Dictionary<string, object> Finish(Plugin p, HSceneProc proc)
    {
        UnityEngine.Time.realtimeSinceStartup = 20; p.Reports.End(proc, proc.flags);
        p.Mode = "roaming"; p.Reports.WorldRefreshed(); return Map(p.Reports.Last);
    }
    static Dictionary<string, object> Person(Dictionary<string, object> report, string id)
    { return Array(report["participants"]).Select(Map).Single(x => (string)Map(x["actor"])["id"] == id); }
    static int Completed(Plugin p) { return p.Events.Count(e => (string)e["kind"] == "game_h_completed"); }
    static void NormalSettlement()
    {
        var p = Setup(); var proc = Scene(p, 2, p.ownedHeroine);
        p.ownedHeroine.favor = 20; p.ownedHeroine.hCount = 40; proc.flags.count.sonyuInside = 2;
        p.Reports.Begin(proc, proc.flags); p.Reports.Begin(proc, proc.flags);
        Check(p.Events.Count == 1, "duplicate start ignored");
        var id = Map(p.Reports.Status())["sessionId"];
        p.ownedHeroine.hCount++; proc.flags.count.sonyuInside = 3; proc.flags.count.sonyuOrg = 1;
        proc.flags.count.selectAreas[1] = 4; proc.flags.count.mouseAction[0, 1] = new HFlag.MouseAction { click = true };
        UnityEngine.Time.realtimeSinceStartup = 25; p.Reports.End(proc, proc.flags); p.Reports.End(proc, proc.flags);
        Check(Completed(p) == 0 && (string)Map(p.Reports.Status())["phase"] == "settling", "native end is not final settlement");
        p.Reports.WorldRefreshed(); Check(p.Reports.Last == null, "event scene blocks settlement");
        p.Mode = "roaming"; p.SceneBusy = true; p.Reports.WorldRefreshed(); Check(p.Reports.Last == null, "fade blocks settlement");
        p.SceneBusy = false; GameAPI.InsideHScene = true; p.Reports.WorldRefreshed(); Check(p.Reports.Last == null, "another native H blocks settlement");
        GameAPI.InsideHScene = false; Manager.Scene.Instance.NowSceneNames = new[] { "H", "HSceneResult" };
        p.Reports.WorldRefreshed(); Check(p.Reports.Last == null, "normal mode alone cannot bypass post-H result scene");
        Manager.Scene.Instance.NowSceneNames = new string[0]; p.ownedHeroine.favor = 27; p.ownedHeroine.lewdness = 4;
        p.Reports.WorldRefreshed(); p.Reports.WorldRefreshed(); var r = Map(p.Reports.Last);
        Check(Completed(p) == 1 && Equals(r["sessionId"], id), "one completion for same session");
        Check((float)r["durationSeconds"] == 15, "duration ends at native callback");
        var person = Person(r, "bridge:main");
        Check((int)Map(person["statsAtNativeEnd"])["favor"] == 20 && (int)Map(person["statsAfterSettlement"])["favor"] == 27, "native and settled attributes remain separate");
        Check((double)Map(person["delta"])["favor"] == 7 && (double)Map(person["delta"])["hCount"] == 1, "cumulative stats are differenced");
        var stats = Map(r["statistics"]);
        Check((int)Map(stats["atEnd"])["sonyuInside"] == 3 && (double)Map(stats["observedDelta"])["sonyuInside"] == 1, "native counter total and observed delta differ");
        Check((int)stats["nativeOrgasmTotal"] == 1 && (int)stats["nativeFinishTotal"] == 3, "native helper aggregates");
        Check(Array(Map(stats["atEnd"])["mouseActions"]).Length == 1, "mouse history is sparse flags");
        proc.flags.count.sonyuInside = 99; proc.flags.count.selectAreas[1] = 99; p.ownedHeroine.favor = 99;
        Check((int)Map(stats["atEnd"])["sonyuInside"] == 3 && (double)Array(Map(stats["atEnd"])["selectAreas"])[1] == 4, "published report is detached from live counters and arrays");
        Check((int)Map(person["statsAfterSettlement"])["favor"] == 27, "published attributes are immutable snapshots");
        Check(p.Events.All(e => e["target"] == null && e["generation"] == null), "H events keep game event scope");
        Json.Parse(Json.Stringify(r)); Check(true, "full report JSON roundtrip"); p.Reports.StopObserving();
    }
    static void IndependentAndGroup()
    {
        foreach (int mode in new[] { 3, 4, 5 })
        {
            var p = Setup(); var other = p.ownedSave.heroineList[0]; other.Name = p.ownedHeroine.Name;
            var proc = mode == 5 ? Scene(p, mode, other, p.ownedHeroine) : Scene(p, mode, other);
            p.Reports.Begin(proc, proc.flags); var unrelated = new HFlag { mode = (HFlag.EMode)mode };
            p.Observe(unrelated, "Orgasm"); p.Observe(proc.flags, "Orgasm", false);
            p.Observe(proc.flags, "Orgasm"); p.Observe(proc.flags, "Orgasm"); p.Observe(proc.flags, "Orgasm_A"); p.Observe(proc.flags, "WLoop"); p.Observe(proc.flags, "Orgasm");
            var r = Finish(p, proc); var observations = Map(r["observations"]);
            Check(!(bool)r["playerParticipated"], "context player excluded from independent mode " + mode);
            Check(!Array(r["participants"]).Select(Map).Any(x => (string)Map(x["actor"])["id"] == "player:main"), "no phantom player participant");
            Check((bool)r["bridgeParticipated"] == (mode == 5), "same-name NPC cannot impersonate bridge");
            Check(Map(r["statistics"])["nativeOrgasmTotal"] == null, "independent native total unavailable");
            if (mode == 3) Check((int)observations["masturbationClimaxEntries"] == 2, "deduplicated independent state entries");
            if (mode == 5) Check((int)observations["lesbianClimaxEntries"] == 2 && observations["perParticipant"] == null, "lesbian entries stay aggregate");
            p.Reports.StopObserving();
        }
        foreach (int mode in new[] { 6, 7, 8, 9 })
        {
            var p = Setup(); var proc = mode < 8 ? Scene(p, mode, p.ownedSave.heroineList[0], p.ownedHeroine) : Scene(p, mode, p.ownedHeroine);
            if (mode >= 8) proc.male1 = new ChaControl();
            p.Reports.Begin(proc, proc.flags); var r = Finish(p, proc);
            Check((int)r["participantCount"] == 3 && (bool)r["bridgeParticipated"], "three native slots counted " + mode);
            Check(Map(r["statistics"])["perParticipant"] == null, "group counters not assigned to bridge");
            if (mode >= 8) Check(Array(r["participants"]).Select(Map).Count(x => (string)Map(x["actor"])["role"] == "unresolved_model") == 1, "unknown second male remains unresolved");
            p.Reports.StopObserving();
        }
    }
    static void ExitsAndIdentity()
    {
        var p = Setup(); var a = Scene(p, 2, p.ownedHeroine); p.Reports.Begin(a, a.flags);
        a.Destroyed = true; UnityEngine.SceneManagement.SceneManager.Unload(); UnityEngine.SceneManagement.SceneManager.Unload();
        var r = Map(p.Reports.Last); Check(Completed(p) == 1 && (string)r["state"] == "aborted", "abnormal unload completes once");
        Check(Map(r["statistics"])["atEnd"] == null && Person(r, "bridge:main")["delta"] == null, "abnormal exit cannot fabricate end or settled data");
        p.Reports.StopObserving(); p = Setup(); a = Scene(p, 2, p.ownedHeroine); p.Reports.Begin(a, a.flags); p.Reports.End(a, a.flags);
        p.Reports.WorldChanged(); p.Reports.WorldChanged(); Check(Completed(p) == 1 && p.Reports.Last == null, "world change clears last report and deduplicates completion");
        r = Map(p.Events.Last()["data"]); Check((string)r["world"] == "test-world" && Person(r, "bridge:main")["delta"] == null, "old-world event never reads new save stats");
        p.Reports.StopObserving(); p = Setup(); a = Scene(p, 2, p.ownedHeroine); p.Reports.Begin(a, a.flags); p.Reports.End(a, a.flags);
        p.ownedGeneration = "reloaded-in-same-save-instance"; p.Mode = "roaming"; p.Reports.WorldRefreshed();
        Check(Completed(p) == 1 && p.Reports.Last == null && Map(Map(p.Events.Last()["data"])["settlement"])["state"].Equals("unavailable"), "in-place save load cannot settle using the new generation");
        p.Reports.StopObserving(); p = Setup(); a = Scene(p, 2, p.ownedHeroine); p.Reports.Begin(a, a.flags); p.Reports.End(a, a.flags);
        var b = Scene(p, 2, p.ownedSave.heroineList[0]); p.Reports.Begin(b, b.flags); p.Reports.End(a, a.flags);
        Check(Completed(p) == 1 && Map(p.Reports.Status())["phase"].Equals("active"), "late end from old proc cannot end new run");
        r = Finish(p, b); Check(Completed(p) == 2 && !(bool)r["bridgeParticipated"], "next scene owns its own report");
        p.Reports.StopObserving(); p = Setup(); a = Scene(p, 2, p.ownedHeroine); p.Reports.Begin(a, a.flags);
        p.Reports.StopObserving(); p.Reports.StopObserving(); Check(Completed(p) == 1, "unload is idempotent");
        p.Reports.Begin(a, a.flags); Check(Completed(p) == 1, "stopped observer ignores lifecycle callbacks");
        p = Setup(); var copy = new SaveData.Heroine { Name = p.ownedHeroine.Name }; a = Scene(p, 2, copy);
        a.flags.player = new SaveData.Player(); a.flags.isFreeH = true; p.Reports.Begin(a, a.flags); p.Reports.End(a, a.flags);
        a.Destroyed = true; p.Mode = "menu"; Manager.Scene.Instance.LoadSceneName = "FreeH"; p.Reports.WorldRefreshed(); r = Map(p.Reports.Last);
        Check(!(bool)r["bridgeParticipated"] && Map(r["settlement"])["state"].Equals("not_applicable"), "free-H copies do not reuse story identity or settled stats");
        Check(Array(r["participants"]).Select(Map).Any(x => Map(x["actor"])["role"].Equals("scene_player")), "free-H player stays scene-local"); p.Reports.StopObserving();
    }
    static void MissingAndLateData()
    {
        var hidden = Setup(); var hiddenProc = Scene(hidden, 0, hidden.ownedHeroine);
        hiddenProc.flags.nowAnimationInfo.paramMale.path.assetpath = ""; hidden.Reports.Begin(hiddenProc, hiddenProc.flags);
        var hiddenReport = Finish(hidden, hiddenProc);
        Check((bool)hiddenReport["playerParticipated"] && (int)hiddenReport["participantCount"] == 2, "foreplay player participates even without a male animation");
        hidden.Reports.StopObserving();
        var p = Setup(); var a = Scene(p, 2, p.ownedHeroine); a.flags.count.sonyuInside = 8;
        p.Reports.Begin(a, a.flags); a.flags.count.sonyuInside = 2; a.flags.count.kiss = 10000; a.flags.count.selectAreas[0] = float.NaN; a.flags.gaugeFemale = float.PositiveInfinity;
        var newcomer = new SaveData.Heroine { Name = "Late participant" }; a.flags.lstHeroine.Add(newcomer); p.Observe(a.flags, "LateState");
        var r = Finish(p, a); Check(Map(Map(r["statistics"])["observedDelta"])["sonyuInside"] == null, "counter reset is not negative scene activity");
        Check(Map(Map(r["statistics"])["observedDelta"])["kiss"] == null && Array(Map(r["statistics"])["saturatedCounters"]).Contains("kiss"), "saturated counters cannot claim exact observed event deltas");
        var late = Array(r["participants"]).Select(Map).Single(x => Map(x["actor"])["name"].Equals("Late participant"));
        Check(late["statsAtStart"] == null && late["delta"] == null, "late participant cannot invent scene-start baseline");
        Check(Map(r["nativeEndState"])["gaugeFemale"] == null, "nonfinite native values are unavailable"); Json.Parse(Json.Stringify(r));
        p.Reports.StopObserving(); p = Setup(); a = Scene(p, 3, p.ownedHeroine); p.ownedSave.player.hcount = 12;
        p.Reports.Begin(a, a.flags); a.flags.mode = HFlag.EMode.sonyu; a.flags.nowAnimationInfo.paramMale.path.assetpath = "male"; p.ownedSave.player.hcount = 14;
        p.Observe(a.flags, "SLoop"); r = Finish(p, a);
        Check((int)Map(Person(r, "player:main")["statsAtStart"])["hCount"] == 12, "player joining later uses captured start context baseline");
        Check((double)Map(Person(r, "player:main")["delta"])["hCount"] == 2, "player late participation delta is not reset at joining"); p.Reports.StopObserving();
    }
    static void AllNativeFields()
    {
        var p = Setup(); var proc = Scene(p, 2, p.ownedHeroine); var count = proc.flags.count;
        var fields = typeof(HFlag.Count).GetFields(); int value = 1;
        foreach (var field in fields)
        {
            if (field.FieldType == typeof(int)) field.SetValue(count, value++);
            if (field.FieldType == typeof(float)) field.SetValue(count, 1.25f);
            if (field.FieldType == typeof(float[]))
            {
                var array = (float[])field.GetValue(count);
                for (int i = 0; i < array.Length; i++) array[i] = i + .5f;
            }
        }
        p.Reports.Begin(proc, proc.flags);
        foreach (var field in fields)
        {
            if (field.FieldType == typeof(int)) field.SetValue(count, (int)field.GetValue(count) + 17);
            if (field.FieldType == typeof(float)) field.SetValue(count, 1.5f);
            if (field.FieldType == typeof(bool)) field.SetValue(count, true);
            if (field.FieldType == typeof(float[]))
            {
                var array = (float[])field.GetValue(count);
                for (int i = 0; i < array.Length; i++) array[i] += 5;
            }
        }
        count.mouseAction[9, 6] = new HFlag.MouseAction { click = true, drag = true };
        var stats = Map(Finish(p, proc)["statistics"]); var end = Map(stats["atEnd"]); var delta = Map(stats["observedDelta"]);
        Check(end.Count == fields.Length, "every native Count field has one report representation");
        foreach (var field in fields)
        {
            if (field.FieldType == typeof(int))
                Check(Equals(end[field.Name], field.GetValue(count)) && (double)delta[field.Name] == 17, "integer native field " + field.Name);
            if (field.FieldType == typeof(float))
                Check((double)end[field.Name] == 1.5 && (double)delta[field.Name] == .25, "float native field " + field.Name);
            if (field.FieldType == typeof(bool))
                Check((bool)end[field.Name] && !delta.ContainsKey(field.Name), "latched native flag " + field.Name);
            if (field.FieldType == typeof(float[]))
                Check(Array(end[field.Name]).Select(Convert.ToDouble).SequenceEqual(((float[])field.GetValue(count)).Select(x => (double)x))
                    && Array(delta[field.Name]).All(x => (double)x == 5), "native accumulation array " + field.Name);
        }
        var cell = Map(Array(end["mouseActions"]).Single());
        Check((int)cell["item"] == 9 && (int)cell["area"] == 6 && (bool)cell["click"] && (bool)cell["drag"], "sparse mouse matrix preserves both index boundaries and flags");
        p.Reports.StopObserving();
    }
    public static void Main()
    {
        NormalSettlement(); IndependentAndGroup(); ExitsAndIdentity(); MissingAndLateData(); AllNativeFields();
        Console.WriteLine("PASS: " + assertions + " H-report assertions using production module and simulated native objects. Unity/native lifecycle acceptance is separate.");
    }
}
