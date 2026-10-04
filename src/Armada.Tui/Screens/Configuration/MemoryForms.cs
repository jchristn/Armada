namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Memory create and edit (a TUI extension: the dashboard defines <c>createMemory</c> and <c>updateMemory</c> but
    /// its Memory page only lists and deletes): type, topic, key, summary, content (inline or <c>$EDITOR</c>),
    /// salience 0 to 1, vessel, tags, and scope. Use on the UI loop.
    /// </summary>
    public static class MemoryForms
    {
        #region Public-Methods

        /// <summary>
        /// Load vessels and open the form.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Memory to edit, or null to create.</param>
        /// <param name="onSaved">Runs on the UI loop with the saved memory.</param>
        public static void Open(TuiContext context, Memory? existing, Action<Memory> onSaved)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EntityUi.Run<List<Vessel>>(context, ct => EntityLookups.VesselsAsync(context.Client, ct), vessels => Show(context, existing, vessels, onSaved), "Failed to load memories.");
        }

        /// <summary>
        /// Open the form with the vessel list already loaded.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Memory to edit, or null.</param>
        /// <param name="vessels">Vessels.</param>
        /// <param name="onSaved">Saved callback.</param>
        /// <returns>The dialog.</returns>
        public static FormDialog Show(TuiContext context, Memory? existing, List<Vessel> vessels, Action<Memory> onSaved)
        {
            EntityForm form = new EntityForm(context);
            SelectField<string> type = form.Enum("Type", existing != null ? existing.Type : MemoryTypeEnum.Semantic);
            InputField topic = form.Text("Topic", existing?.Topic);
            InputField key = form.Text("Key", existing?.Key, "", false, "A memory with the same key is updated in place.");
            InputField summary = form.Text("Summary", existing?.Summary);
            TextAreaField content = form.Area("Content", existing?.Content, 8, true);
            InputField salience = form.Text("Salience", (existing?.Salience ?? 0.5).ToString("0.##", CultureInfo.InvariantCulture), "0.5", true, "Between 0 and 1; higher is recalled first.", v =>
            {
                if (!Double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return "Enter a number.";
                return d < 0 || d > 1 ? "The number is out of range." : null;
            });
            SelectField<string> vessel = form.Select("Vessel", EntityLookups.Options(vessels, v => v.Id, v => v.Name), existing?.VesselId, "None");
            InputField tags = form.Text("Tags", existing != null ? String.Join(", ", existing.Tags) : "", "tag1, tag2");
            SelectField<string> scope = form.Scope(existing != null ? existing.Scope : ScopeRules.ResolveCreateScope(context.Session, null));
            form.MarkClean();

            Memory? saved = null;
            return EntityUi.ShowForm(context, existing != null ? "Edit Memory" : "Create Memory", form, existing != null ? "Save Changes" : "Create Memory", async ct =>
            {
                Memory payload = existing != null ? (Armada.Client.ArmadaJson.Deserialize<Memory>(Armada.Client.ArmadaJson.Serialize(existing)) ?? new Memory()) : new Memory();
                payload.Type = EntityForm.EnumValue(type, MemoryTypeEnum.Semantic);
                payload.Topic = EntityUi.Blank(topic.Value);
                payload.Key = EntityUi.Blank(key.Value);
                payload.Summary = EntityUi.Blank(summary.Value);
                payload.Content = content.Value;
                payload.Salience = Double.Parse(salience.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
                payload.VesselId = EntityUi.Blank(vessel.Value);
                payload.Tags = EntityUi.SplitLines(tags.Value);
                payload.Scope = ScopeRules.ResolveCreateScope(context.Session, EntityForm.EnumValue(scope, ScopeEnum.TenantWide));
                saved = existing != null
                    ? await context.Client.UpdateMemoryAsync(existing.Id, payload, ct).ConfigureAwait(false)
                    : await context.Client.CreateMemoryAsync(payload, ct).ConfigureAwait(false);
                return null;
            }, () =>
            {
                if (saved == null) return;
                EntityUi.Toast(context, NotificationSeverityEnum.Success, existing != null ? context.Loc.T("Memory saved.") : context.Loc.T("Memory created."));
                onSaved?.Invoke(saved);
            });
        }

        #endregion
    }
}
