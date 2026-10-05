namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Armada.Core.Models;

    /// <summary>
    /// Opens the setup wizard after sign-in with the dashboard's rules: a completely empty deployment (no fleets,
    /// vessels, or captains) always opens it and clears the "setup completed" preference; otherwise it opens while
    /// a fleet, vessel, or captain is missing unless setup was completed (or skipped) before. When the check fails,
    /// it opens.
    /// </summary>
    public static class SetupWizardAutoOpen
    {
        #region Public-Members

        /// <summary>
        /// Route of the setup wizard.
        /// </summary>
        public const string Route = "/setup";

        #endregion

        #region Public-Methods

        /// <summary>
        /// The auto-open rule. Null counts mean the check failed.
        /// </summary>
        /// <param name="hasFleet">Any fleet exists, or null when unknown.</param>
        /// <param name="hasVessel">Any vessel exists, or null when unknown.</param>
        /// <param name="hasCaptain">Any captain exists, or null when unknown.</param>
        /// <param name="completed">The "setup completed" preference.</param>
        /// <returns>Decision.</returns>
        public static SetupWizardDecision Decide(bool? hasFleet, bool? hasVessel, bool? hasCaptain, bool completed)
        {
            SetupWizardDecision decision = new SetupWizardDecision();
            if (hasFleet == null || hasVessel == null || hasCaptain == null)
            {
                decision.Open = true;
                return decision;
            }

            if (!hasFleet.Value && !hasVessel.Value && !hasCaptain.Value)
            {
                decision.ClearCompleted = true;
                decision.Open = true;
                return decision;
            }

            if (completed) return decision;
            decision.Open = !hasFleet.Value || !hasVessel.Value || !hasCaptain.Value;
            return decision;
        }

        /// <summary>
        /// Evaluate the rule against the server now; on the UI loop, clears the flag when needed and navigates to
        /// the wizard when it should open.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <returns>The background task.</returns>
        public static Task CheckAsync(TuiContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return Task.Run(async () =>
            {
                bool? hasFleet = null;
                bool? hasVessel = null;
                bool? hasCaptain = null;
                try
                {
                    EnumerationResult<Fleet>? fleets = await context.Client.ListFleetsAsync(new ArmadaPageQuery(1, 1)).ConfigureAwait(false);
                    EnumerationResult<Vessel>? vessels = await context.Client.ListVesselsAsync(new ArmadaPageQuery(1, 1)).ConfigureAwait(false);
                    EnumerationResult<Captain>? captains = await context.Client.ListCaptainsAsync(new ArmadaPageQuery(1, 1)).ConfigureAwait(false);
                    hasFleet = (fleets?.Objects ?? new List<Fleet>()).Count > 0;
                    hasVessel = (vessels?.Objects ?? new List<Vessel>()).Count > 0;
                    hasCaptain = (captains?.Objects ?? new List<Captain>()).Count > 0;
                }
                catch (Exception)
                {
                    // Unknown state opens the wizard, as in the dashboard.
                }

                context.Dispatcher.Post(() =>
                {
                    if (!context.Session.IsSignedIn) return;
                    SetupWizardDecision decision = Decide(hasFleet, hasVessel, hasCaptain, context.Prefs.Current.SetupCompleted);
                    if (decision.ClearCompleted && context.Prefs.Current.SetupCompleted)
                    {
                        context.Prefs.Current.SetupCompleted = false;
                        context.Prefs.Save();
                    }

                    if (decision.Open) context.Navigate(Route);
                });
            });
        }

        /// <summary>
        /// Check after every sign-in and open the wizard when the rule says so. A start route given on the command
        /// line (<c>--route</c>) wins for the first sign-in, so scripted and deep-linked starts land where asked.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="explicitStartRoute">True when the TUI was started with an explicit route.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public static void Attach(TuiContext context, bool explicitStartRoute = false)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            bool skipNext = explicitStartRoute;
            context.Session.SignedIn += (s, e) =>
            {
                if (skipNext)
                {
                    skipNext = false;
                    return;
                }

                _ = CheckAsync(context);
            };
        }

        #endregion
    }
}
