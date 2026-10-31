using System;
using System.Collections.Generic;
using HarmonyLib;
using Illusion.Component.Correct;
using Illusion.Component.Correct.Process;
using RootMotion.FinalIK;
using UnityEngine;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        sealed class AdvMotionCorrection
        {
            internal BaseData Data;
            internal BaseProcess Process;
            internal Transform Bone;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal bool Enabled, NoRestore;
            internal BaseProcess.Type Type;
        }
        sealed class AdvMotionSnapshot
        {
            internal ChaControl Actor;
            internal Animator Animator;
            internal RuntimeAnimatorController Controller;
            internal AnimatorStateInfo[] States;
            internal float[] Weights;
            internal float Speed;
            internal readonly Dictionary<string, object> Parameters = new Dictionary<string, object>();
            internal ADV.CharaData Character;
            internal MotionIK IK;
            internal MotionIKData IKData;
            internal bool IKEnabled;
            internal string IKBundle, IKAsset, ShakeBundle, ShakeAsset;
            internal readonly List<AdvMotionCorrection> Corrections = new List<AdvMotionCorrection>();
            internal readonly Dictionary<IKEffector, Vector2> Effectors = new Dictionary<IKEffector, Vector2>();
            internal readonly Dictionary<IKConstraintBend, float> Bends = new Dictionary<IKConstraintBend, float>();
            internal YureCtrl Shake;
            internal Dictionary<string, YureCtrl.Info> ShakeInfo;
            internal bool ShakeInitialized;
            internal bool[] ShakeActive, ShakeDefaults;
            internal YureCtrl.BreastShapeInfo[] Shapes, DefaultShapes;
            internal bool?[] DynamicBones;
            internal bool[,] ShapeMask;
        }
        AdvMotionSnapshot CaptureAdvMotionAnimator()
        {
            var animator = ownedCha.animBody;
            var snapshot = new AdvMotionSnapshot { Actor = ownedCha, Animator = animator, Controller = animator.runtimeAnimatorController, Speed = animator.speed,
                States = new AnimatorStateInfo[animator.layerCount], Weights = new float[animator.layerCount] };
            for (int i = 0; i < animator.layerCount; i++)
            { snapshot.States[i] = animator.GetCurrentAnimatorStateInfo(i); snapshot.Weights[i] = animator.GetLayerWeight(i); }
            foreach (var parameter in animator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Float) snapshot.Parameters[parameter.name] = animator.GetFloat(parameter.name);
                else if (parameter.type == AnimatorControllerParameterType.Int) snapshot.Parameters[parameter.name] = animator.GetInteger(parameter.name);
                else if (parameter.type == AnimatorControllerParameterType.Bool) snapshot.Parameters[parameter.name] = animator.GetBool(parameter.name);
            }
            return snapshot;
        }
        static YureCtrl.BreastShapeInfo[] CopyMotionShapes(YureCtrl.BreastShapeInfo[] source)
        {
            var copy = new YureCtrl.BreastShapeInfo[source.Length];
            for (int i = 0; i < source.Length; i++) copy[i] = new YureCtrl.BreastShapeInfo { left = source[i].left, right = source[i].right };
            return copy;
        }
        static void CaptureMotionCorrection(AdvMotionSnapshot snapshot, BaseData data)
        {
            if (data == null) return;
            foreach (var correction in snapshot.Corrections) if (correction.Data == data) return;
            var process = data.GetComponent<BaseProcess>();
            snapshot.Corrections.Add(new AdvMotionCorrection { Data = data, Bone = data.bone, Position = data.pos, Rotation = data.rot,
                Process = process, Enabled = process != null && process.enabled, NoRestore = process != null && process.noRestore,
                Type = process == null ? default(BaseProcess.Type) : process.type });
        }
        void CaptureAdvMotionNative(AdvSequenceRun run)
        {
            var snapshot = run.MotionSnapshot;
            if (snapshot == null || snapshot.Character != null) return;
            foreach (var character in run.Scenario.commandController.Characters.Values)
                if (ReferenceEquals(character.data, ownedHeroine)) { snapshot.Character = character; break; }
            if (snapshot.Character == null) throw new InvalidOperationException("The native ADV character was not initialized");
            var ik = snapshot.Character.ikMotion;
            snapshot.IK = ik.motionIK; snapshot.IKData = snapshot.IK.data; snapshot.IKEnabled = snapshot.IK.enabled;
            snapshot.IKBundle = ik.bundle; snapshot.IKAsset = ik.asset;
            // TalkScene can change its Animator independently of IK. Capture actual corrections rather than guessing an IK state from a clip name.
            if (snapshot.IK.frameCorrect != null) foreach (var info in snapshot.IK.frameCorrect.list) CaptureMotionCorrection(snapshot, info.data);
            if (snapshot.IK.ikCorrect != null) foreach (var info in snapshot.IK.ikCorrect.list) CaptureMotionCorrection(snapshot, info.data);
            foreach (var pair in snapshot.IK.ikTargetPairs)
            {
                snapshot.Effectors[pair.effector] = new Vector2(pair.effector.positionWeight, pair.effector.rotationWeight);
                if (pair.effector.target != null) CaptureMotionCorrection(snapshot, pair.effector.target.GetComponent<BaseData>());
                if (pair.bend != null)
                {
                    snapshot.Bends[pair.bend] = pair.bend.weight;
                    if (pair.bend.bendGoal != null) CaptureMotionCorrection(snapshot, pair.bend.bendGoal.GetComponent<BaseData>());
                }
            }
            // Native LoadData mutates the data object. Keep the original object out of the transient command.
            AccessTools.Property(typeof(MotionIK), "data").SetValue(snapshot.IK, snapshot.IKData.Copy(), null);
            var shake = snapshot.Character.yureMotion;
            snapshot.ShakeBundle = shake.bundle; snapshot.ShakeAsset = shake.asset; snapshot.Shake = shake.yureCtrl;
            snapshot.ShakeInfo = new Dictionary<string, YureCtrl.Info>(snapshot.Shake.dicInfo);
            snapshot.ShakeInitialized = snapshot.Shake.isInit;
            snapshot.ShakeActive = (bool[])snapshot.Shake.isActives.Clone();
            snapshot.ShakeDefaults = (bool[])((bool[])AccessTools.Field(typeof(YureCtrl), "yureEnableActives").GetValue(snapshot.Shake)).Clone();
            snapshot.Shapes = CopyMotionShapes(snapshot.Shake.breastShapes);
            snapshot.DefaultShapes = CopyMotionShapes((YureCtrl.BreastShapeInfo[])AccessTools.Field(typeof(YureCtrl), "breastShapeEnables").GetValue(snapshot.Shake));
            snapshot.DynamicBones = new bool?[3];
            for (int i = 0; i < snapshot.DynamicBones.Length; i++)
            { var bone = snapshot.Actor.getDynamicBoneBust((ChaInfo.DynamicBoneKind)i); if (bone != null) snapshot.DynamicBones[i] = bone.enabled; }
            snapshot.ShapeMask = (bool[,])snapshot.Actor.chaFile.status.disableBustShapeMask.Clone();
        }
        object ReadAdvMotionActual(AdvSequenceRun run)
        {
            var snapshot = run.MotionSnapshot;
            if (snapshot == null || snapshot.Animator == null || snapshot.Actor == null) return null;
            var animator = snapshot.Animator; var clips = new List<object>();
            foreach (var item in animator.GetCurrentAnimatorClipInfo(0)) clips.Add(Json.Map("name", item.clip.name, "weight", item.weight));
            var bones = new List<object>(); var masks = new List<object>();
            for (int i = 0; i < 3; i++) { var bone = snapshot.Actor.getDynamicBoneBust((ChaInfo.DynamicBoneKind)i); bones.Add(bone == null ? null : (object)bone.enabled); }
            var mask = snapshot.Actor.chaFile.status.disableBustShapeMask;
            for (int i = 0; i < mask.GetLength(0); i++) { var row = new List<object>(); for (int j = 0; j < mask.GetLength(1); j++) row.Add(mask[i, j]); masks.Add(row); }
            return Json.Map("controller", animator.runtimeAnimatorController == null ? null : animator.runtimeAnimatorController.name,
                "stateHash", animator.GetCurrentAnimatorStateInfo(0).shortNameHash, "layerNo", 0, "clips", clips,
                "ikEnabled", snapshot.IK == null ? null : (object)snapshot.IK.enabled,
                "ikStates", snapshot.IK == null ? null : snapshot.IK.stateNames, "dynamicBones", bones, "bodyShapeMask", masks);
        }
        void RestoreAdvMotion(AdvSequenceRun run)
        {
            var snapshot = run.MotionSnapshot; if (snapshot == null) return;
            run.MotionSnapshot = null;
            // A native event or another world owns its new model. Never restore into that host.
            if (run.Generation != ownedGeneration || run.World != world || NativeEventBusy || run.Talk != currentTalk || snapshot.Actor != ownedCha || snapshot.Animator == null)
            { run.MotionRestore = "host_changed"; return; }
            try
            {
                var animator = snapshot.Animator;
                animator.runtimeAnimatorController = snapshot.Controller;
                foreach (var parameter in snapshot.Parameters)
                {
                    if (parameter.Value is float) animator.SetFloat(parameter.Key, (float)parameter.Value);
                    else if (parameter.Value is int) animator.SetInteger(parameter.Key, (int)parameter.Value);
                    else animator.SetBool(parameter.Key, (bool)parameter.Value);
                }
                for (int i = 0; i < snapshot.States.Length; i++)
                {
                    animator.SetLayerWeight(i, snapshot.Weights[i]);
                    animator.Play(snapshot.States[i].fullPathHash, i, snapshot.States[i].normalizedTime);
                }
                animator.speed = snapshot.Speed; animator.Update(0);
                if (snapshot.Character != null)
                {
                    var ik = snapshot.Character.ikMotion; ik.bundle = snapshot.IKBundle; ik.asset = snapshot.IKAsset;
                    AccessTools.Property(typeof(MotionIK), "data").SetValue(snapshot.IK, snapshot.IKData, null);
                    AccessTools.Property(typeof(MotionIK), "enabled").SetValue(snapshot.IK, snapshot.IKEnabled, null);
                    foreach (var item in snapshot.Effectors) { item.Key.positionWeight = item.Value.x; item.Key.rotationWeight = item.Value.y; }
                    foreach (var item in snapshot.Bends) item.Key.weight = item.Value;
                    foreach (var correction in snapshot.Corrections)
                    {
                        correction.Data.bone = correction.Bone; correction.Data.pos = correction.Position; correction.Data.rot = correction.Rotation;
                        if (correction.Process != null)
                        { correction.Process.type = correction.Type; correction.Process.noRestore = correction.NoRestore; correction.Process.enabled = correction.Enabled; }
                    }
                    var shake = snapshot.Character.yureMotion; shake.bundle = snapshot.ShakeBundle; shake.asset = snapshot.ShakeAsset;
                    snapshot.Shake.dicInfo.Clear(); foreach (var item in snapshot.ShakeInfo) snapshot.Shake.dicInfo.Add(item.Key, item.Value);
                    snapshot.Shake.isInit = snapshot.ShakeInitialized;
                    Array.Copy(snapshot.ShakeActive, snapshot.Shake.isActives, snapshot.ShakeActive.Length);
                    AccessTools.Field(typeof(YureCtrl), "yureEnableActives").SetValue(snapshot.Shake, snapshot.ShakeDefaults);
                    snapshot.Shake.breastShapes = snapshot.Shapes;
                    AccessTools.Field(typeof(YureCtrl), "breastShapeEnables").SetValue(snapshot.Shake, snapshot.DefaultShapes);
                    for (int i = 0; i < snapshot.DynamicBones.Length; i++) if (snapshot.DynamicBones[i].HasValue) snapshot.Actor.playDynamicBoneBust(i, snapshot.DynamicBones[i].Value);
                    for (int i = 0; i < snapshot.ShapeMask.GetLength(0); i++)
                        for (int j = 0; j < snapshot.ShapeMask.GetLength(1); j++) snapshot.Actor.DisableShapeBodyID(i, j, snapshot.ShapeMask[i, j]);
                }
                run.MotionRestore = "restored";
            }
            catch (Exception e)
            {
                run.MotionRestore = "failed"; run.State = "failed"; run.Reason = "motion_restore_failed";
                run.Error = "Motion restoration failed: " + e.GetBaseException().Message;
                Logger.LogError(run.Error);
            }
        }
    }
}
