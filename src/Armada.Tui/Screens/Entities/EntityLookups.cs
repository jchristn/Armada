namespace Armada.Tui.Screens.Entities
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Reference lists the entity screens need for pickers and name columns (the dashboard loads them with
    /// <c>pageSize: 9999</c>): vessels, environments, releases, workflow profiles, deployments, personas, prompt
    /// templates, captains, fleets, and pipelines. Every method reads all pages and returns an empty list when the
    /// call fails, so a missing permission never blocks a screen. Thread-safe (stateless; safe off the UI loop).
    /// </summary>
    public static class EntityLookups
    {
        #region Public-Members

        /// <summary>
        /// Page size used when reading reference lists.
        /// </summary>
        public const int PageSize = 500;

        #endregion

        #region Public-Methods

        /// <summary>
        /// All vessels.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Vessels.</returns>
        public static Task<List<Vessel>> VesselsAsync(ArmadaClient client, CancellationToken token = default)
        {
            return Safe(() => ArmadaPaging.ReadAllAsync<Vessel>((p, ct) => client.ListVesselsAsync(new ArmadaPageQuery(p, PageSize), ct), 20, token));
        }

        /// <summary>
        /// All deployment environments.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Environments.</returns>
        public static Task<List<DeploymentEnvironment>> EnvironmentsAsync(ArmadaClient client, CancellationToken token = default)
        {
            return Safe(() => ArmadaPaging.ReadAllAsync<DeploymentEnvironment>((p, ct) => client.ListEnvironmentsAsync(new DeploymentEnvironmentQuery { PageNumber = p, PageSize = PageSize }, ct), 20, token));
        }

        /// <summary>
        /// All releases.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Releases.</returns>
        public static Task<List<Release>> ReleasesAsync(ArmadaClient client, CancellationToken token = default)
        {
            return Safe(() => ArmadaPaging.ReadAllAsync<Release>((p, ct) => client.ListReleasesAsync(new ReleaseQuery { PageNumber = p, PageSize = PageSize }, ct), 20, token));
        }

        /// <summary>
        /// All deployments.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Deployments.</returns>
        public static Task<List<Deployment>> DeploymentsAsync(ArmadaClient client, CancellationToken token = default)
        {
            return Safe(() => ArmadaPaging.ReadAllAsync<Deployment>((p, ct) => client.ListDeploymentsAsync(new DeploymentQuery { PageNumber = p, PageSize = PageSize }, ct), 20, token));
        }

        /// <summary>
        /// All workflow profiles.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Profiles.</returns>
        public static Task<List<WorkflowProfile>> WorkflowProfilesAsync(ArmadaClient client, CancellationToken token = default)
        {
            return Safe(() => ArmadaPaging.ReadAllAsync<WorkflowProfile>((p, ct) => client.ListWorkflowProfilesAsync(new ArmadaPageQuery(p, PageSize), ct), 20, token));
        }

        /// <summary>
        /// All personas.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Personas.</returns>
        public static Task<List<Persona>> PersonasAsync(ArmadaClient client, CancellationToken token = default)
        {
            return Safe(() => ArmadaPaging.ReadAllAsync<Persona>((p, ct) => client.ListPersonasAsync(new ArmadaPageQuery(p, PageSize), ct), 20, token));
        }

        /// <summary>
        /// All prompt templates.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Templates.</returns>
        public static Task<List<PromptTemplate>> PromptTemplatesAsync(ArmadaClient client, CancellationToken token = default)
        {
            return Safe(() => ArmadaPaging.ReadAllAsync<PromptTemplate>((p, ct) => client.ListPromptTemplatesAsync(new ArmadaPageQuery(p, PageSize), ct), 20, token));
        }

        /// <summary>
        /// All captains.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Captains.</returns>
        public static Task<List<Captain>> CaptainsAsync(ArmadaClient client, CancellationToken token = default)
        {
            return Safe(() => ArmadaPaging.ReadAllAsync<Captain>((p, ct) => client.ListCaptainsAsync(new ArmadaPageQuery(p, PageSize), ct), 20, token));
        }

        /// <summary>
        /// All fleets.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Fleets.</returns>
        public static Task<List<Fleet>> FleetsAsync(ArmadaClient client, CancellationToken token = default)
        {
            return Safe(() => ArmadaPaging.ReadAllAsync<Fleet>((p, ct) => client.ListFleetsAsync(new ArmadaPageQuery(p, PageSize), ct), 20, token));
        }

        /// <summary>
        /// All pipelines.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Pipelines.</returns>
        public static Task<List<Pipeline>> PipelinesAsync(ArmadaClient client, CancellationToken token = default)
        {
            return Safe(() => ArmadaPaging.ReadAllAsync<Pipeline>((p, ct) => client.ListPipelinesAsync(new ArmadaPageQuery(p, PageSize), ct), 20, token));
        }

        /// <summary>
        /// Select options from records.
        /// </summary>
        /// <typeparam name="TItem">Record type.</typeparam>
        /// <param name="items">Records.</param>
        /// <param name="id">Value selector.</param>
        /// <param name="label">Label selector.</param>
        /// <param name="detail">Optional detail selector.</param>
        /// <returns>Options.</returns>
        public static List<SelectOption<string>> Options<TItem>(IEnumerable<TItem>? items, Func<TItem, string> id, Func<TItem, string> label, Func<TItem, string>? detail = null)
        {
            if (items == null) return new List<SelectOption<string>>();
            return items.Select(i => new SelectOption<string>(id(i), label(i), detail != null ? detail(i) : "")).ToList();
        }

        /// <summary>
        /// Name lookup by id; returns the id itself (or "-") when unknown.
        /// </summary>
        /// <param name="map">Map.</param>
        /// <param name="id">Id.</param>
        /// <param name="fallback">Fallback when the id is blank.</param>
        /// <returns>Name.</returns>
        public static string Name(IReadOnlyDictionary<string, string>? map, string? id, string fallback = "-")
        {
            if (String.IsNullOrEmpty(id)) return fallback;
            if (map != null && map.TryGetValue(id!, out string? name) && !String.IsNullOrEmpty(name)) return name;
            return id!;
        }

        #endregion

        #region Private-Methods

        private static async Task<List<TItem>> Safe<TItem>(Func<Task<List<TItem>>> load)
        {
            try
            {
                return await load().ConfigureAwait(false);
            }
            catch (ArmadaApiException)
            {
                return new List<TItem>();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return new List<TItem>();
            }
        }

        #endregion
    }
}
