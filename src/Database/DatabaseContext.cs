using DetectorV2.Core;
using DetectorV2.Database.Tables;
using Microsoft.EntityFrameworkCore;

namespace DetectorV2.Database
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

        public DbSet<Resource> Resources { get; set; }

        public DbSet<ScanResult> ScanResults { get; set; }

        #endregion
    }
}