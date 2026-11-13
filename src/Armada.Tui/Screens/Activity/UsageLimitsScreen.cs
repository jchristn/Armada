namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;
    using Armada.Client;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;

    /// <summary>Account/host limits under Activity, with captain-scoped capture and expiring cooldown.</summary>
    public class UsageLimitsScreen : OpsDetailScreen
    {
        private readonly string? _CaptainId;
        private readonly OpsDocumentView _Usage = new OpsDocumentView();
        private List<UsageSnapshotView>? _Views;
        private int _Page = 1;
        private int _Pages = 1;
        private bool _Capturing;
        private DateTime _NextEligible;
        private string? _Message;

        /// <summary>Create a limits view. The captainId query opens one captain's accounts.</summary>
        public UsageLimitsScreen(RouteMatch route, TuiContext context) : base(route, context, "UsageLimitsScreen", "Limits")
        {
            _CaptainId = route.Query.TryGetValue("captainId", out string? id) ? id : null;
            _Usage.Builder = doc => UsagePresentation.Build(doc, _Views, _Message, Context.Clock.UtcNow);
            AddPanel("usage", "Usage", _Usage);
            OpsScreenAction captureAction = Action("capture", "Capture now", Capture, "c", () => !String.IsNullOrEmpty(_CaptainId) && (Context.Session.IsGlobalAdmin || Context.Session.IsTenantAdmin), true);
            captureAction.EnabledWhen = () => !_Capturing && Context.Clock.UtcNow >= _NextEligible;
            captureAction.DynamicLabel = () => Context.Clock.UtcNow < _NextEligible ? "Capture cooldown" : "Capture now";
            Action("previous", "Previous", () => { _Page--; Load(); }, "[", () => _Page > 1, true);
            Action("next", "Next", () => { _Page++; Load(); }, "]", () => _Page < _Pages, true);
            SubscribeCoalesced("usage.snapshot", Load);
            Load();
        }
        /// <inheritdoc />
        public override void Load()
        {
            Call((client, token) => client.GetUsageLatestAsync(new UsageQuery { CaptainId = _CaptainId, PageNumber = _Page, PageSize = 25 }, token), result =>
            {
                _Views = result?.Objects ?? new List<UsageSnapshotView>();
                _Pages = result?.TotalPages ?? 1;
                Loaded = true; LoadError = null; _Message = null; Heading = Tr("Limits");
                _Usage.Invalidate();
            }, null, error => { Loaded = true; Heading = Tr("Limits"); _Message = Tr("Usage could not be loaded."); _Usage.Invalidate(); });
        }
        private void Capture()
        {
            if (_Capturing || Context.Clock.UtcNow < _NextEligible) return;
            _Capturing = true;
            Call((client, token) => client.CaptureCaptainUsageAsync(_CaptainId!, null, token), receipt =>
            {
                _Capturing = false;
                _NextEligible = receipt?.NextEligibleUtc ?? (receipt?.Status == "Passive" ? DateTime.MinValue : Context.Clock.UtcNow.AddSeconds(60));
                _Message = Tr(receipt?.Status == "Passive" ? "Fresh" : "Capture queued");
                _Usage.Invalidate();
            }, null, error =>
            {
                _Capturing = false;
                if (error is ArmadaApiException api && api.StatusCode == 429) { _NextEligible = UsageCooldown.Resolve(api, Context.Clock.UtcNow); _Message = Tr("Capture cooldown"); }
                else _Message = Tr("Capture failed") + ": " + error.Message;
                _Usage.Invalidate();
            });
        }
    }
}
