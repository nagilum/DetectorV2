using DetectorWorker.Core;
using DetectorWorker.Database.Tables;
using Microsoft.EntityFrameworkCore;

namespace DetectorWorker.Database
{
    public class DatabaseContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var connectionString = Config.Get("_cache_connectionString");

            if (connectionString == null)
            {
                connectionString = $"Data Source={Config.Get("database", "hostname")};" +
                                   $"Initial Catalog={Config.Get("database", "database")};" +
                                   $"User ID={Config.Get("database", "username")};" +
                                   $"Password={Config.Get("database", "password")};";

                Config.Set("_cache_connectionString", connectionString);
            }

            optionsBuilder.UseSqlServer(connectionString);
        }

        #region DbSets

        public DbSet<Alert> Alerts { get; set; }

        public DbSet<GraphData> GraphData { get; set; }

        public DbSet<Issue> Issues { get; set; }

        public DbSet<Log> Logs { get; set; }

        public DbSet<MonthlyReport> MonthlyReports { get; set; }

        public DbSet<Resource> Resources { get; set; }

        public DbSet<ScanResult> ScanResults { get; set; }

        #endregion
    }
}