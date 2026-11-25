namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// A stand-in <see cref="IAdmiralService"/> for battery dispatch tests, built with <see cref="DispatchProxy"/>:
    /// CreateVoyageAsync records its request (in the positional form the battery cases read) and creates the voyage and an InProgress mission in the database;
    /// CancelMissionAsync marks the mission Cancelled. Every other member throws.
    /// </summary>
    public class RecordingBatteryAdmiral : DispatchProxy
    {
        #region Public-Members

        /// <summary>
        /// Database the voyage and mission are written to.
        /// </summary>
        public DatabaseDriver Database { get; set; } = null!;

        /// <summary>
        /// Every CreateVoyageAsync request as { Title, Description, VesselId, Missions, PipelineId, SelectedPlaybooks,
        /// CaptainOverridesJson, LandingMode, null, ApprovalOptions, IsEvaluation }.
        /// </summary>
        public List<object?[]> Dispatches { get; } = new List<object?[]>();

        /// <summary>
        /// Missions cancelled.
        /// </summary>
        public List<string> Cancelled { get; } = new List<string>();

        /// <summary>
        /// Thrown by CreateVoyageAsync when set.
        /// </summary>
        public Exception? DispatchError { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a proxy over a database.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <returns>The proxy (cast it to <see cref="RecordingBatteryAdmiral"/> to read what it recorded).</returns>
        public static IAdmiralService Create(DatabaseDriver database)
        {
            IAdmiralService proxy = DispatchProxy.Create<IAdmiralService, RecordingBatteryAdmiral>();
            ((RecordingBatteryAdmiral)(object)proxy).Database = database ?? throw new ArgumentNullException(nameof(database));
            return proxy;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null) throw new ArgumentNullException(nameof(targetMethod));
            if (targetMethod.Name == "CreateVoyageAsync" && args != null && args.Length >= 2 && args[1] is VoyageCreationRequest request)
                return DispatchAsync(new object?[]
                {
                    request.Title, request.Description, request.VesselId, request.Missions, request.PipelineId, request.SelectedPlaybooks,
                    Armada.Core.Services.MissionService.SerializeCaptainOverrides(request.CaptainOverrides), request.LandingMode, null,
                    request.ApprovalOptions, request.IsEvaluation
                });
            if (targetMethod.Name == "CancelMissionAsync" && args != null) return CancelAsync((Mission)args[0]!);
            throw new NotSupportedException(targetMethod.Name + " is not used by battery dispatch.");
        }

        #endregion

        #region Private-Methods

        private async Task<Voyage> DispatchAsync(object?[] args)
        {
            lock (Dispatches) Dispatches.Add(args);
            if (DispatchError != null) throw DispatchError;
            List<MissionDescription> missions = (List<MissionDescription>)args[3]!;
            bool isEvaluation = args.Length > 10 && args[10] is bool flag && flag;
            Voyage voyage = await Database.Voyages.CreateAsync(new Voyage((string)args[0]!, (string?)args[1]) { IsEvaluation = isEvaluation }).ConfigureAwait(false);
            foreach (MissionDescription description in missions)
            {
                Mission mission = new Mission(description.Title, description.Description)
                {
                    VoyageId = voyage.Id,
                    VesselId = (string)args[2]!,
                    Persona = description.Persona,
                    IsEvaluation = voyage.IsEvaluation,
                    Status = MissionStatusEnum.InProgress
                };
                await Database.Missions.CreateAsync(mission).ConfigureAwait(false);
            }

            return voyage;
        }

        private async Task<Mission> CancelAsync(Mission mission)
        {
            lock (Cancelled) Cancelled.Add(mission.Id);
            mission.Status = MissionStatusEnum.Cancelled;
            return await Database.Missions.UpdateAsync(mission, CancellationToken.None).ConfigureAwait(false);
        }

        #endregion
    }
}
