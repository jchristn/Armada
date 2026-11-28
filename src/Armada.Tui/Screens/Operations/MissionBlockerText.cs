namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Text;
    using Armada.Core.Enums;

    /// <summary>
    /// Labels for the typed reasons a pending mission has no captain yet.
    /// </summary>
    public static class MissionBlockerText
    {
        /// <summary>
        /// A readable label for a blocker reason ("EndpointMissionLimit" becomes "Endpoint mission limit").
        /// </summary>
        /// <param name="reason">Blocker reason.</param>
        /// <returns>The label.</returns>
        public static string ReasonLabel(MissionAssignmentBlockerReasonEnum reason)
        {
            string name = reason.ToString();
            StringBuilder sb = new StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && Char.IsUpper(c) && !Char.IsUpper(name[i - 1])) sb.Append(' ').Append(Char.ToLowerInvariant(c));
                else sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
