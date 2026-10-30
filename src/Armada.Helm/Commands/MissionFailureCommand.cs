namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Armada.Core.Enums;

    /// <summary>
    /// <c>armada mission failure &lt;id&gt;</c>: an alias of <c>armada blackbox show</c> for a mission (the same options:
    /// <c>--brief</c>, <c>--redact-hosts</c>, <c>--json</c>).
    /// </summary>
    [Description("Show the black box entry of a mission (alias of blackbox show)")]
    public class MissionFailureCommand : BlackBoxShowCommand
    {
        /// <inheritdoc />
        protected override BlackBoxSubjectTypeEnum? ForcedSubject
        {
            get { return BlackBoxSubjectTypeEnum.Mission; }
        }
    }
}
