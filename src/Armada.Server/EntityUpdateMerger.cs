namespace Armada.Server
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Shared rules for applying a client-supplied replacement body to a stored record on update. The REST PUT routes
    /// and the WebSocket update_* commands both go through these methods so that server-owned fields (tenant and owner,
    /// operational state, timestamps) are preserved the same way on every surface and cannot be cleared or spoofed by
    /// omitting or sending them in the body.
    /// </summary>
    public static class EntityUpdateMerger
    {
        #region Public-Methods

        /// <summary>
        /// Apply a fleet replacement body: the body supplies the editable fields; id, tenant, owner, and creation time
        /// come from the stored record.
        /// </summary>
        /// <param name="existing">Stored fleet.</param>
        /// <param name="incoming">Client-supplied replacement body.</param>
        /// <returns>The record to persist (the incoming instance with server-owned fields restored).</returns>
        public static Fleet MergeFleet(Fleet existing, Fleet incoming)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));

            incoming.Id = existing.Id;
            incoming.TenantId = existing.TenantId;
            incoming.UserId = existing.UserId;
            incoming.CreatedUtc = existing.CreatedUtc;
            incoming.LastUpdateUtc = DateTime.UtcNow;
            return incoming;
        }

        /// <summary>
        /// Apply a vessel replacement body: the body supplies the editable fields; id, tenant, and owner come from the
        /// stored record, and the GitHub token override is kept unless the body explicitly carries the field.
        /// </summary>
        /// <param name="existing">Stored vessel.</param>
        /// <param name="incoming">Client-supplied replacement body.</param>
        /// <returns>The record to persist (the incoming instance with server-owned fields restored).</returns>
        public static Vessel MergeVessel(Vessel existing, Vessel incoming)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));

            incoming.Id = existing.Id;
            incoming.TenantId = existing.TenantId;
            incoming.UserId = existing.UserId;
            if (incoming.GitHubTokenOverrideSpecified)
            {
                incoming.NormalizeGitHubTokenOverride();
            }
            else
            {
                incoming.GitHubTokenOverride = existing.GitHubTokenOverride;
            }
            return incoming;
        }

        /// <summary>
        /// Apply a mission update body: only the metadata fields (title, description, priority, vessel, voyage, branch,
        /// pull request URL, parent mission) are copied onto the stored record. Status, captain, dock, process, commit,
        /// diff, tenant, owner, and timestamps are preserved.
        /// </summary>
        /// <param name="existing">Stored mission; it is modified in place.</param>
        /// <param name="incoming">Client-supplied update body.</param>
        /// <returns>The record to persist (the stored instance with the metadata fields applied).</returns>
        public static Mission MergeMission(Mission existing, Mission incoming)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));

            existing.Title = incoming.Title;
            existing.Description = incoming.Description;
            existing.Priority = incoming.Priority;
            existing.VesselId = incoming.VesselId;
            existing.VoyageId = incoming.VoyageId;
            existing.BranchName = incoming.BranchName;
            existing.PrUrl = incoming.PrUrl;
            existing.ParentMissionId = incoming.ParentMissionId;
            existing.LastUpdateUtc = DateTime.UtcNow;
            return existing;
        }

        /// <summary>
        /// Apply a captain replacement body: the body supplies the configuration fields (name, runtime, model, persona
        /// and runtime options); id, tenant, owner, operational state (state, current mission and dock, process,
        /// recovery attempts, quarantine, heartbeats), and creation time come from the stored record. Runtime options
        /// are normalized for the runtime as on create.
        /// </summary>
        /// <param name="existing">Stored captain.</param>
        /// <param name="incoming">Client-supplied replacement body.</param>
        /// <returns>The record to persist (the incoming instance with server-owned fields restored).</returns>
        public static Captain MergeCaptain(Captain existing, Captain incoming)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));

            incoming.Id = existing.Id;
            incoming.TenantId = existing.TenantId;
            incoming.UserId = existing.UserId;
            incoming.State = existing.State;
            incoming.CurrentMissionId = existing.CurrentMissionId;
            incoming.CurrentDockId = existing.CurrentDockId;
            incoming.ProcessId = existing.ProcessId;
            incoming.RecoveryAttempts = existing.RecoveryAttempts;
            incoming.QuarantineUntilUtc = existing.QuarantineUntilUtc;
            incoming.QuarantineReason = existing.QuarantineReason;
            // The CLI permission policy changes only through its own endpoint (PUT /api/v1/captains/{id}/cli-permission-policy).
            incoming.CliPermissionPolicy = existing.CliPermissionPolicy;
            incoming.LastHeartbeatUtc = existing.LastHeartbeatUtc;
            incoming.LastProcessAliveUtc = existing.LastProcessAliveUtc;
            incoming.CreatedUtc = existing.CreatedUtc;
            incoming.LastUpdateUtc = DateTime.UtcNow;
            NormalizeCaptainRuntimeOptions(incoming, existing);
            return incoming;
        }

        /// <summary>
        /// Normalize a captain's runtime options for its runtime. Non-Mux captains keep only the runtime-independent
        /// autoApprove switch (carried over from the stored record when the body omits options); Mux captains keep
        /// their stored options when the body omits them.
        /// </summary>
        /// <param name="captain">Captain to normalize in place.</param>
        /// <param name="existing">Stored captain on update, or null on create.</param>
        public static void NormalizeCaptainRuntimeOptions(Captain captain, Captain? existing = null)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));

            if (captain.Runtime != AgentRuntimeEnum.Mux)
            {
                bool? autoApprove = CaptainRuntimeOptions.GetExplicitAutoApprove(captain.RuntimeOptionsJson);
                if (autoApprove == null && String.IsNullOrWhiteSpace(captain.RuntimeOptionsJson) && existing != null)
                    autoApprove = CaptainRuntimeOptions.GetExplicitAutoApprove(existing.RuntimeOptionsJson);
                captain.RuntimeOptionsJson = CaptainRuntimeOptions.WithAutoApprove(null, autoApprove);
                return;
            }

            if (String.IsNullOrWhiteSpace(captain.RuntimeOptionsJson) &&
                existing != null &&
                existing.Runtime == AgentRuntimeEnum.Mux &&
                !String.IsNullOrWhiteSpace(existing.RuntimeOptionsJson))
            {
                captain.RuntimeOptionsJson = existing.RuntimeOptionsJson;
                return;
            }

            if (String.IsNullOrWhiteSpace(captain.RuntimeOptionsJson))
            {
                captain.RuntimeOptionsJson = null;
            }
        }

        #endregion
    }
}
