namespace Test.Shared.Infrastructure
{
    using System;
    using System.Threading.Tasks;
    using Armada.Core.Services;

    /// <summary>
    /// Assertions for duplicate-entity handling: an action must fail with a typed
    /// <see cref="DuplicateEntityException"/>, and text a client sees must not carry database provider wording.
    /// </summary>
    public static class DuplicateEntityAsserts
    {
        #region Private-Members

        // Fragments of the four providers' unique-violation messages (and the schema names they quote). None of them
        // appears in Armada's own duplicate-entity messages.
        private static readonly string[] _ProviderMarkers = new string[]
        {
            "SQLite",
            "UNIQUE",
            "constraint",
            "Constraint",
            "duplicate key",
            "Duplicate entry",
            "Violation of",
            "23505",
            "Error 19",
            "idx_",
            "captains.",
            "fleets.",
            "vessels.",
            "personas.",
            "for key"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run <paramref name="action"/> and require a <see cref="DuplicateEntityException"/> whose message carries no
        /// provider text. Any other outcome fails with the actual exception type and message.
        /// </summary>
        /// <param name="action">Action expected to fail.</param>
        /// <param name="label">Label for failure messages.</param>
        /// <returns>The exception.</returns>
        public static async Task<DuplicateEntityException> ExpectDuplicateAsync(Func<Task> action, string label)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (DuplicateEntityException ex)
            {
                AssertNoProviderText(ex.Message, label + " message");
                return ex;
            }
            catch (Exception ex)
            {
                throw new AssertionException(label + ": expected DuplicateEntityException but got " + ex.GetType().FullName + ": " + ex.Message);
            }

            throw new AssertionException(label + ": expected DuplicateEntityException but the action succeeded");
        }

        /// <summary>
        /// Fail when <paramref name="text"/> contains database provider wording.
        /// </summary>
        /// <param name="text">Client-visible text (a message or a whole response body).</param>
        /// <param name="label">Label for failure messages.</param>
        public static void AssertNoProviderText(string? text, string label)
        {
            if (String.IsNullOrEmpty(text)) return;
            foreach (string marker in _ProviderMarkers)
            {
                if (text.Contains(marker, StringComparison.Ordinal))
                    throw new AssertionException(label + ": contains provider text <" + marker + ">: " + text);
            }
        }

        #endregion
    }
}
