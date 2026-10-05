namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Lookups a screen loads once and shares between its pickers and its name columns, like the dashboard pages'
    /// <c>listVessels({ pageSize: 1000 })</c> calls: vessels, captains, fleets, pipelines, personas, voyages, users, and
    /// playbooks. Each list loads on first request; <see cref="Changed"/> fires on the UI loop when one arrives.
    /// Failures leave a list empty (non-fatal, as on the dashboard). Not thread-safe; use on the UI loop.
    /// </summary>
    public class OpsReferenceData
    {
        #region Public-Members

        /// <summary>
        /// Vessels (empty until loaded).
        /// </summary>
        public List<Vessel> Vessels { get; private set; } = new List<Vessel>();

        /// <summary>
        /// Captains.
        /// </summary>
        public List<Captain> Captains { get; private set; } = new List<Captain>();

        /// <summary>
        /// Fleets.
        /// </summary>
        public List<Fleet> Fleets { get; private set; } = new List<Fleet>();

        /// <summary>
        /// Pipelines.
        /// </summary>
        public List<Pipeline> Pipelines { get; private set; } = new List<Pipeline>();

        /// <summary>
        /// Personas.
        /// </summary>
        public List<Persona> Personas { get; private set; } = new List<Persona>();

        /// <summary>
        /// Voyages.
        /// </summary>
        public List<Voyage> Voyages { get; private set; } = new List<Voyage>();

        /// <summary>
        /// Users (tenant admins only; empty otherwise).
        /// </summary>
        public List<UserMaster> Users { get; private set; } = new List<UserMaster>();

        /// <summary>
        /// Playbooks.
        /// </summary>
        public List<Playbook> Playbooks { get; private set; } = new List<Playbook>();

        /// <summary>
        /// Lists that finished loading (by name: vessels, captains, fleets, pipelines, personas, voyages, users, playbooks).
        /// </summary>
        public HashSet<string> Loaded { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Raised on the UI loop after a list arrives (argument: the list name).
        /// </summary>
        public event EventHandler<string>? Changed;

        #endregion

        #region Private-Members

        private readonly TuiContext _Context;
        private readonly Func<bool> _Live;
        private readonly HashSet<string> _Requested = new HashSet<string>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Services.</param>
        /// <param name="live">True while results should still be applied.</param>
        public OpsReferenceData(TuiContext context, Func<bool> live)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Live = live ?? (() => true);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Request lists by name (each loads once; <c>force</c> reloads).
        /// </summary>
        /// <param name="force">Reload even when already requested.</param>
        /// <param name="names">List names.</param>
        public void Ensure(bool force, params string[] names)
        {
            foreach (string name in names ?? Array.Empty<string>())
            {
                if (!force && _Requested.Contains(name)) continue;
                _Requested.Add(name);
                Load(name);
            }
        }

        /// <summary>
        /// Request lists by name once.
        /// </summary>
        /// <param name="names">List names.</param>
        public void Ensure(params string[] names)
        {
            Ensure(false, names);
        }

        /// <summary>
        /// Vessel name, or a short id (the dashboard's <c>vesselName</c>), or "-".
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <returns>Name.</returns>
        public string VesselName(string? id)
        {
            if (String.IsNullOrEmpty(id)) return "-";
            Vessel? v = Vessels.FirstOrDefault(x => x.Id == id);
            return v != null ? v.Name : Short(id!);
        }

        /// <summary>
        /// Captain name, or a short id, or "-".
        /// </summary>
        /// <param name="id">Captain id.</param>
        /// <returns>Name.</returns>
        public string CaptainName(string? id)
        {
            if (String.IsNullOrEmpty(id)) return "-";
            Captain? c = Captains.FirstOrDefault(x => x.Id == id);
            return c != null ? c.Name : Short(id!);
        }

        /// <summary>
        /// Fleet name, or the id, or "-".
        /// </summary>
        /// <param name="id">Fleet id.</param>
        /// <returns>Name.</returns>
        public string FleetName(string? id)
        {
            if (String.IsNullOrEmpty(id)) return "-";
            Fleet? f = Fleets.FirstOrDefault(x => x.Id == id);
            return f != null ? f.Name : id!;
        }

        /// <summary>
        /// Pipeline name, or the id, or "-".
        /// </summary>
        /// <param name="id">Pipeline id.</param>
        /// <returns>Name.</returns>
        public string PipelineName(string? id)
        {
            if (String.IsNullOrEmpty(id)) return "-";
            Pipeline? p = Pipelines.FirstOrDefault(x => x.Id == id || x.Name == id);
            return p != null ? p.Name : id!;
        }

        /// <summary>
        /// Voyage title, or the id, or "-".
        /// </summary>
        /// <param name="id">Voyage id.</param>
        /// <returns>Title.</returns>
        public string VoyageTitle(string? id)
        {
            if (String.IsNullOrEmpty(id)) return "-";
            Voyage? v = Voyages.FirstOrDefault(x => x.Id == id);
            return v != null ? v.Title : id!;
        }

        /// <summary>
        /// Vessel picker options sorted by name (value: id).
        /// </summary>
        /// <param name="emptyLabel">English label of a leading empty option, or null for none.</param>
        /// <returns>Options.</returns>
        public List<SelectOption<string>> VesselOptions(string? emptyLabel = null)
        {
            List<SelectOption<string>> options = new List<SelectOption<string>>();
            if (emptyLabel != null) options.Add(new SelectOption<string>("", _Context.Loc.T(emptyLabel)));
            options.AddRange(Vessels.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).Select(v => new SelectOption<string>(v.Id, v.Name, v.Id)));
            return options;
        }

        /// <summary>
        /// Captain picker options (value: id).
        /// </summary>
        /// <param name="emptyLabel">English label of a leading empty option, or null.</param>
        /// <returns>Options.</returns>
        public List<SelectOption<string>> CaptainOptions(string? emptyLabel = null)
        {
            List<SelectOption<string>> options = new List<SelectOption<string>>();
            if (emptyLabel != null) options.Add(new SelectOption<string>("", _Context.Loc.T(emptyLabel)));
            options.AddRange(Captains.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(c => new SelectOption<string>(c.Id, c.Name, c.Runtime + " / " + c.State)));
            return options;
        }

        /// <summary>
        /// Fleet picker options (value: id).
        /// </summary>
        /// <param name="emptyLabel">English label of a leading empty option, or null.</param>
        /// <returns>Options.</returns>
        public List<SelectOption<string>> FleetOptions(string? emptyLabel = null)
        {
            List<SelectOption<string>> options = new List<SelectOption<string>>();
            if (emptyLabel != null) options.Add(new SelectOption<string>("", _Context.Loc.T(emptyLabel)));
            options.AddRange(Fleets.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(f => new SelectOption<string>(f.Id, f.Name, f.Id)));
            return options;
        }

        /// <summary>
        /// Pipeline picker options (value: id).
        /// </summary>
        /// <param name="emptyLabel">English label of a leading empty option, or null.</param>
        /// <returns>Options.</returns>
        public List<SelectOption<string>> PipelineOptions(string? emptyLabel = null)
        {
            List<SelectOption<string>> options = new List<SelectOption<string>>();
            if (emptyLabel != null) options.Add(new SelectOption<string>("", _Context.Loc.T(emptyLabel)));
            options.AddRange(Pipelines.Select(p => new SelectOption<string>(p.Id, p.Name, p.Description ?? "")));
            return options;
        }

        /// <summary>
        /// Persona picker options (value: name).
        /// </summary>
        /// <param name="emptyLabel">English label of a leading empty option, or null.</param>
        /// <returns>Options.</returns>
        public List<SelectOption<string>> PersonaOptions(string? emptyLabel = null)
        {
            List<SelectOption<string>> options = new List<SelectOption<string>>();
            if (emptyLabel != null) options.Add(new SelectOption<string>("", _Context.Loc.T(emptyLabel)));
            options.AddRange(Personas.Select(p => new SelectOption<string>(p.Name, p.Name, p.Description ?? "")));
            return options;
        }

        /// <summary>
        /// User scope options for the admin-only "view as user" filter (value: user id; "" is All users).
        /// </summary>
        /// <returns>Options.</returns>
        public List<SelectOption<string>> UserScopeOptions()
        {
            List<SelectOption<string>> options = new List<SelectOption<string>> { new SelectOption<string>("", _Context.Loc.T("All users")) };
            foreach (UserMaster u in Users)
            {
                string name = String.Join(" ", new[] { u.FirstName, u.LastName }.Where(s => !String.IsNullOrWhiteSpace(s))).Trim();
                options.Add(new SelectOption<string>(u.Id, name.Length > 0 ? name + " (" + u.Email + ")" : u.Email));
            }

            return options;
        }

        #endregion

        #region Private-Methods

        private static string Short(string id)
        {
            return id.Length > 8 ? id.Substring(0, 8) : id;
        }

        private void Load(string name)
        {
            ArmadaClient client = _Context.Client;
            bool canScope = _Context.Session.IsGlobalAdmin || _Context.Session.IsTenantAdmin;
            _ = Task.Run(async () =>
            {
                try
                {
                    ArmadaPageQuery q = new ArmadaPageQuery();
                    q.PageSize = 1000;
                    switch (name)
                    {
                        case "vessels":
                            List<Vessel> vessels = (await client.ListVesselsAsync(q).ConfigureAwait(false))?.Objects ?? new List<Vessel>();
                            Apply(name, () => Vessels = vessels);
                            break;
                        case "captains":
                            List<Captain> captains = (await client.ListCaptainsAsync(q).ConfigureAwait(false))?.Objects ?? new List<Captain>();
                            Apply(name, () => Captains = captains);
                            break;
                        case "fleets":
                            List<Fleet> fleets = (await client.ListFleetsAsync(q).ConfigureAwait(false))?.Objects ?? new List<Fleet>();
                            Apply(name, () => Fleets = fleets);
                            break;
                        case "pipelines":
                            List<Pipeline> pipelines = (await client.ListPipelinesAsync(q).ConfigureAwait(false))?.Objects ?? new List<Pipeline>();
                            Apply(name, () => Pipelines = pipelines);
                            break;
                        case "personas":
                            List<Persona> personas = (await client.ListPersonasAsync(q).ConfigureAwait(false))?.Objects ?? new List<Persona>();
                            Apply(name, () => Personas = personas);
                            break;
                        case "voyages":
                            List<Voyage> voyages = (await client.ListVoyagesAsync(q).ConfigureAwait(false))?.Objects ?? new List<Voyage>();
                            Apply(name, () => Voyages = voyages);
                            break;
                        case "users":
                            if (!canScope)
                            {
                                Apply(name, () => Users = new List<UserMaster>());
                                break;
                            }

                            List<UserMaster> users = (await client.ListUsersAsync().ConfigureAwait(false))?.Objects ?? new List<UserMaster>();
                            Apply(name, () => Users = users);
                            break;
                        case "playbooks":
                            List<Playbook> playbooks = (await client.ListPlaybooksAsync(q).ConfigureAwait(false))?.Objects ?? new List<Playbook>();
                            Apply(name, () => Playbooks = playbooks);
                            break;
                    }
                }
                catch (Exception)
                {
                    // Non-fatal, as on the dashboard: the picker or name column stays minimal.
                    Apply(name, () => { });
                }
            });
        }

        private void Apply(string name, Action set)
        {
            _Context.Dispatcher.Post(() =>
            {
                if (!_Live()) return;
                set();
                Loaded.Add(name);
                Changed?.Invoke(this, name);
            });
        }

        #endregion
    }
}
