namespace Armada.Tui.Screens.Delivery
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The environment's reusable verification definition editor (dashboard <c>EnvironmentDetail.tsx</c>, "Reusable
    /// Verification Definitions"): one form dialog per definition with name, method, path, expected status, must
    /// contain text, headers (one <c>Header-Name: value</c> per line), request body, and active. Plugs into a
    /// <see cref="RecordListField{T}"/> as its editor. Use on the UI loop.
    /// </summary>
    public static class VerificationDefinitionEditor
    {
        #region Public-Methods

        /// <summary>
        /// One-line summary of a definition.
        /// </summary>
        /// <param name="definition">Definition.</param>
        /// <returns>Text.</returns>
        public static string Describe(DeploymentVerificationDefinition definition)
        {
            string status = definition.ExpectedStatusCode.HasValue ? definition.ExpectedStatusCode.Value.ToString(CultureInfo.InvariantCulture) : "-";
            return (definition.Active ? "[x] " : "[ ] ") + definition.Name + "  " + definition.Method + " " + definition.Path + "  -> " + status
                + (String.IsNullOrEmpty(definition.MustContainText) ? "" : "  contains \"" + definition.MustContainText + "\"");
        }

        /// <summary>
        /// Stable text for dirty tracking.
        /// </summary>
        /// <param name="definition">Definition.</param>
        /// <returns>Text.</returns>
        public static string Fingerprint(DeploymentVerificationDefinition definition)
        {
            return Describe(definition) + "|" + SerializeHeaders(definition.Headers) + "|" + (definition.RequestBody ?? "") + "|" + definition.Id;
        }

        /// <summary>
        /// Wire a record list to this editor.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="field">Field.</param>
        public static void Attach(TuiContext context, RecordListField<DeploymentVerificationDefinition> field)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (field == null) throw new ArgumentNullException(nameof(field));
            field.Fingerprint = Fingerprint;
            field.EmptyText = "No reusable verification definitions are configured for this environment yet.";
            field.Editor = (existing, done) => Edit(context, existing, done);
            field.Toggle = d =>
            {
                DeploymentVerificationDefinition copy = Clone(d, false);
                copy.Active = !d.Active;
                return copy;
            };
        }

        /// <summary>
        /// Open the editor dialog for a definition (null adds a new one with the dashboard defaults).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Definition or null.</param>
        /// <param name="done">Called with the edited definition.</param>
        /// <returns>The dialog.</returns>
        public static Modals.FormDialog Edit(TuiContext context, DeploymentVerificationDefinition? existing, Action<DeploymentVerificationDefinition> done)
        {
            DeploymentVerificationDefinition seed = existing ?? new DeploymentVerificationDefinition { Name = "Verification", Method = "GET", Path = "/health", ExpectedStatusCode = 200, Active = true };
            EntityForm form = new EntityForm(context);
            InputField name = form.Text("Name", seed.Name, "", true);
            InputField method = form.Text("Method", seed.Method, "GET", true);
            InputField path = form.Text("Path", seed.Path, "/health or /api/status", true);
            InputField expected = form.Number("Expected Status", seed.ExpectedStatusCode, 100, 599);
            InputField mustContain = form.Text("Must Contain Text", seed.MustContainText);
            TextAreaField headers = form.Area("Headers", SerializeHeaders(seed.Headers), 3, false, null, ".txt");
            headers.Placeholder = "Header-Name: value";
            TextAreaField body = form.Area("Request Body", seed.RequestBody, 4, false, null, ".json");
            CheckField active = form.Check("Active", seed.Active);
            form.MarkClean();
            DeploymentVerificationDefinition? result = null;
            return EntityUi.ShowForm(context, existing != null ? "Edit" : "Add Verification", form, "Save", ct =>
            {
                DeploymentVerificationDefinition d = Clone(seed, false);
                d.Name = name.Value.Trim();
                d.Method = method.Value.Trim().ToUpperInvariant();
                d.Path = path.Value.Trim();
                d.ExpectedStatusCode = EntityForm.IntValue(expected);
                d.MustContainText = EntityUi.Blank(mustContain.Value);
                d.Headers = ParseHeaders(headers.Value);
                d.RequestBody = String.IsNullOrEmpty(body.Value) ? null : body.Value;
                d.Active = active.Value;
                result = d;
                return System.Threading.Tasks.Task.FromResult<string?>(null);
            }, () =>
            {
                if (result != null) done(result);
            });
        }

        /// <summary>
        /// Copy a definition (optionally with a new id, for Duplicate).
        /// </summary>
        /// <param name="source">Source.</param>
        /// <param name="newId">Generate a new id.</param>
        /// <returns>Copy.</returns>
        public static DeploymentVerificationDefinition Clone(DeploymentVerificationDefinition source, bool newId)
        {
            DeploymentVerificationDefinition copy = new DeploymentVerificationDefinition();
            if (!newId) copy.Id = source.Id;
            copy.Name = source.Name;
            copy.Method = source.Method;
            copy.Path = source.Path;
            copy.RequestBody = source.RequestBody;
            copy.Headers = new Dictionary<string, string>(source.Headers ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            copy.ExpectedStatusCode = source.ExpectedStatusCode;
            copy.MustContainText = source.MustContainText;
            copy.Active = source.Active;
            return copy;
        }

        /// <summary>
        /// Parse <c>Header-Name: value</c> lines (lines without a name are ignored).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Headers.</returns>
        public static Dictionary<string, string> ParseHeaders(string? text)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (String.IsNullOrEmpty(text)) return headers;
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                int idx = line.IndexOf(':');
                if (idx < 1) continue;
                string key = line.Substring(0, idx).Trim();
                if (key.Length == 0) continue;
                headers[key] = line.Substring(idx + 1).Trim();
            }

            return headers;
        }

        /// <summary>
        /// Serialize headers one per line.
        /// </summary>
        /// <param name="headers">Headers.</param>
        /// <returns>Text.</returns>
        public static string SerializeHeaders(Dictionary<string, string>? headers)
        {
            if (headers == null) return "";
            return String.Join("\n", headers.Select(h => h.Key + ": " + h.Value));
        }

        #endregion
    }
}
