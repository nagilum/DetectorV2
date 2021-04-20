using DetectorWorker.Core;
using DetectorWorker.Database;
using DetectorWorker.Database.Tables;
using DetectorWorker.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var dt = DateTimeOffset.Now;

                    var report = await new DatabaseContext()
                        .MonthlyReports
                        .FirstOrDefaultAsync(n => n.Year == dt.Year &&
                                                  n.Month == dt.Month,
                            cancellationToken);

                    if (report == null)
                    {
                        // Generate a report.
                        await this.GenerateReport(dt.Year, dt.Month, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    this.Logger.LogCritical(ex, ex.Message);
                    await Log.LogCritical(ex.Message);
                }

                // Wait a day.
                await Task.Delay(new TimeSpan(24, 0, 0), cancellationToken);
            }
        }

        #region Report generating functions

        /// <summary>
        /// Generate a report.
        /// </summary>
        /// <param name="year">Year of report.</param>
        /// <param name="month">Month of report.</param>
        /// <param name="cancellationToken">Passed cancellation token.</param>
        private async Task GenerateReport(int year, int month, CancellationToken cancellationToken)
        {
            await using var db = new DatabaseContext();

            try
            {
                // Prepare dates.
                var from = new DateTimeOffset(year, month, 1, 0, 0, 0, 0, DateTimeOffset.Now.Offset);
                var to = from.AddMonths(1);

                var resources = await db.Resources
                    .Where(n => !n.Deleted.HasValue)
                    .OrderBy(n => n.Name)
                    .ToListAsync(cancellationToken);

                var allIssues = await db.Issues
                    .ToListAsync(cancellationToken);

                var allGraphData = await db.GraphData
                    .ToListAsync(cancellationToken);

                var overallUptimeString = string.Empty;
                var overallDowntimeString = string.Empty;
                var overallIssuesString = string.Empty;
                var overallAverageResponseTimeString = string.Empty;

                var sb = new StringBuilder();

                // Create Detector header.
                sb.AppendLine("<div style=\"background-color: #000; color: #fff; font-family: sans-serif; font-size: 13px; padding: 10px; text-align: left;\">");
                sb.AppendLine("<div>Detector Monthly Report</div>");
                sb.AppendLine($"<div>{from:yyyy-MM-dd} to {to:yyyy-MM-dd}</div>");
                sb.AppendLine("</div>");
                sb.AppendLine("<div>&nbsp;</div>");

                // Overall
                sb.AppendLine("<table style=\"border-left: solid 1px #666; border-right: solid 1px #666; border-top: solid 1px #666; border-collapse: collapse; font-family: sans-serif; font-size: 13px; width: 100%;\">");
                sb.AppendLine("<thead><tr>");
                sb.AppendLine("<th style=\"background-color: #000; color: #fff; padding: 10px; text-align: left; text-transform: uppercase;\">Uptime</th>");
                sb.AppendLine("<th style=\"background-color: #000; color: #fff; padding: 10px; text-align: left; text-transform: uppercase;\">Downtime</th>");
                sb.AppendLine("<th style=\"background-color: #000; color: #fff; padding: 10px; text-align: left; text-transform: uppercase;\">Issues</th>");
                sb.AppendLine("<th style=\"background-color: #000; color: #fff; padding: 10px; text-align: left; text-transform: uppercase;\">Average Response Time (ms)</th>");
                sb.AppendLine("</tr></thead>");
                sb.AppendLine("<tbody>");
                sb.AppendLine("<tr style=\"border-bottom: solid 1px #666;\">");
                sb.AppendLine("<td style=\"padding: 10px;\">{OVERALL-UPTIME}</td>");
                sb.AppendLine("<td style=\"border-left: solid 1px #666; padding: 10px;\">{OVERALL-DOWNTIME}</td>");
                sb.AppendLine("<td style=\"border-left: solid 1px #666; padding: 10px;\">{OVERALL-ISSUES}</td>");
                sb.AppendLine("<td style=\"border-left: solid 1px #666; padding: 10px;\">{OVERALL-AVGRT}</td>");
                sb.AppendLine("</tr>");
                sb.AppendLine("</tbody>");
                sb.AppendLine("</table>");
                sb.AppendLine("<div>&nbsp;</div>");

                // Setup resource table.
                sb.AppendLine("<table style=\"border-left: solid 1px #666; border-right: solid 1px #666; border-top: solid 1px #666; border-collapse: collapse; font-family: sans-serif; font-size: 13px; width: 100%;\">");
                sb.AppendLine("<thead><tr>");
                sb.AppendLine("<th style=\"background-color: #000; color: #fff; padding: 10px; text-align: left; text-transform: uppercase;\">Resource</th>");
                sb.AppendLine("<th style=\"background-color: #000; color: #fff; padding: 10px; text-align: left; text-transform: uppercase;\">Uptime</th>");
                sb.AppendLine("<th style=\"background-color: #000; color: #fff; padding: 10px; text-align: left; text-transform: uppercase;\">Downtime</th>");
                sb.AppendLine("<th style=\"background-color: #000; color: #fff; padding: 10px; text-align: left; text-transform: uppercase;\">Issues</th>");
                sb.AppendLine("<th style=\"background-color: #000; color: #fff; padding: 10px; text-align: left; text-transform: uppercase;\">Average Response Time (ms)</th>");
                sb.AppendLine("</tr></thead>");
                sb.AppendLine("<tbody>");

                // Cycle and add resources and info.
                foreach (var resource in resources)
                {
                    // Get issues.
                    var issues = new List<Issue>();

                    foreach (var issue in allIssues.Where(n => n.ResourceId == resource.Id))
                    {
                        // Started before, ended inside.
                        var add = issue.Created < from &&
                                  issue.Updated > from &&
                                  issue.Updated < to;

                        // Started inside, ended after.
                        if (issue.Created > from &&
                            issue.Created < to &&
                            issue.Updated > to)
                        {
                            add = true;
                        }

                        // Started before, ended after.
                        if (issue.Created < from &&
                            issue.Updated > to)
                        {
                            add = true;
                        }

                        // Started inside, ended inside.
                        if (issue.Created > from &&
                            issue.Updated < to)
                        {
                            add = true;
                        }

                        // Add?
                        if (add)
                        {
                            issues.Add(issue);
                        }
                    }

                    // Calculate downtime.
                    var secondsDowntime = 0;

                    foreach (var issue in issues)
                    {
                        var f = issue.Created < from
                            ? from
                            : issue.Created;

                        var t = issue.Resolved ?? issue.Updated;

                        if (t > to)
                        {
                            t = to;
                        }

                        var s = t - f;

                        secondsDowntime += (int) s.TotalSeconds;
                    }

                    var timespanDowntime = new TimeSpan(0, 0, secondsDowntime);
                    var downtime = string.Empty;

                    if (timespanDowntime.Days > 0)
                    {
                        downtime += $"{timespanDowntime.Days}d ";
                    }

                    if (timespanDowntime.Hours > 0)
                    {
                        downtime += $"{timespanDowntime.Hours}h ";
                    }

                    if (timespanDowntime.Minutes > 0)
                    {
                        downtime += $"{timespanDowntime.Minutes}m ";
                    }

                    if (timespanDowntime.Seconds > 0)
                    {
                        downtime += $"{timespanDowntime.Seconds}s ";
                    }

                    if (downtime == string.Empty)
                    {
                        downtime = "-";
                    }

                    // Calculate uptime.
                    var timespanUptimeTotal = to - from;
                    var downtimePercentage = (100D / timespanUptimeTotal.TotalSeconds) * timespanDowntime.TotalSeconds;
                    var uptimePercentage = 100D - downtimePercentage;
                    var uptimePercentageString = uptimePercentage.Equals(100)
                        ? "&gt;99.99"
                        : $"{uptimePercentage:0.00}";

                    // Calculate average response time.
                    var graphData = allGraphData
                        .FirstOrDefault(n => n.ResourceId == resource.Id);

                    var responseTime = 0D;

                    List<GraphPoint> gpl = null;

                    if (graphData != null)
                    {
                        try
                        {
                            gpl = JsonSerializer.Deserialize<List<GraphPoint>>(graphData.GraphJson)
                                  ?? new List<GraphPoint>();
                        }
                        catch
                        {
                            //
                        }
                    }

                    if (gpl != null)
                    {
                        responseTime = gpl
                            .Where(n => n.rt.HasValue &&
                                        n.dt >= from &&
                                        n.dt < to)
                            .Sum(n => n.rt.Value);

                        responseTime /= gpl.Count;
                    }

                    var responseTimeString = responseTime.Equals(0)
                        ? "-"
                        : $"{responseTime:0.00} ms";

                    // Row.
                    sb.AppendLine("<tr style=\"border-bottom: solid 1px #666;\">");
                    sb.AppendLine($"<td style=\"padding: 10px;\">{resource.Name}<br><a href=\"{resource.Url}\">{resource.Url}</a></td>");
                    sb.AppendLine($"<td style=\"border-left: solid 1px #666; padding: 10px;\">{uptimePercentageString}%</td>");
                    sb.AppendLine($"<td style=\"border-left: solid 1px #666; {(downtime != "-" ? "color: #f00; font-weight: bold;" : "")} padding: 10px;\">{downtime}</td>");
                    sb.AppendLine($"<td style=\"border-left: solid 1px #666; {(issues.Count > 0 ? "color: #f00; font-weight: bold;" : "")} padding: 10px;\">{issues.Count}</td>");
                    sb.AppendLine($"<td style=\"border-left: solid 1px #666; padding: 10px;\">{responseTimeString}</td>");
                    sb.AppendLine("</tr>");
                }

                // Add resource table footer.
                sb.AppendLine("</tbody>");
                sb.AppendLine("</table>");

                // TODO: Add footer.

                // TODO: Compile HTML and add overall values.
                var html = sb.ToString();

                html = html.Replace("{OVERALL-UPTIME}", overallUptimeString);
                html = html.Replace("{OVERALL-DOWNTIME}", overallDowntimeString);
                html = html.Replace("{OVERALL-ISSUES}", overallIssuesString);
                html = html.Replace("{OVERALL-AVGRT}", overallAverageResponseTimeString);

                // Add to db.
                var report = new MonthlyReport
                {
                    Created = DateTimeOffset.Now,
                    Year = year,
                    Month = month,
                    SentTo = Config.Get("emails", "monthlyreports"),
                    Html = html
                };

                await db.MonthlyReports.AddAsync(report, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);

                // TEMP: Save to disk.
                var path = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    $"detector-monthly-report-{year}-{month}.html");

                await File.WriteAllTextAsync(
                    path,
                    html,
                    Encoding.UTF8,
                    cancellationToken);

                // Log locally.
                this.Logger.LogInformation($"Created monthly report for {year}.{month}");

                // TODO: Send to e-mails.
            }
            catch (Exception ex)
            {
                this.Logger.LogCritical(ex, ex.Message);
                await Log.LogCritical(ex.Message);
            }
        }

        #endregion
    }
}