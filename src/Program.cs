using DetectorWorker.Core;
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
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>();
                })
                .Build()
                .Run();
        }   
    }
}