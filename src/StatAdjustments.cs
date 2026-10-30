using System;
using System.Collections.Generic;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        const int StatAdjustmentLimit = 4096;
        const int StatMutationIdLength = 128;
        sealed class StatAdjustment
        {
            public Dictionary<string, object> Deltas, Result;
            public string Error = "The previous attempt did not complete; inspect the actor before issuing a new mutationId.";
        }
        readonly Dictionary<string, StatAdjustment> statAdjustments = new Dictionary<string, StatAdjustment>(StringComparer.Ordinal);
        string statAdjustmentGeneration;

        static object ReadStatAdjustmentPolicy()
        {
            return Json.Map("operation", "adjust_stats", "deltaMin", int.MinValue, "deltaMax", int.MaxValue,
                "overflow", "clamp", "mutationIdRequired", true, "mutationIdMaxLength", StatMutationIdLength,
                "deduplicationScope", "actor_generation", "maxMutations", StatAdjustmentLimit,
                "description", "Add signed integer deltas to current native values. Reuse mutationId and identical deltas to retrieve the original outcome without applying again, including after resume. Records are retained for this actor generation; new IDs are rejected when full.");
        }
        static Dictionary<string, object> NormalizeStatDeltas(Dictionary<string, object> input)
        {
            if (input.Count == 0) throw new BridgeException("invalid_argument", "deltas is empty");
            var result = new Dictionary<string, object>();
            foreach (var pair in input)
            {
                if (!AdjustableStat(pair.Key)) throw new BridgeException("unsupported_field", pair.Key + " does not support numeric adjustment; use set_stats for flags and relationships");
                double value = Number(pair.Value, int.MinValue, int.MaxValue, pair.Key);
                if (value != Math.Floor(value)) throw new BridgeException("invalid_argument", pair.Key + " delta must be an integer");
                result.Add(pair.Key, (int)value);
            }
            return result;
        }
        static bool SameStatDeltas(Dictionary<string, object> left, Dictionary<string, object> right)
        {
            if (left.Count != right.Count) return false;
            foreach (var pair in left)
            {
                object value;
                if (!right.TryGetValue(pair.Key, out value) || (int)pair.Value != (int)value) return false;
            }
            return true;
        }
        static object StatAdjustmentReply(StatAdjustment adjustment, bool replayed)
        {
            if (adjustment.Result == null) throw new BridgeException("adjustment_failed", adjustment.Error);
            var reply = new Dictionary<string, object>(adjustment.Result);
            reply["replayed"] = replayed;
            return reply;
        }
        object AdjustOwnedStats(string mutationId, Dictionary<string, object> input)
        {
            RequireStatsActor();
            if (mutationId.Length > StatMutationIdLength || mutationId.Trim().Length == 0)
                throw new BridgeException("invalid_argument", "mutationId must contain 1..128 nonblank characters");
            var deltas = NormalizeStatDeltas(input);
            if (statAdjustmentGeneration != ownedGeneration)
            { statAdjustments.Clear(); statAdjustmentGeneration = ownedGeneration; }
            StatAdjustment previous;
            if (statAdjustments.TryGetValue(mutationId, out previous))
            {
                if (!SameStatDeltas(previous.Deltas, deltas)) throw new BridgeException("adjustment_conflict", "mutationId was already used with different deltas");
                return StatAdjustmentReply(previous, true);
            }
            if (statAdjustments.Count >= StatAdjustmentLimit)
                throw new BridgeException("adjustment_limit", "This actor generation has reached its adjustment record limit; existing mutationIds can still be queried");

            // The queue executes synchronously on the Unity thread: snapshot, calculation and commit cannot interleave.
            SyncOwnedStats();
            var before = new Dictionary<string, object>(ownedStats);
            var changes = new Dictionary<string, object>();
            var clamped = new List<string>();
            foreach (var pair in deltas)
            {
                long total = (long)Convert.ToInt32(before[pair.Key]) + (int)pair.Value;
                int value = (int)Math.Max(StatMinimum(pair.Key), Math.Min((long)StatMaximum(pair.Key), total));
                changes.Add(pair.Key, value);
                if (total != value) clamped.Add(pair.Key);
            }
            changes = NormalizeStats(changes);
            // Reserve the ID before any native write. Never evict uncertain outcomes or replay a failed commit.
            var adjustment = new StatAdjustment(); adjustment.Deltas = deltas;
            statAdjustments.Add(mutationId, adjustment);
            try
            {
                var actual = CommitOwnedStats(changes, before);
                var applied = new Dictionary<string, object>();
                foreach (string key in deltas.Keys) applied.Add(key, (long)Convert.ToInt32(actual[key]) - Convert.ToInt32(before[key]));
                adjustment.Result = Json.Map("operation", "adjust_stats", "mutationId", mutationId, "target", ActorId, "generation", ownedGeneration,
                    "requestedDeltas", deltas, "requested", changes, "before", before, "actual", actual,
                    "appliedDeltas", applied, "clampedFields", clamped.ToArray());
            }
            catch (Exception e)
            {
                adjustment.Error = "Adjustment " + mutationId + " failed and will not run again with this mutationId. Inspect the actor before using a new ID. Cause: " + e.Message;
                throw new BridgeException("adjustment_failed", adjustment.Error);
            }
            // Cache the committed result before notifications or transport serialization can fail.
            try { AddEvent("stats_changed", adjustment.Result); }
            catch (Exception e) { Logger.LogWarning("Attribute adjustment notification: " + e.Message); }
            return StatAdjustmentReply(adjustment, false);
        }
    }
}
