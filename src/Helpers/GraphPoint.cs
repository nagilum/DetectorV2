using System;

namespace DetectorWorker.Helpers
{
    public class GraphPoint
    {
        /// <summary>
        /// Created.
        /// </summary>
        public DateTimeOffset dt { get; set; }

        /// <summary>
        /// Response time (ms).
        /// </summary>
        public double? rt { get; set; }

        /// <summary>
        /// Status.
        /// </summary>
        public string st { get; set; }
    }
}