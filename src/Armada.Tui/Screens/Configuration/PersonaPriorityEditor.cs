namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Ordered persona matching-class editor, including availability and exhaustion controls, and the escalation ladder
    /// (RS-5.6), which uses the same matcher.
    /// </summary>
    public sealed class PersonaPriorityEditor
    {
        private readonly RecordListField<PersonaCaptainPriority> _Priorities;
        private readonly SelectField<string> _Availability;
        private readonly SelectField<string> _Exhaustion;
        private readonly RecordListField<PersonaCaptainPriority> _Ladder;

        /// <summary>Attach localized fields to a persona form. The existing persona is never mutated.</summary>
        /// <exception cref="ArgumentNullException">The context or form is null.</exception>
        public PersonaPriorityEditor(TuiContext context, EntityForm form, Persona? persona)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (form == null) throw new ArgumentNullException(nameof(form));
            _Priorities = form.List<PersonaCaptainPriority>("Captain priorities", p => Describe(p),
                persona?.CaptainPriorities.Select(Copy).ToList(), 5,
                "a adds a priority; Enter edits; Del removes; Alt+Up/Down reorders.");
            _Priorities.EmptyText = "None (default routing)";
            _Priorities.Fingerprint = Describe;
            _Priorities.Editor = (priority, done) => Edit(context, "Captain priorities", priority, done);
            _Availability = form.Select("When a priority is unavailable", Enum.GetNames<PersonaPriorityAvailabilityEnum>()
                .Select(v => new SelectOption<string>(v, context.Loc.T(v == "FallThrough" ? "Try lower priorities" : "Wait for the first priority"))).ToList(),
                (persona?.PriorityAvailability ?? PersonaPriorityAvailabilityEnum.FallThrough).ToString(), null, true);
            _Exhaustion = form.Select("When all priorities are unavailable", Enum.GetNames<PersonaPriorityExhaustionEnum>()
                .Select(v => new SelectOption<string>(v, context.Loc.T(v == "Wait" ? "Wait" : "Use any eligible captain"))).ToList(),
                (persona?.PriorityExhaustion ?? PersonaPriorityExhaustionEnum.Wait).ToString(), null, true,
                "Wait for the first priority always waits, regardless of the exhaustion setting.");
            _Ladder = form.List<PersonaCaptainPriority>("Escalation ladder", p => Describe(p),
                persona?.EscalationLadder.Select(Copy).ToList(), 4,
                "Lowest rung first. A work stage whose captain cannot finish moves to the next rung. a adds a rung; Enter edits; Del removes; Alt+Up/Down reorders.");
            _Ladder.EmptyText = "None (higher tier only)";
            _Ladder.Fingerprint = Describe;
            _Ladder.Editor = (rung, done) => Edit(context, "Escalation ladder", rung, done);
        }

        /// <summary>Copy form values into a save payload.</summary>
        /// <exception cref="ArgumentNullException">The target persona is null.</exception>
        public void Apply(Persona persona)
        {
            if (persona == null) throw new ArgumentNullException(nameof(persona));
            persona.CaptainPriorities = _Priorities.Items.Select(Copy).ToList();
            persona.PriorityAvailability = EntityForm.EnumValue(_Availability, PersonaPriorityAvailabilityEnum.FallThrough);
            persona.PriorityExhaustion = EntityForm.EnumValue(_Exhaustion, PersonaPriorityExhaustionEnum.Wait);
            persona.EscalationLadder = _Ladder.Items.Select(Copy).ToList();
        }

        private static string Describe(PersonaCaptainPriority priority)
        {
            return String.Join(" / ", new[] { priority.Runtime?.ToString(), priority.Model, priority.Endpoint, priority.MinimumTier?.ToString() }.Where(v => !String.IsNullOrEmpty(v)));
        }

        private static PersonaCaptainPriority Copy(PersonaCaptainPriority priority)
        {
            return new PersonaCaptainPriority { Runtime = priority.Runtime, Model = priority.Model, Endpoint = priority.Endpoint, MinimumTier = priority.MinimumTier };
        }

        private static void Edit(TuiContext context, string title, PersonaCaptainPriority? priority, Action<PersonaCaptainPriority> done)
        {
            EntityUi.Run(context, ct => context.Client.ListModelEndpointsAsync(ct), endpoints => EditLoaded(context, title, priority, done, endpoints ?? new List<ModelEndpoint>()), "Failed to load endpoints.");
        }

        private static void EditLoaded(TuiContext context, string title, PersonaCaptainPriority? priority, Action<PersonaCaptainPriority> done, List<ModelEndpoint> endpoints)
        {
            EntityForm form = new EntityForm(context);
            List<SelectOption<string>> targets = Enum.GetNames<AgentRuntimeEnum>().Where(v => v != "ApiEndpoint").Select(v => new SelectOption<string>(v, v)).ToList();
            targets.AddRange(endpoints.Where(e => e.Kind == ModelEndpointKindEnum.Inference).Select(e => new SelectOption<string>("endpoint:" + e.Id, "API Endpoint: " + e.Name + " (" + e.Provider + " / " + e.Model + ")")));
            string? selected = priority?.Runtime == AgentRuntimeEnum.ApiEndpoint && priority.Endpoint != null ? "endpoint:" + priority.Endpoint : priority?.Runtime?.ToString();
            if (selected != null && !targets.Any(t => t.Value == selected)) targets.Add(new SelectOption<string>(selected, "API Endpoint: " + (priority?.Endpoint ?? "Any")));
            SelectField<string> runtime = form.Select("Runtime", targets, selected, "Any");
            InputField model = form.Text("Model", priority?.Model);
            InputField endpoint = form.Text("Endpoint", priority?.Endpoint, "", false, "Mux endpoint name, or choose a specific API endpoint in Runtime.");
            SelectField<string> tier = form.Select("Minimum tier", Enum.GetNames<CaptainTierEnum>().Select(v => new SelectOption<string>(v, context.Loc.T(v))).ToList(), priority?.MinimumTier?.ToString(), "Any");
            void SyncTarget()
            {
                endpoint.Visible = runtime.Value == "Mux";
            }
            runtime.ValueChanged += (s, e) => SyncTarget();
            SyncTarget();
            form.MarkClean();
            PersonaCaptainPriority? result = null;
            EntityUi.ShowForm(context, title, form, "Save", token =>
            {
                token.ThrowIfCancellationRequested();
                PersonaCaptainPriority candidate = new PersonaCaptainPriority
                {
                    Runtime = runtime.Value?.StartsWith("endpoint:", StringComparison.Ordinal) == true ? AgentRuntimeEnum.ApiEndpoint : Enum.TryParse(runtime.Value, out AgentRuntimeEnum parsedRuntime) ? parsedRuntime : null,
                    Model = EntityUi.Blank(model.Value),
                    Endpoint = runtime.Value?.StartsWith("endpoint:", StringComparison.Ordinal) == true ? runtime.Value.Substring("endpoint:".Length) : runtime.Value == "Mux" ? EntityUi.Blank(endpoint.Value) : null,
                    MinimumTier = Enum.TryParse(tier.Value, out CaptainTierEnum parsedTier) ? parsedTier : null
                };
                Persona validation = new Persona();
                validation.CaptainPriorities.Add(candidate);
                string? error = PersonaCaptainPrioritySelector.Validate(validation);
                if (error == null) result = candidate;
                return Task.FromResult(error);
            }, () => { if (result != null) done(result); });
        }
    }
}
