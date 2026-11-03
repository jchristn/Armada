namespace Armada.Core.Services
{
    /// <summary>Shared semantic eligibility reason with the existing diagnostic summary.</summary>
    internal sealed class CaptainEligibilityFailure
    {
        internal string Code { get; set; } = "RoutingIneligible";
        internal string Summary { get; set; } = string.Empty;
    }
}
