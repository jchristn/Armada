namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Armada.Core.Enums;

    /// <summary>
    /// <c>armada voyage failure &lt;id&gt;</c>: an alias of <c>armada blackbox show</c> for a voyage (the same options:
    /// <c>--brief</c>, <c>--redact-hosts</c>, <c>--json</c>).
    /// </summary>
    [Description("Show the black box entry of a voyage (alias of blackbox show)")]
    public class VoyageFailureCommand : BlackBoxShowCommand
    {
        /// <inheritdoc />
        protected override BlackBoxSubjectTypeEnum? ForcedSubject
        {
            get { return BlackBoxSubjectTypeEnum.Voyage; }
        }
    }
}
