using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DetectorWorker.Workers
{
    /// <summary>
    /// Create monthly reports.
    /// </summary>
    public class MonthlyReports : BackgroundService
    {
        /// <summary>
        /// Worker logger.
        /// </summary>
        private readonly ILogger<MonthlyReports> Logger;

        /// <summary>
        /// Init the worker.
        /// </summary>
        public MonthlyReports(ILogger<MonthlyReports> logger)
        {
            this.Logger = logger;
        }

        /// <summary>
        /// Create monthly reports.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            // TODO

            //while (!cancellationToken.IsCancellationRequested)
            //{
            //}
        }
    }
}