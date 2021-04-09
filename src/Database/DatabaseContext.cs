using DetectorWorker.Core;
using DetectorWorker.Database.Tables;
using Microsoft.EntityFrameworkCore;

namespace DetectorWorker.Database
{
    public class DatabaseContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder ob)
        {
            ob.UseSqlServer(
                $"Data Source={Config.Get("database", "hostname")};" +
                $"Initial Catalog={Config.Get("database", "database")};" +
                $"User ID={Config.Get("database", "username")};" +
                $"Password={Config.Get("database", "password")};");
        }

        #region DbSets

        public DbSet<Alert> Alerts { get; set; }

        public DbSet<Issue> Issues { get; set; }

        public DbSet<Log> Logs { get; set; }

        public DbSet<Resource> Resources { get; set; }

        #endregion
    }
}