using DetectorWorker.Database;
using DetectorWorker.Database.Tables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DetectorWorker.Workers
{
    /// <summary>
    /// Clean old entries from the database.
    /// </summary>
    public class CleanDb : BackgroundService
    {
        /// <summary>
        /// Worker logger.
        /// </summary>
        private readonly ILogger<CleanDb> Logger;

        /// <summary>
        /// Init the worker.
        /// </summary>
        public CleanDb(ILogger<CleanDb> logger)
        {
            this.Logger = logger;
        }

        /// <summary>
        /// Create monthly reports.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using var db = new DatabaseContext();

                var dt = DateTimeOffset.Now.AddMonths(-6);

                var removedResources = 0;
                var removedIssues = 0;
                var removedAlerts = 0;
                var removedLogs = 0;
                var removedScanResults = 0;

                // Remove deleted resources older than 6 months.
                try
                {
                    var resources = await db.Resources
                        .Where(n => n.Deleted.HasValue &&
                                    n.Deleted.Value < dt)
                        .ToListAsync(cancellationToken);

                    foreach (var resource in resources)
                    {
                        var issues = await db.Issues
                            .Where(n => n.ResourceId == resource.Id)
                            .ToListAsync(cancellationToken);

                        foreach (var issue in issues)
                        {
                            var alerts = await db.Alerts
                                .Where(n => n.IssueId == issue.Id)
                                .ToListAsync(cancellationToken);

                            if (!alerts.Any())
                            {
                                continue;
                            }

                            removedAlerts += alerts.Count;
                            db.Alerts.RemoveRange(alerts);
                        }

                        removedIssues += issues.Count;
                        db.Issues.RemoveRange(issues);

                        var scanResults = await db.ScanResults
                            .Where(n => n.ResourceId == resource.Id)
                            .ToListAsync(cancellationToken);

                        removedScanResults += scanResults.Count;
                        db.ScanResults.RemoveRange(scanResults);
                    }

                    // Remove resources.
                    removedResources += resources.Count;
                    db.Resources.RemoveRange(resources);

                    // Save.
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    this.Logger.LogCritical(ex, ex.Message);
                    await Log.LogCritical(ex.Message);
                }

                // Remove resolved issues older than 6 months.
                try
                {
                    var issues = await db.Issues
                        .Where(n => n.Resolved.HasValue &&
                                    n.Resolved.Value < dt)
                        .ToListAsync(cancellationToken);

                    // Get all alerts relevant to these issues.
                    foreach (var issue in issues)
                    {
                        var alerts = await db.Alerts
                            .Where(n => n.IssueId == issue.Id)
                            .ToListAsync(cancellationToken);

                        if (!alerts.Any())
                        {
                            continue;
                        }

                        removedAlerts += alerts.Count;
                        db.Alerts.RemoveRange(alerts);
                    }

                    // Remove issues.
                    removedIssues += issues.Count;
                    db.Issues.RemoveRange(issues);

                    // Save.
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    this.Logger.LogCritical(ex, ex.Message);
                    await Log.LogCritical(ex.Message);
                }

                // Remove log entries older than 6 months.
                try
                {
                    var logs = await db.Logs
                        .Where(n => n.Created < dt)
                        .ToListAsync(cancellationToken);

                    removedLogs += logs.Count;
                    db.Logs.RemoveRange(logs);
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    this.Logger.LogCritical(ex, ex.Message);
                    await Log.LogCritical(ex.Message);
                }

                // Remove scan results older than 6 months.
                try
                {
                    var scanResults = await db.ScanResults
                        .Where(n => n.Created < dt)
                        .ToListAsync(cancellationToken);

                    removedScanResults += scanResults.Count;
                    db.ScanResults.RemoveRange(scanResults);
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    this.Logger.LogCritical(ex, ex.Message);
                    await Log.LogCritical(ex.Message);
                }

                // Log.
                this.Logger.LogInformation($"Removed {removedResources} deleted resources, {removedIssues} resolved issues, {removedAlerts} alerts, {removedScanResults} scan results, and {removedLogs} log entries from the db.");

                // Wait a day.
                await Task.Delay(new TimeSpan(24, 0, 0), cancellationToken);
            }
        }
    }
}