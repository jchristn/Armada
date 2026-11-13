namespace Armada.Core.Models
{
    using System;

    /// <summary>Internal SQL-aggregated reservation estimates for one verified account/window generation.</summary>
    public sealed class UsageSelectionBudgetTotals
    {
        /// <summary>Credential-host namespace.</summary>
        public string HostId { get; set; } = String.Empty;
        /// <summary>Runtime namespace.</summary>
        public string Runtime { get; set; } = String.Empty;
        /// <summary>Opaque account identity; never expose in a client projection.</summary>
        public string AccountKey { get; set; } = String.Empty;
        /// <summary>Stable normalized window key.</summary>
        public string WindowKey { get; set; } = String.Empty;
        /// <summary>Proven window generation or continuous conservative accounting.</summary>
        public string Generation { get; set; } = String.Empty;
        /// <summary>Total active launch estimates in percentage points.</summary>
        public double ActivePercent { get; set; }
        /// <summary>Total ended-execution estimates awaiting settlement.</summary>
        public double UnsettledPercent { get; set; }
        /// <summary>Whether aged debt makes this account uncertain.</summary>
        public bool HasUncertainDebt { get; set; }
    }
}
