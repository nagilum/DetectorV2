using DetectorWorker.Core;
using DetectorWorker.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DetectorWorker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Load config.
            Config.Load();

            // Setup host.
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((_, services) =>
                {
                    // Scan all the resources regularly.
                    services.AddHostedService<Scanner>();

                    // Create monthly reports.
                    services.AddHostedService<MonthlyReports>();

                    // Clean old entries from the database.
                    services.AddHostedService<CleanDb>();
                })
                .Build()
                .Run();
        }   
    }
}