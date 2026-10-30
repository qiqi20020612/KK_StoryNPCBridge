using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        string resumeToken, recoveryWorld, recoveryGeneration, recoveryDeadlineUtc;
        float recoveryGraceSeconds, recoveryStartedAt, recoveryDeadline;
        object recoveryInterruptedTask;
        bool Recovering { get { return ownedController != null && recoveryDeadline > 0; } }
        void InitializeControlRecovery()
        { recoveryGraceSeconds = Mathf.Clamp(Config.Bind("Bridge", "ReconnectGraceSeconds", 60f, "Unexpected disconnect grace period, 5-600 seconds. Normal release/disconnect has no grace period.").Value, 5, 600); }
        void ClearControlRecovery()
        {
            resumeToken = null; recoveryWorld = null; recoveryGeneration = null; recoveryDeadlineUtc = null;
            recoveryDeadline = 0; recoveryStartedAt = 0; recoveryInterruptedTask = null;
        }
        void CreateResumeToken()
        { resumeToken = Guid.NewGuid().ToString("N"); recoveryWorld = world; recoveryGeneration = ownedGeneration; }
        object ReadRecovery()
        { return Json.Map("state", Recovering ? "waiting_reconnect" : ownedController != null ? "connected" : "unclaimed", "graceSeconds", recoveryGraceSeconds,
            "deadlineUtc", recoveryDeadlineUtc, "remainingSeconds", Recovering ? Math.Max(0, recoveryDeadline - Time.realtimeSinceStartup) : 0,
            "interruptedTaskState", recoveryInterruptedTask, "description", "Recovery requires authentication, the current generation and the private resumeToken from the claim/resume reply. Tokens are never included in snapshots."); }
        void TickControlRecovery()
        {
            if (ownedController == null) return;
            if (Recovering)
            {
                if (Time.realtimeSinceStartup >= recoveryDeadline)
                { AddEvent("controller_recovery_expired", Json.Map("reason", "reconnect_timeout")); ReleaseOwned("reconnect_timeout", false); }
                return;
            }
            if (ownedController.CleanExit) { ReleaseOwned("disconnected", false); return; }
            if (ownedController.Alive && (DateTime.UtcNow.Ticks - Interlocked.Read(ref ownedController.LastSeen)) / TimeSpan.TicksPerSecond <= 12) return;
            ownedController.Alive = false;
            try { if (ownedController.Client != null) ownedController.Client.Close(); } catch (ObjectDisposedException) { }
            recoveryStartedAt = Time.realtimeSinceStartup; recoveryDeadline = recoveryStartedAt + recoveryGraceSeconds;
            recoveryDeadlineUtc = DateTime.UtcNow.AddSeconds(recoveryGraceSeconds).ToString("o");
            recoveryInterruptedTask = ReadNativeStatus();
            if (resumeToken == null) CreateResumeToken();
            if (!SceneBusy && Mode == "roaming" && ownedNpc != null && ownedNpc.AI != null) StopOwnedMotion("connection_lost");
            else
            {
                FinishApproach("cancelled", "connection_lost");
                if (activeTask != null) AddEvent("native_action_cancelled", activeTask.Read());
                activeTask = null; nativeQueue.Clear(); ownedMoving = false; stopOnRoam = true;
            }
            if (ownedNpc != null) ownedNpc.Pause(true);
            if (ownedAgent != null && ownedAgent.enabled && ownedAgent.isOnNavMesh) { ownedAgent.isStopped = true; ownedAgent.ResetPath(); }
            ownedState = "waiting_reconnect"; SyncAdvInputLock();
            AddEvent("controller_connection_lost", ReadRecovery());
        }
        object ResumeControl(Dictionary<string, object> command, Session session)
        {
            string token = Text(command, "resumeToken");
            if (!Recovering) throw new BridgeException("not_waiting_reconnect", "The actor has no disconnected controller reservation");
            if (recoveryWorld != world || recoveryGeneration != ownedGeneration) throw new BridgeException("stale_recovery", "The story or actor generation changed");
            if (!BridgeServer.Equal(token, resumeToken)) throw new BridgeException("invalid_resume_token", "The recovery token is invalid or was already used");
            if (Time.realtimeSinceStartup >= recoveryDeadline) { ReleaseOwned("reconnect_timeout", false); throw new BridgeException("recovery_expired", "The grace period expired"); }
            float paused = Time.realtimeSinceStartup - recoveryStartedAt;
            if (DialogueActive)
            {
                dialogueOpenedAt += paused; advLineStartedAt += paused; lastHoldProcess += paused;
                if (modelRequestId != null) modelRequestedAt += paused;
            }
            ownedController = session; recoveryDeadline = 0; recoveryDeadlineUtc = null;
            CreateResumeToken(); ownedState = "idle";
            if (Mode == "roaming" && !SceneBusy && ownedNpc != null && ownedNpc.AI != null) SetNativeIdle(); else stopOnRoam = true;
            SyncAdvInputLock();
            AddEvent("controller_resumed", Json.Map("pausedSeconds", paused, "interruptedTasksRequireExplicitRestart", true));
            return ControlReply();
        }
        object ControlReply()
        {
            var result = (Dictionary<string, object>)ReadOwnedActor();
            result["handoverReport"] = lastHandoverReport; result["resumeToken"] = resumeToken;
            result["world"] = world; return result;
        }
    }
}
