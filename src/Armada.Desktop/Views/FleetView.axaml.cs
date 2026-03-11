namespace Armada.Desktop.Views
{
    using System.Collections.Generic;
    using System.Linq;
    using Avalonia.Controls;
    using Avalonia.Interactivity;
    using Avalonia.VisualTree;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Desktop.ViewModels;

    /// <summary>
    /// Fleet management view.
    /// </summary>
    public partial class FleetView : UserControl
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Handle tab switching.
        /// </summary>
        private void OnTabClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tab)
            {
                Border? vessels = this.FindControl<Border>("VesselsPanel");
                Border? captains = this.FindControl<Border>("CaptainsPanel");
                Border? fleets = this.FindControl<Border>("FleetsPanel");

                if (vessels != null) vessels.IsVisible = tab == "Vessels";
                if (captains != null) captains.IsVisible = tab == "Captains";
                if (fleets != null) fleets.IsVisible = tab == "Fleets";

                if (DataContext is FleetViewModel vm) vm.SelectedTab = tab;
            }
        }

        /// <summary>
        /// Stop all captains with confirmation.
        /// </summary>
        private async void OnStopAllClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is FleetViewModel vm)
            {
                Window? owner = this.FindAncestorOfType<Window>();
                if (owner != null)
                {
                    ConfirmDialog dialog = new ConfirmDialog(
                        "Stop All Captains",
                        "This will stop all working and stalled captains. Active missions will be interrupted.",
                        "Stop All");
                    bool confirmed = await dialog.ShowConfirmAsync(owner);
                    if (!confirmed) return;
                }

                await vm.StopAllCaptainsAsync();
            }
        }

        /// <summary>
        /// Stop a single captain.
        /// </summary>
        private async void OnStopCaptainClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string captainId && DataContext is FleetViewModel vm)
            {
                await vm.StopCaptainAsync(captainId);
            }
        }

        /// <summary>
        /// Edit a captain's settings.
        /// </summary>
        private async void OnEditCaptainClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string captainId && DataContext is FleetViewModel vm)
            {
                Captain? captain = vm.Captains.FirstOrDefault(c => c.Id == captainId);
                if (captain == null) return;

                Window? owner = this.FindAncestorOfType<Window>();
                if (owner == null) return;

                List<Mission> recentMissions = await vm.LoadRecentMissionsForCaptainAsync(captainId);

                EditCaptainDialog dialog = new EditCaptainDialog(captain, recentMissions);
                bool saved = await dialog.ShowEditAsync(owner);

                if (dialog.SelectedMissionId != null)
                {
                    if (owner.DataContext is MainWindowViewModel mainVm)
                    {
                        mainVm.NavigateTo("Missions");
                    }
                }

                if (!saved) return;

                captain.Name = dialog.EditedName;
                captain.Runtime = dialog.EditedRuntime;
                captain.MaxParallelism = dialog.EditedParallelism;
                await vm.UpdateCaptainAsync(captain);
            }
        }

        /// <summary>
        /// Remove a captain with confirmation.
        /// </summary>
        private async void OnRemoveCaptainClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string captainId && DataContext is FleetViewModel vm)
            {
                // Check if captain is active
                Captain? captain = vm.Captains.FirstOrDefault(c => c.Id == captainId);
                if (captain != null && captain.State == CaptainStateEnum.Working)
                {
                    Window? owner = this.FindAncestorOfType<Window>();
                    if (owner != null)
                    {
                        ConfirmDialog errorDialog = new ConfirmDialog(
                            "Cannot Remove Captain",
                            "This captain is currently working. Stop the captain before removing it.",
                            "OK",
                            isDanger: false);
                        await errorDialog.ShowConfirmAsync(owner);
                    }
                    return;
                }

                Window? confirmOwner = this.FindAncestorOfType<Window>();
                if (confirmOwner != null)
                {
                    ConfirmDialog dialog = new ConfirmDialog(
                        "Remove Captain",
                        "This will permanently remove this captain.",
                        "Remove");
                    bool confirmed = await dialog.ShowConfirmAsync(confirmOwner);
                    if (!confirmed) return;
                }

                await vm.RemoveCaptainAsync(captainId);
            }
        }

        /// <summary>
        /// Toggle add captain form.
        /// </summary>
        private void OnToggleAddCaptainClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is FleetViewModel vm) vm.ShowAddCaptain = !vm.ShowAddCaptain;
        }

        /// <summary>
        /// Add a new captain.
        /// </summary>
        private async void OnAddCaptainClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is FleetViewModel vm) await vm.AddCaptainAsync();
        }

        /// <summary>
        /// Cancel add captain.
        /// </summary>
        private void OnCancelAddCaptainClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is FleetViewModel vm) vm.ShowAddCaptain = false;
        }

        /// <summary>
        /// Toggle add vessel form.
        /// </summary>
        private void OnToggleAddVesselClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is FleetViewModel vm) vm.ShowAddVessel = !vm.ShowAddVessel;
        }

        /// <summary>
        /// Add a new vessel.
        /// </summary>
        private async void OnAddVesselClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is FleetViewModel vm) await vm.AddVesselAsync();
        }

        /// <summary>
        /// Cancel add vessel.
        /// </summary>
        private void OnCancelAddVesselClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is FleetViewModel vm) vm.ShowAddVessel = false;
        }

        /// <summary>
        /// Edit a vessel's settings.
        /// </summary>
        private async void OnEditVesselClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string vesselId && DataContext is FleetViewModel vm)
            {
                Vessel? vessel = vm.Vessels.FirstOrDefault(v => v.Id == vesselId);
                if (vessel == null) return;

                Window? owner = this.FindAncestorOfType<Window>();
                if (owner == null) return;

                EditVesselDialog dialog = new EditVesselDialog(vessel);
                bool saved = await dialog.ShowEditAsync(owner);

                if (!saved) return;

                vessel.Name = dialog.EditedName;
                vessel.RepoUrl = dialog.EditedRepoUrl;
                vessel.DefaultBranch = dialog.EditedBranch;
                vessel.WorkingDirectory = dialog.EditedWorkingDirectory;
                vessel.ProjectContext = dialog.EditedProjectContext;
                vessel.StyleGuide = dialog.EditedStyleGuide;
                await vm.UpdateVesselAsync(vessel);
            }
        }

        /// <summary>
        /// Remove a vessel with confirmation.
        /// </summary>
        private async void OnRemoveVesselClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string vesselId && DataContext is FleetViewModel vm)
            {
                Window? owner = this.FindAncestorOfType<Window>();
                if (owner != null)
                {
                    ConfirmDialog dialog = new ConfirmDialog(
                        "Remove Vessel",
                        "This will decommission this vessel. Existing missions for this vessel will not be affected.",
                        "Remove");
                    bool confirmed = await dialog.ShowConfirmAsync(owner);
                    if (!confirmed) return;
                }

                await vm.DeleteVesselAsync(vesselId);
            }
        }

        /// <summary>
        /// Toggle add fleet form.
        /// </summary>
        private void OnToggleAddFleetClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is FleetViewModel vm) vm.ShowAddFleet = !vm.ShowAddFleet;
        }

        /// <summary>
        /// Add a new fleet.
        /// </summary>
        private async void OnAddFleetClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is FleetViewModel vm) await vm.AddFleetAsync();
        }

        /// <summary>
        /// Cancel add fleet.
        /// </summary>
        private void OnCancelAddFleetClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is FleetViewModel vm) vm.ShowAddFleet = false;
        }

        /// <summary>
        /// Enter edit mode for a fleet.
        /// </summary>
        private void OnEditFleetClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string fleetId && DataContext is FleetViewModel vm)
            {
                FleetDisplayItem? item = vm.Fleets.FirstOrDefault(f => f.Fleet.Id == fleetId);
                if (item != null)
                {
                    item.EditName = item.Fleet.Name;
                    item.EditDescription = item.Fleet.Description ?? "";
                    item.IsEditing = true;
                }
            }
        }

        /// <summary>
        /// Save fleet edits.
        /// </summary>
        private async void OnSaveFleetClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string fleetId && DataContext is FleetViewModel vm)
            {
                FleetDisplayItem? item = vm.Fleets.FirstOrDefault(f => f.Fleet.Id == fleetId);
                if (item != null)
                {
                    await vm.UpdateFleetAsync(item);
                }
            }
        }

        /// <summary>
        /// Cancel fleet editing.
        /// </summary>
        private void OnCancelEditFleetClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string fleetId && DataContext is FleetViewModel vm)
            {
                FleetDisplayItem? item = vm.Fleets.FirstOrDefault(f => f.Fleet.Id == fleetId);
                if (item != null) item.IsEditing = false;
            }
        }

        /// <summary>
        /// Remove a fleet with confirmation.
        /// </summary>
        private async void OnRemoveFleetClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string fleetId && DataContext is FleetViewModel vm)
            {
                Window? owner = this.FindAncestorOfType<Window>();
                if (owner != null)
                {
                    ConfirmDialog dialog = new ConfirmDialog(
                        "Remove Fleet",
                        "This will permanently remove this fleet. Vessels in this fleet will not be deleted.",
                        "Remove");
                    bool confirmed = await dialog.ShowConfirmAsync(owner);
                    if (!confirmed) return;
                }

                await vm.RemoveFleetAsync(fleetId);
            }
        }

        /// <summary>
        /// Open log viewer for a captain.
        /// </summary>
        private void OnViewLogClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string captainId && DataContext is FleetViewModel vm)
            {
                Captain? captain = vm.Captains.FirstOrDefault(c => c.Id == captainId);
                string captainName = captain?.Name ?? captainId;

                LogViewerViewModel logVm = new LogViewerViewModel(captainId, captainName, vm.GetSettings());
                LogViewerWindow logWindow = new LogViewerWindow(logVm);

                Window? owner = this.FindAncestorOfType<Window>();
                if (owner != null) logWindow.Show(owner);
                else logWindow.Show();
            }
        }
    }
}
