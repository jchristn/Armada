namespace Armada.Core.Services.Health
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;

    /// <summary>
    /// Grades presence of continuous integration configuration: a GitHub Actions workflow (.github/workflows/*.yml or
    /// *.yaml), azure-pipelines.yml, .gitlab-ci.yml, or a Jenkinsfile. Pass when present, Fail when absent (there is no
    /// Warn). Excluded from the overall rollup by default. Works on bare clones (from tracked files). Also records
    /// repository hygiene columns: HasCiConfig, HasLicense (a root LICENSE, LICENCE, or COPYING file), and HasReadme (a
    /// root README file).
    /// </summary>
    public class ContinuousIntegrationCriterion : IVesselHealthCriterion
    {
        #region Public-Members

        /// <inheritdoc />
        public VesselHealthCriterionEnum Code => VesselHealthCriterionEnum.ContinuousIntegration;

        /// <inheritdoc />
        public bool RequiresRepository => true;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<bool> AppliesAsync(VesselHealthContext context, CancellationToken token = default)
        {
            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public async Task<VesselHealthCriterionResult> EvaluateAsync(VesselHealthContext context, CancellationToken token = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            RepositoryFileInventory inventory = await context.Cache.GetInventoryAsync(context, token).ConfigureAwait(false);
            List<string> ciFiles = FindCiFiles(inventory);
            context.Health.HasCiConfig = ciFiles.Count > 0;
            context.Health.HasLicense = HasRootFile(inventory, new string[] { "LICENSE", "LICENCE", "COPYING" });
            context.Health.HasReadme = HasRootFile(inventory, new string[] { "README" });

            if (ciFiles.Count > 0)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.Pass, VesselHealthDetailCodes.CiConfigured, ciFiles.Count, null);
            return new VesselHealthCriterionResult(VesselHealthStatusEnum.Fail, VesselHealthDetailCodes.NoCiConfig, 0, null);
        }

        /// <summary>
        /// Find CI configuration files in an inventory.
        /// </summary>
        /// <param name="inventory">Repository inventory.</param>
        /// <returns>Relative paths of CI configuration files.</returns>
        /// <exception cref="ArgumentNullException">Thrown when inventory is null.</exception>
        public static List<string> FindCiFiles(RepositoryFileInventory inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            return inventory.Files.Where(f =>
                (f.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase)
                    && (f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
                || String.Equals(f, "azure-pipelines.yml", StringComparison.OrdinalIgnoreCase)
                || String.Equals(f, "azure-pipelines.yaml", StringComparison.OrdinalIgnoreCase)
                || String.Equals(f, ".gitlab-ci.yml", StringComparison.OrdinalIgnoreCase)
                || String.Equals(f, "Jenkinsfile", StringComparison.Ordinal))
                .ToList();
        }

        #endregion

        #region Private-Methods

        private static bool HasRootFile(RepositoryFileInventory inventory, string[] stems)
        {
            foreach (string file in inventory.Files)
            {
                if (file.Contains('/')) continue;
                foreach (string stem in stems)
                {
                    if (String.Equals(file, stem, StringComparison.OrdinalIgnoreCase)) return true;
                    if (file.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }

            return false;
        }

        #endregion
    }
}
