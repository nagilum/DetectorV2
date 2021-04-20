using DetectorWorker.Database;
using DetectorWorker.Database.Tables;
using DetectorWorker.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DetectorWorker.Workers
{
    /// <summary>
    /// Cycle scan results and build resource graphs.
    /// </summary>
    public class BuildGraphs : BackgroundService
    {
        /// <summary>
        /// Worker logger.
        /// </summary>
        private readonly ILogger<BuildGraphs> Logger;

        /// <summary>
        /// Init the worker.
        /// </summary>
        public BuildGraphs(ILogger<BuildGraphs> logger)
        {
            this.Logger = logger;
        }

        /// <summary>
        /// Cycle scan results and build resource graphs.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                List<long> rids;

                try
                {
                    rids = await new DatabaseContext().Resources
                        .Where(n => !n.Deleted.HasValue)
                        .Select(n => n.Id)
                        .ToListAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    // Log to db.
                    await Log.LogCritical(ex);

                    // Log locally.
                    this.Logger.LogCritical(ex, ex.Message);

                    continue;
                }

                // Cycle each resource and build the graph.
                // TODO: Convert to parallel foreach.
                foreach (var id in rids)
                {
                    await this.BuildGraph(id, cancellationToken);
                }

                // Wait a day.
                await Task.Delay(new TimeSpan(0, 5, 0), cancellationToken);
            }
        }

        #region Graph building functions

        /// <summary>
        /// Build the graph points for a resource.
        /// </summary>
        /// <param name="resourceId">Id of resource.</param>
        /// <param name="cancellationToken">Passed cancellation token.</param>
        private async Task BuildGraph(long resourceId, CancellationToken cancellationToken)
        {
            try
            {
                await using var db = new DatabaseContext();

                var resource = await db.Resources
                    .FirstOrDefaultAsync(n => n.Id == resourceId, cancellationToken);

                if (resource == null)
                {
                    throw new Exception($"Resource not found: {resourceId}");
                }

                var scanResults = await db.ScanResults
                    .Where(n => n.ResourceId == resourceId)
                    .ToListAsync(cancellationToken);

                if (scanResults.Count == 0)
                {
                    return;
                }

                this.Logger.LogInformation($"[{resource.Identifier}] [BUILDING GRAPH] {resource.Url}");

                var graphData = await db.GraphData
                                    .FirstOrDefaultAsync(n => n.ResourceId == resourceId, cancellationToken)
                                ?? new GraphData
                                {
                                    Created = DateTimeOffset.Now,
                                    ResourceId = resourceId
                                };

                var json = graphData.GraphJson ?? "[]";
                var list = JsonSerializer.Deserialize<List<GraphPoint>>(json)
                           ?? new List<GraphPoint>();

                foreach (var scanResult in scanResults)
                {
                    if (list.Any(n => n.dt == scanResult.Created))
                    {
                        continue;
                    }

                    var graphPoint = new GraphPoint
                    {
                        dt = scanResult.Created,
                        rt = scanResult.ResponseTimeMs,
                        st = scanResult.ErrorMessage ?? "Ok"
                    };

                    list.Add(graphPoint);
                }

                var threshold = DateTimeOffset.Now.AddDays(-2);

                list = list
                    .Where(n => n.dt > threshold)
                    .OrderByDescending(n => n.dt)
                    .ToList();

                graphData.GraphJson = JsonSerializer.Serialize(list);
                graphData.Updated = DateTimeOffset.Now;

                if (graphData.Id == 0)
                {
                    await db.GraphData.AddAsync(graphData, cancellationToken);
                }

                await db.SaveChangesAsync(cancellationToken);

                db.ScanResults.RemoveRange(scanResults);

                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await Log.LogCritical(ex);
            }
        }

        #endregion
    }
}