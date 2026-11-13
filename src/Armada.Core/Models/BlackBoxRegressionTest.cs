namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// The test that would have caught a black box failure.
    /// </summary>
    public class BlackBoxRegressionTest
    {
        /// <summary>
        /// Test suite (for example Services.LandingService).
        /// </summary>
        public string Suite { get; set; } = "";

        /// <summary>
        /// The case that would have caught the failure.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRegressionTest()
        {
        }
    }
}
