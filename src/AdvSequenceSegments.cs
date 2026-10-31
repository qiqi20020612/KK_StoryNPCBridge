using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        sealed class AdvSequenceSegment
        {
            internal string Speaker, Text, Motion;
            internal double Wait;
            internal bool Recorded;
            internal Dictionary<string, object> Expression;
            internal Dictionary<string, object> NativeMotion;
            internal object Read()
            {
                var value = Json.Map("speaker", Speaker, "text", Text, "waitSeconds", Wait);
                if (Expression != null && Expression.Count > 0) value["expression"] = Expression;
                if (Motion != null) value["motion"] = Motion;
                if (NativeMotion != null) value["nativeMotion"] = NativeMotion;
                return value;
            }
        }
        List<AdvSequenceSegment> ParseAdvSequence(Dictionary<string, object> command)
        {
            object value;
            var input = command.TryGetValue("segments", out value) ? value as IList : null;
            if (input == null || input.Count < 1 || input.Count > 32)
                throw new BridgeException("invalid_argument", "segments must contain 1..32 objects");
            var result = new List<AdvSequenceSegment>(); int total = 0;
            foreach (object item in input)
            {
                var data = item as Dictionary<string, object>;
                if (data == null) throw new BridgeException("invalid_argument", "Each segment must be an object");
                foreach (string key in data.Keys)
                    if (key != "speaker" && key != "text" && key != "expression" && key != "motion" && key != "waitSeconds")
                        throw new BridgeException("unsupported_field", "Unsupported segment field: " + key);
                var segment = new AdvSequenceSegment { Speaker = OptionalText(data, "speaker", "npc"), Text = OptionalText(data, "text", "") };
                if (segment.Speaker != "npc" && segment.Speaker != "player" && segment.Speaker != "narrator")
                    throw new BridgeException("invalid_argument", "speaker must be npc, player or narrator");
                if (segment.Text.Length > 0) ValidateDialogueText(segment.Text);
                total += segment.Text.Length;
                if (total > 8000) throw new BridgeException("invalid_argument", "Sequence text exceeds 8000 UTF-16 units");
                if (data.ContainsKey("expression"))
                { segment.Expression = NormalizeFace(Object(data, "expression")); ValidateFace(segment.Expression); }
                if (data.TryGetValue("motion", out value)) segment.Motion = AnimationState(value);
                if (data.TryGetValue("waitSeconds", out value)) segment.Wait = Number(value, 0, 30, "waitSeconds");
                if (segment.Text.Length == 0 && segment.Motion == null && (segment.Expression == null || segment.Expression.Count == 0) && segment.Wait == 0)
                    throw new BridgeException("invalid_argument", "A segment needs text, expression, motion or a positive waitSeconds");
                result.Add(segment);
            }
            return result;
        }
        static string[] SequenceExpressionArgs(Dictionary<string, object> changes)
        {
            // Empty native arguments preserve fields omitted from the normalized expression.
            string[] keys = { "eyebrow", "eyes", "mouth", "eyebrowOpen", "eyesOpen", "mouthOpen", "gaze", "blush", "highlight", "tears", "blink" };
            var args = new string[12]; args[0] = "-2";
            for (int i = 0; i < keys.Length; i++)
            {
                object value; args[i + 1] = keys[i].Length > 0 && changes.TryGetValue(keys[i], out value)
                    ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
            }
            return args;
        }
        List<ADV.Program.Transfer> BuildAdvSequenceTransfers()
        {
            var run = advSequence;
            if (run.Operation == "play_adv_motion") run.MotionSnapshot = CaptureAdvMotionAnimator();
            BeginAdvPages();
            var list = ADV.Program.Transfer.NewList(false, false);
            for (int i = 0; i < run.Segments.Count; i++)
            {
                var segment = run.Segments[i]; string index = (i + 1).ToString(CultureInfo.InvariantCulture);
                list.Add(ADV.Program.Transfer.Create(false, ADV.Command.Wait, "0", AdvSequenceMarker, run.Id, index, "segment"));
                object fixedMouth;
                if (segment.Expression != null && segment.Expression.TryGetValue("mouthFixed", out fixedMouth))
                    list.Add(ADV.Program.Transfer.Create(true, ADV.Command.CharaFixMouth, "-2", Convert.ToString(fixedMouth, CultureInfo.InvariantCulture)));
                if (segment.Expression != null && segment.Expression.Count > 0)
                    list.Add(ADV.Program.Transfer.Create(true, ADV.Command.CharaExpression, SequenceExpressionArgs(segment.Expression)));
                if (segment.Motion != null)
                    list.Add(ADV.Program.Transfer.Create(true, ADV.Command.CharaMotion, "-2", segment.Motion, "", "", "", "", "", "", "", "", "0"));
                if (segment.NativeMotion != null)
                    list.Add(ADV.Program.Transfer.Create(true, ADV.Command.CharaMotion, AdvMotionArgs(segment.NativeMotion)));
                string speaker = segment.Speaker == "npc" ? "[H名]" : segment.Speaker == "player" ? Manager.Game.Instance.saveData.player.Name : "";
                if (segment.Text.Length > 0) AddAdvText(list, speaker, segment.Text);
                if (segment.Wait > 0)
                    list.Add(ADV.Program.Transfer.Create(false, ADV.Command.Wait, segment.Wait.ToString(CultureInfo.InvariantCulture), AdvSequenceMarker, run.Id, index, "delay"));
            }
            run.PageCount = advPageCount;
            list.Add(ADV.Program.Transfer.Create(false, ADV.Command.Tag, run.EndTag));
            list.Add(ADV.Program.Transfer.Create(false, ADV.Command.Wait, DialogueWaitMarker, AdvSequenceMarker, run.Id, "0", "end"));
            list.Add(ADV.Program.Transfer.Close());
            run.FaceOverride = faceOverride; run.AnimationOverride = animationOverride; run.VisualsCaptured = true;
            run.FaceActor = ownedCha; run.FaceSnapshot = ReadFace(ownedCha);
            faceOverride = false; animationOverride = false;
            run.State = "running"; run.StartedUtc = DateTime.UtcNow.ToString("o"); AddEvent("adv_sequence_started", ReadAdvSequence());
            return list;
        }
    }
}
