namespace Armada.Core.Services
{
    using System;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;
    using Armada.Core.Models;

    /// <summary>
    /// The pure rules behind model profiles (docs/MODEL_HETEROGENEITY.md, Decisions 5 and 6): the identity key that makes
    /// the same model as served map to one profile per tenant, and when an observation starts a new revision instead of
    /// pooling into the current one.
    /// </summary>
    public static class ModelProfileRules
    {
        #region Private-Members

        private static readonly Regex _VersionNumber = new Regex(@"\d+(\.\d+)*", RegexOptions.CultureInvariant);
        private const double SamplingTolerance = 1e-9;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The identity key of a profile: a SHA-256 hex digest of the tenant and the identity (runtime, provider and model
        /// trimmed and lower-cased; served context as a number). Equal for observations of the same model as served.
        /// </summary>
        /// <param name="tenantId">Tenant, or null.</param>
        /// <param name="identity">Identity.</param>
        /// <returns>64 lowercase hex characters.</returns>
        /// <exception cref="ArgumentNullException">Thrown when identity is null.</exception>
        public static string IdentityKey(string? tenantId, ModelProfileIdentity identity)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            string canonical = "model-profile-v1\n"
                + (Normalize(tenantId) ?? String.Empty) + "\n"
                + (Normalize(identity.Runtime) ?? String.Empty).ToLowerInvariant() + "\n"
                + (Normalize(identity.Provider) ?? String.Empty).ToLowerInvariant() + "\n"
                + (Normalize(identity.Model) ?? String.Empty).ToLowerInvariant() + "\n"
                + (identity.ServedContextTokens.HasValue ? identity.ServedContextTokens.Value.ToString(CultureInfo.InvariantCulture) : String.Empty);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                StringBuilder sb = new StringBuilder(64);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>
        /// The identity an observation describes, with blank values as null and text trimmed.
        /// </summary>
        /// <param name="observation">Observation.</param>
        /// <returns>The identity.</returns>
        /// <exception cref="ArgumentNullException">Thrown when observation is null.</exception>
        public static ModelProfileIdentity IdentityOf(ModelProfileObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            return new ModelProfileIdentity
            {
                Runtime = Normalize(observation.Runtime) ?? String.Empty,
                Provider = Normalize(observation.Provider),
                Model = Normalize(observation.Model),
                ServedContextTokens = observation.ServedContextTokens.HasValue && observation.ServedContextTokens.Value > 0 ? observation.ServedContextTokens : null
            };
        }

        /// <summary>
        /// The major component of a runtime version: the first number of the first dotted number in the text (a leading v
        /// and trailing words are ignored, so "v2.0.14 (Claude Code)" is 2). Text without a number is compared whole,
        /// lower-cased. Null or blank gives null.
        /// </summary>
        /// <param name="version">Runtime version text.</param>
        /// <returns>The major version, or null.</returns>
        public static string? RuntimeMajor(string? version)
        {
            string? text = Normalize(version);
            if (text == null) return null;
            Match match = _VersionNumber.Match(text);
            if (!match.Success) return text.ToLowerInvariant();
            string first = match.Value.Split('.')[0].TrimStart('0');
            return first.Length == 0 ? "0" : first;
        }

        /// <summary>
        /// Whether an observation starts a new revision rather than pooling into <paramref name="current"/>. A new runtime
        /// major version, a different model digest, or a different temperature or top-p starts one; a runtime minor or
        /// patch release pools. A value the observation does not know (null) never starts one; a value the current
        /// revision did not know but the observation does (first time it is known) does. A change of served context is a
        /// different identity, so it is a different profile with its own first revision.
        /// </summary>
        /// <param name="current">Current revision, or null when the profile has none (always starts one).</param>
        /// <param name="observation">Observation.</param>
        /// <returns>True when a new revision must be created.</returns>
        /// <exception cref="ArgumentNullException">Thrown when observation is null.</exception>
        public static bool StartsNewRevision(ModelProfileRevision? current, ModelProfileObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            if (current == null) return true;

            string? observedMajor = RuntimeMajor(observation.RuntimeVersion);
            if (observedMajor != null && !String.Equals(observedMajor, RuntimeMajor(current.RuntimeVersion), StringComparison.Ordinal)) return true;

            string? observedDigest = Normalize(observation.ModelDigest);
            if (observedDigest != null && !String.Equals(observedDigest, Normalize(current.ModelDigest), StringComparison.OrdinalIgnoreCase)) return true;

            if (SamplingDiffers(current.Sampling.Temperature, observation.Temperature)) return true;
            if (SamplingDiffers(current.Sampling.TopP, observation.TopP)) return true;
            return false;
        }

        /// <summary>
        /// A new revision describing an observation (the first time it is seen).
        /// </summary>
        /// <param name="profile">Profile the revision belongs to.</param>
        /// <param name="observation">Observation.</param>
        /// <returns>The revision (not yet stored).</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static ModelProfileRevision RevisionFor(ModelProfile profile, ModelProfileObservation observation)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            return new ModelProfileRevision
            {
                ProfileId = profile.Id,
                TenantId = profile.TenantId,
                RuntimeVersion = Normalize(observation.RuntimeVersion),
                ModelDigest = Normalize(observation.ModelDigest),
                Sampling = new ModelProfileSampling { Temperature = observation.Temperature, TopP = observation.TopP },
                ToolConfiguration = new ModelProfileToolConfiguration { Tools = observation.ToolsEnabled, Mcp = observation.McpToolsEnabled },
                FirstSeenUtc = DateTime.UtcNow
            };
        }

        #endregion

        #region Private-Methods

        private static bool SamplingDiffers(double? current, double? observed)
        {
            if (!observed.HasValue) return false;
            if (!current.HasValue) return true;
            return Math.Abs(current.Value - observed.Value) > SamplingTolerance;
        }

        private static string? Normalize(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
