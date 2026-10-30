namespace Armada.Core.Services
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// What the suggested actions of one model alert would act on: the persona, its priority entries that select only this
    /// profile's captains, and the captains on the profile. Built by <see cref="ModelAlertActionPlanner"/>; nothing is
    /// changed by building it.
    /// </summary>
    public class ModelAlertActionPlan
    {
        /// <summary>
        /// The alert's persona when it exists and is visible, else null.
        /// </summary>
        public Persona? Persona { get; set; } = null;

        /// <summary>
        /// Indexes into the persona's CaptainPriorities of the entries that select only this profile's captains.
        /// </summary>
        public List<int> EntryIndexes { get; set; } = new List<int>();

        /// <summary>
        /// Whether the persona's default captain is on this profile.
        /// </summary>
        public bool DefaultCaptainOnProfile { get; set; } = false;

        /// <summary>
        /// Captains linked to the profile (Captain.ModelProfileId), idle ones first.
        /// </summary>
        public List<Captain> ProfileCaptains { get; set; } = new List<Captain>();

        /// <summary>
        /// The suggested actions and whether each is available.
        /// </summary>
        public List<ModelAlertSuggestedAction> Actions { get; set; } = new List<ModelAlertSuggestedAction>();
    }
}
