namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Body of PUT /api/v1/push/devices/{id}. Omitted (null) fields are left unchanged.
    /// </summary>
    public class PushDeviceUpdateRequest
    {
        #region Public-Members

        /// <summary>
        /// New device name (at most 128 characters), or null to keep it.
        /// </summary>
        public string? DeviceName { get; set; } = null;

        /// <summary>
        /// New enabled categories (an empty list mutes the device), or null to keep them.
        /// </summary>
        public List<PushCategoryEnum>? Categories { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PushDeviceUpdateRequest()
        {
        }

        #endregion
    }
}
