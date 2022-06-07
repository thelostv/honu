﻿using System.Collections.Generic;
using watchtower.Models.Api;

namespace watchtower.Models.Health {

    /// <summary>
    ///     Information about the health of Honu
    /// </summary>
    public class HonuHealth {

        /// <summary>
        ///     Information about the realtime Death stream
        /// </summary>
        public List<CensusRealtimeHealthEntry> Death { get; set; } = new List<CensusRealtimeHealthEntry>();

        /// <summary>
        ///     Information about the realtime Exp stream
        /// </summary>
        public List<CensusRealtimeHealthEntry> Exp { get; set; } = new List<CensusRealtimeHealthEntry>();

        /// <summary>
        ///     Information about when Honu last had a bad realtime stream
        /// </summary>
        public List<BadHealthEntry> RealtimeHealthFailures { get; set; } = new List<BadHealthEntry>();

        /// <summary>
        ///     Information about the hosted queues in Honu
        /// </summary>
        public List<ServiceQueueCount> Queues { get; set; } = new List<ServiceQueueCount>();

    }
}