namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// A voyage read the way a client must read it now that GET /api/v1/voyages/{id} no longer embeds missions: the
    /// voyage detail (<see cref="VoyageProgress"/>) plus every mission read explicitly, one bounded page of 100 at a time,
    /// oldest first. Tests use it where they previously read the embedded Missions list.
    /// </summary>
    public sealed class VoyageDetailView
    {
        #region Public-Members

        /// <summary>
        /// The voyage.
        /// </summary>
        public Voyage? Voyage { get; private set; } = null;

        /// <summary>
        /// The voyage detail as served (counts only).
        /// </summary>
        public VoyageProgress? Progress { get; private set; } = null;

        /// <summary>
        /// Every mission of the voyage, oldest first, read through the paged missions enumeration.
        /// </summary>
        public List<Mission> Missions { get; private set; } = new List<Mission>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the detail from a GET /api/v1/voyages/{id} response, then page the voyage's missions.
        /// </summary>
        /// <param name="client">Client to page the missions with.</param>
        /// <param name="detailResponse">Response of GET /api/v1/voyages/{id}.</param>
        /// <returns>The view.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static async Task<VoyageDetailView> ReadAsync(HttpClient client, HttpResponseMessage detailResponse)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (detailResponse == null) throw new ArgumentNullException(nameof(detailResponse));
            VoyageDetailView view = new VoyageDetailView();
            view.Progress = await JsonHelper.DeserializeAsync<VoyageProgress>(detailResponse).ConfigureAwait(false);
            view.Voyage = view.Progress?.Voyage;
            if (view.Voyage == null || String.IsNullOrEmpty(view.Voyage.Id)) return view;
            view.Missions = await ReadMissionsAsync(client, view.Voyage.Id).ConfigureAwait(false);
            return view;
        }

        /// <summary>
        /// Read every mission of a voyage through bounded pages of 100, oldest first.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="voyageId">Voyage id.</param>
        /// <returns>The missions.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static async Task<List<Mission>> ReadMissionsAsync(HttpClient client, string voyageId)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (String.IsNullOrEmpty(voyageId)) throw new ArgumentNullException(nameof(voyageId));
            List<Mission> missions = new List<Mission>();
            for (int pageNumber = 1; pageNumber <= 100; pageNumber++)
            {
                HttpResponseMessage response = await client.GetAsync("/api/v1/missions?voyageId=" + Uri.EscapeDataString(voyageId) + "&order=CreatedAscending&pageSize=100&pageNumber=" + pageNumber).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) break;
                EnumerationResult<Mission> page = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response).ConfigureAwait(false);
                missions.AddRange(page.Objects);
                if (page.Objects.Count == 0 || pageNumber >= page.TotalPages) break;
            }

            return missions;
        }

        #endregion
    }
}
