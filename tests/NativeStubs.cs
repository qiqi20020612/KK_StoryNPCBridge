// Data-only host doubles. They do not emulate native H execution or settlement.
using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) { return (ReferenceEquals(a, null) || a.Destroyed) ? ReferenceEquals(b, null) || b.Destroyed : ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object o) { return ReferenceEquals(this, o); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    public class MonoBehaviour : Object { }
    public static class Time { public static float realtimeSinceStartup; }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { }
    public static class SceneManager
    {
        public static event Action<Scene> sceneUnloaded;
        public static void Unload() { if (sceneUnloaded != null) sceneUnloaded(new Scene()); }
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static FieldInfo Field(Type t, string name) { return t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
    }
}
namespace KKAPI.MainGame
{
    public class GameCustomFunctionController : UnityEngine.MonoBehaviour
    {
        protected virtual void OnStartH(UnityEngine.MonoBehaviour proc, HFlag flags, bool vr) { }
        protected virtual void OnEndH(UnityEngine.MonoBehaviour proc, HFlag flags, bool vr) { }
        public void Begin(UnityEngine.MonoBehaviour proc, HFlag flags) { OnStartH(proc, flags, false); }
        public void End(UnityEngine.MonoBehaviour proc, HFlag flags) { OnEndH(proc, flags, false); }
    }
    public static class GameAPI { public static bool InsideHScene; }
}
public class SaveData
{
    public class CharaData { public string Name = "NPC"; public int schoolClass = 1, schoolClassIndex; public ChaControl chaCtrl; }
    public class Heroine : CharaData
    {
        public int favor, intimacy, lewdness, anger, hCount, relation, countNamaInsert, countHoushi;
        public bool isGirlfriend, isKiss, isVirgin = true, isAnalVirgin = true;
        public float houshiExp, countKokanH, countAnalH;
        public float[] hAreaExps = new float[6], massageExps = new float[2];
    }
    public class Player : CharaData { public int hcount, orgCount, hPeopleCount, hentai; }
    public Player player = new Player { Name = "Player" };
    public List<Heroine> heroineList = new List<Heroine>();
}
public class ChaControl : UnityEngine.Object
{
    public class Status { public int coordinateType; public byte[] clothesState = new byte[9]; }
    public class Param { public string lastname = "", firstname = "Model"; }
    public bool loadEnd = true, visibleAll = true;
    public Status fileStatus = new Status();
    public Param fileParam = new Param();
}
public class HSceneProc : UnityEngine.MonoBehaviour
{
    public class PathName { public string assetpath = "male-animation"; }
    public class MaleParameter { public PathName path = new PathName(); }
    public class AnimationListInfo { public int id; public HFlag.EMode mode; public string nameAnimation = "Native animation"; public MaleParameter paramMale = new MaleParameter(); }
    public HFlag flags;
    public ChaControl male1;
    public int finishADV;
    public class Parameters { public float[] aibus = new float[5], sonyus = new float[2]; public float houshi; }
    public Parameters addParameter = new Parameters();
}
public class HFlag : UnityEngine.Object
{
    public enum EMode { none = -1, aibu, houshi, sonyu, masturbation, peeping, lesbian, houshi3P, sonyu3P, houshi3PMMF, sonyu3PMMF }
    public enum FinishKind { none, inside, outside, orgW, sameW, orgS, sameS }
    public class MouseAction { public bool click, drag; }
    public class Count
    {
        public int aibuOrg, kiss, notKiss, notPowerful, dontTouchAnal, dontTouchMassage;
        public int houshiOutside, houshiInside, handFinish, nameFinish, kuwaeFinish, paizuriFinish, paizurinameFinish, paizurikuwaeFinish;
        public int houshiDrink, houshiVomit, splash, sonyuCondomInside, sonyuOrg, sonyuInside, sonyuOutside, sonyuSame, sonyuCondomSame, sonyuTare;
        public int sonyuAnalCondomInside, sonyuAnalOrg, sonyuAnalInside, sonyuAnalOutside, sonyuAnalSame, sonyuAnalCondomSame, sonyuAnalTare;
        public int notCondomPlay, notAnalPlay, sonyuKokanPlay, sonyuAnalPlay;
        public bool isInsertKokan, isInsertAnal, isNameInsertKokan, isHoushiPlay, isInsertKokanVoiceCondition, isInsertAnalVoiceCondition;
        public float[] selectMassages = new float[2], selectAreas = new float[6], selectAreasGlobal = new float[6];
        public float selectHobby;
        public MouseAction[,] mouseAction = new MouseAction[10, 7];
    }
    public Count count = new Count();
    public EMode mode;
    public bool isFreeH, isCondom, isAnalPlay, isHSceneEnd, isUseImmediatelyFinishButton;
    public int numEnd;
    public float gaugeFemale, gaugeMale;
    public FinishKind finish;
    public HSceneProc.AnimationListInfo nowAnimationInfo = new HSceneProc.AnimationListInfo();
    public string nowAnimStateName = "WLoop";
    public List<SaveData.Heroine> lstHeroine = new List<SaveData.Heroine>();
    public SaveData.Player player;
}
namespace Manager
{
    public class Game
    {
        public static Game Instance;
        public static bool IsInstance() { return Instance != null; }
        public SaveData saveData;
    }
    public class Scene
    {
        public static Scene Instance = new Scene();
        public static bool IsInstance() { return Instance != null; }
        public string LoadSceneName = "Action";
        public string[] NowSceneNames = new string[0];
    }
}
namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        public const string ActorId = "bridge:main";
        internal static Plugin Current;
        internal string world = "test-world", ownedGeneration = "test-generation", Mode = "event";
        internal SaveData ownedSave, previousSave;
        internal SaveData.Heroine ownedHeroine;
        internal bool SceneBusy;
        internal class Log { internal void LogWarning(string value) { throw new Exception(value); } }
        internal Log Logger = new Log();
        internal readonly List<Dictionary<string, object>> Events = new List<Dictionary<string, object>>();
        internal void AddEvent(string kind, object data) { Events.Add(Json.Map("seq", Events.Count + 1, "kind", kind, "target", null, "generation", null, "data", data)); }
        internal object ReadGameTime() { return Json.Map("available", true, "world", world); }
        static bool FaceReady(ChaControl c) { return false; }
        static object ReadFace(ChaControl c) { return null; }
        internal HSceneReportController Reports { get { return hSceneReports; } }
        internal void Boot()
        {
            Current = this; hSceneReports = new HSceneReportController();
            typeof(HSceneReportController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hSceneReports, null);
        }
        internal void Observe(HFlag f, string state, bool success = true) { HSceneAnimationChanged(f, state, success); }
    }
}
