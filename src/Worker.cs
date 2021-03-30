using DetectorWorker.Core;
using DetectorWorker.Database;
using DetectorWorker.Database.Tables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DetectorWorker
{
    public class Worker : BackgroundService
    {
        /// <summary>
        /// Worker logger.
        /// </summary>
        private readonly ILogger<Worker> Logger;

        /// <summary>
        /// Init the worker.
        /// </summary>
        public Worker(ILogger<Worker> logger)
        {
            Logger = logger;
        }

        /// <summary>
        /// Check cycles.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Get a list of all resources up for a new scan.
                List<long> ids;

                try
                {
                    ids = await new DatabaseContext()
                        .Resources
                        .Where(n => !n.Deleted.HasValue)
                        .Where(n => n.NextScan == null ||
                                    n.NextScan <= DateTimeOffset.Now)
                        .Select(n => n.Id)
                        .ToListAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    // Log locally.
                    Logger.LogCritical(ex, ex.Message);

                    // Something went wrong with the db-get. Wait and try again.
                    await Task.Delay(new TimeSpan(0, 0, 30), cancellationToken);
                    continue;
                }

                // No resources are up for a new scan, wait for next round.
                if (!ids.Any())
                {
                    await Task.Delay(new TimeSpan(0, 0, 30), cancellationToken);
                    continue;
                }

                // Cycle and scan each resource.
                foreach (var id in ids)
                {
                    await this.ScanResource(id, cancellationToken);
                }

                // All resources scanned, wait 10 sec and go at it again.
                await Task.Delay(new TimeSpan(0, 0, 10), cancellationToken);
            }
        }

        #region Resource scanning functions

        /// <summary>
        /// Scan a resource.
        /// </summary>
        /// <param name="id">Id of the resource to scan.</param>
        /// <param name="cancellationToken">Passed cancellation token.</param>
        private async Task ScanResource(long id, CancellationToken cancellationToken)
        {
            await using var db = new DatabaseContext();

            Resource resource;

            var nextScanSeconds = 60;

            try
            {
                resource = await db.Resources
                    .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
            }
            catch (Exception ex)
            {
                // Log locally.
                Logger.LogCritical(ex, ex.Message);

                // Exit stage.
                return;
            }

            // Log locally.
            Logger.LogInformation($"[{resource.Identifier}] [SCAN] {resource.Url}");

            // Get the last result.
            ScanResult lastResult;

            try
            {
                lastResult = await db.ScanResults
                    .Where(n => n.ResourceId == resource.Id)
                    .OrderByDescending(n => n.Updated)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Log locally.
                Logger.LogCritical(ex, ex.Message);

                // Set last result as null, for ease.
                lastResult = null;
            }

            // Make request!
            var result = new ScanResult
            {
                Created = DateTimeOffset.Now,
                Updated = DateTimeOffset.Now,
                ResourceId = resource.Id,
                Url = resource.Url
            };

            SslPolicyErrors? sslPolicyErrors = null;

            try
            {
                // Prepare
                var uri = new Uri(resource.Url);

                // Resolve IP of connection.
                var hostEntry = await Dns.GetHostEntryAsync(uri.DnsSafeHost);

                if (hostEntry.AddressList.Length > 0)
                {
                    result.ConnectingIp = hostEntry.AddressList[0].ToString();
                }

                // Verify SSL?
                if (resource.Url.StartsWith("https", StringComparison.InvariantCultureIgnoreCase))
                {
                    var tcpClient = new TcpClient(uri.DnsSafeHost, 443);
                    var sslStream = new SslStream(
                        tcpClient.GetStream(),
                        false,
                        delegate(
                            object _,
                            X509Certificate _,
                            X509Chain _,
                            SslPolicyErrors errors)
                        {
                            sslPolicyErrors = errors;
                            return errors == SslPolicyErrors.None;
                        },
                        null);

                    await sslStream.AuthenticateAsClientAsync(uri.DnsSafeHost);
                }

                // Create connection and try to get HTTP.
                if (!(WebRequest.Create(resource.Url) is HttpWebRequest req))
                {
                    throw new Exception($"Unable to create HttpWebRequest for {resource.Url}");
                }

                req.Timeout = 10 * 1000; // 10 seconds.
                req.Method = "GET";
                req.UserAgent = "Detector/1.0.0";
                req.AllowAutoRedirect = false;

                // Get response.
                if (!(req.GetResponse() is HttpWebResponse res))
                {
                    throw new Exception($"Could not get HttpWebResponse from HttpWebRequest for {resource.Url}");
                }

                // Get HTTP status code.
                result.StatusCode = (int) res.StatusCode;

                var validStatusCodes = new[] { 200, 201, 203, 204 };

                if (!validStatusCodes.Contains((int) res.StatusCode))
                {
                    nextScanSeconds = 10;
                }
            }
            catch (WebException ex)
            {
                result.ExceptionMessage = ex.Message;
                nextScanSeconds = 10;

                try
                {
                    if (!(ex.Response is HttpWebResponse res))
                    {
                        throw new Exception($"Unable to get HttpWebResponse from WebException for {resource.Url}");
                    }

                    // Get HTTP status code.
                    result.StatusCode = (int) res.StatusCode;
                }
                catch
                {
                    //
                }
            }
            catch (Exception ex)
            {
                result.ExceptionMessage = ex.Message;
                nextScanSeconds = 10;
            }

            // Analyze SSL cert errors.
            if (sslPolicyErrors == null ||
                sslPolicyErrors == SslPolicyErrors.None)
            {
                // Do nothing, but this skips the rest of the if-else-checks.
            }
            else if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
            {
                result.SslErrorCode = "CHAIN_ERROR";
                result.SslErrorMessage = "Remote Certificate Chain Errors";
                result.ExceptionMessage = null;
            }
            else if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateNameMismatch)
            {
                result.SslErrorCode = "NAME_MISMATCH";
                result.SslErrorMessage = "Remote Certificate Name Mismatch";
                result.ExceptionMessage = null;
            }
            else if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateNotAvailable)
            {
                result.SslErrorCode = "NOT_AVAILABLE";
                result.SslErrorMessage = "Remote Certificate Not Available";
                result.ExceptionMessage = null;
            }

            // Check how we're storing the results.
            var saveNew = false;

            if (lastResult != null &&
                this.AreResultsTheSame(lastResult, result))
            {
                lastResult.Updated = DateTimeOffset.Now;
            }
            else
            {
                saveNew = true;
            }

            try
            {
                if (saveNew)
                {
                    await db.ScanResults.AddAsync(result, cancellationToken);
                }

                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Log locally.
                Logger.LogCritical(ex, ex.Message);
            }

            // Update resource and set when the resource should be scanned next.
            resource.NextScan = DateTimeOffset.Now.AddSeconds(nextScanSeconds);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Log locally.
                Logger.LogCritical(ex, ex.Message);
            }

            // Create an alert, if needed.
            await this.CreateAlert(
                resource,
                lastResult,
                result,
                cancellationToken);
        }

        /// <summary>
        /// Check if the two results have the same values.
        /// </summary>
        private bool AreResultsTheSame(ScanResult lastResult, ScanResult result)
        {
            if (lastResult == null)
            {
                return false;
            }

            return lastResult.StatusCode == result.StatusCode &&
                   lastResult.SslErrorCode == result.SslErrorCode &&
                   lastResult.SslErrorMessage == result.SslErrorMessage &&
                   lastResult.ConnectingIp == result.ConnectingIp &&
                   lastResult.ExceptionMessage == result.ExceptionMessage;
        }

        /// <summary>
        /// Check if we need to create a new alert for this error.
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="lastResult"></param>
        /// <param name="result"></param>
        /// <param name="cancellationToken">Passed cancellation token.</param>
        private async Task CreateAlert(Resource resource, ScanResult lastResult, ScanResult result, CancellationToken cancellationToken)
        {
            var rid = result.Id > 0
                ? result.Id
                : lastResult?.Id;

            // Incorrect HTTP status code?
            //
            // Valid status codes are:
            //  - 200 Ok
            //  - 201 Created
            //  - 203 Partial Information
            //  - 204 No Response (no body)
            var validStatusCodes = new[] {200, 201, 203, 204};
            bool? isSuccess = null;

            if (result.StatusCode.HasValue)
            {
                isSuccess = validStatusCodes.Contains(result.StatusCode.Value);
            }

            if (isSuccess.HasValue &&
                isSuccess.Value == false)
            {
                // BAD

                await this.CreateAlert(
                    resource,
                    rid,
                    "negative",
                    resource.Url,
                    $"Got invalid HTTP status code: {result.StatusCode.Value}",
                    cancellationToken);
            }

            // Success HTTP status code, but different than last time?
            if (isSuccess.HasValue &&
                lastResult != null)
            {
                var isLastResultSuccess = lastResult.StatusCode.HasValue &&
                                          validStatusCodes.Contains(lastResult.StatusCode.Value);

                if (isLastResultSuccess != isSuccess.Value)
                {
                    // GOOD

                    await this.CreateAlert(
                        resource,
                        rid,
                        "positive",
                        resource.Url,
                        "HTTP status code is now valid!",
                        cancellationToken);
                }
            }

            // SSL cert error?
            if (result.SslErrorCode != null)
            {
                // BAD

                await this.CreateAlert(
                    resource,
                    rid,
                    "negative",
                    resource.Url,
                    $"SSL cert error: {result.SslErrorCode} - {result.SslErrorMessage}",
                    cancellationToken);
            }

            // No SSL cert errors, but different than last time?
            if (result.SslErrorCode == null &&
                lastResult?.SslErrorCode != null)
            {
                // GOOD

                await this.CreateAlert(
                    resource,
                    rid,
                    "positive",
                    resource.Url,
                    "The SSL cert error has been resolved!",
                    cancellationToken);
            }

            // Different connecting IP?
            if (lastResult != null &&
                lastResult.ConnectingIp != result.ConnectingIp)
            {
                // WARNING

                await this.CreateAlert(
                    resource,
                    rid,
                    "warning",
                    resource.Url,
                    $"Different connecting IP. Used to be {lastResult.ConnectingIp} but now it's {result.ConnectingIp}. This might not be erroneous, but should be verified.",
                    cancellationToken);
            }

            // Exception message?
            if (result.ExceptionMessage != null)
            {
                // BAD

                await this.CreateAlert(
                    resource,
                    rid,
                    "negative",
                    resource.Url,
                    $"Unhandled error while checking resource: {result.ExceptionMessage}",
                    cancellationToken);
            }

            // Exception message dissapeared?
            if (lastResult?.ExceptionMessage != null &&
                result.ExceptionMessage == null)
            {
                // GOOD

                await this.CreateAlert(
                    resource,
                    rid,
                    "positive",
                    resource.Url,
                    "The previous error has been resolved!",
                    cancellationToken);
            }
        }

        /// <summary>
        /// Create the db entry for the alert.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="scanResultId">Id of scan result.</param>
        /// <param name="type">Type of alert.</param>
        /// <param name="url">Url of resource.</param>
        /// <param name="message">Message in the alert.</param>
        /// <param name="cancellationToken">Passed cancellation token.</param>
        private async Task CreateAlert(Resource resource, long? scanResultId, string type, string url, string message, CancellationToken cancellationToken)
        {
            try
            {
                await using var db = new DatabaseContext();

                var entry = new Alert
                {
                    Created = DateTimeOffset.Now,
                    Updated = DateTimeOffset.Now,
                    ResourceId = resource.Id,
                    ScanResultId = scanResultId,
                    Type = type,
                    Url = url,
                    Message = message
                };

                // Check if the same alert has been triggered the last 24 hours, if so, skip it!
                var prevEntry = await db.Alerts
                    .FirstOrDefaultAsync(n => n.ResourceId == entry.ResourceId &&
                                              n.Type == entry.Type &&
                                              n.Url == entry.Url &&
                                              n.Message == entry.Message &&
                                              n.Created > DateTimeOffset.Now.AddHours(-24),
                        cancellationToken);

                if (prevEntry != null)
                {
                    // We already have the same alert out there in the last 24 hours.
                    // Let's just update the previous one and save it.
                    prevEntry.Updated = DateTimeOffset.Now;
                    await db.SaveChangesAsync(cancellationToken);

                    return;
                }

                // Save to db.
                await db.Alerts.AddAsync(entry, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);

                // Log locally.
                this.LogAlert(resource, entry);

                // Post the alert to Slack.
                await this.PostToSlack(resource, entry, cancellationToken);
            }
            catch (Exception ex)
            {
                // Log locally.
                Logger.LogCritical(ex, ex.Message);
            }
        }

        /// <summary>
        /// Log the alert locally.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="alert">Alert to log.</param>
        private void LogAlert(Resource resource, Alert alert)
        {
            var message = $"[{resource.Identifier}] [ALERT] [{alert.Url}] {alert.Message}";

            switch (alert.Type)
            {
                case "positive":
                    Logger.LogInformation(message);
                    break;

                case "negative":
                    Logger.LogError(message);
                    break;

                case "warning":
                    Logger.LogWarning(message);
                    break;

                case "neutral":
                    Logger.LogInformation(message);
                    break;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="alert"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task PostToSlack(Resource resource, Alert alert, CancellationToken cancellationToken)
        {
            var url = Config.Get("slack", "url");

            if (url == null)
            {
                Logger.LogWarning("No Slack url in config under 'slack.url'!");
                return;
            }

            var color = string.Empty;
            var title = string.Empty;

            switch (alert.Type)
            {
                case "positive":
                    color = "#009700";
                    title = "Ok";

                    break;

                case "negative":
                    color = "#970000";
                    title = "Error";

                    break;

                case "warning":
                    color = "#979700";
                    title = "Warning";

                    break;

                case "nautral":
                    color = "#979797";
                    title = "Info";

                    break;
            }

            try
            {
                if (!(WebRequest.Create(url) is HttpWebRequest req))
                {
                    throw new Exception($"Unable to create HttpWebRequest from {url}");
                }

                req.Method = "POST";
                req.UserAgent = "Detector/1.0.0";

                var text =
                    $"{resource.Url}{Environment.NewLine}" +
                    $"{alert.Message}{Environment.NewLine}";

                var obj = new
                {
                    attachments = new[] {
                        new {
                            text,
                            color,
                            title
                        }
                    }
                };

                var json = JsonSerializer.Serialize(
                    obj,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                var bytes = Encoding.UTF8.GetBytes(json);
                var stream = req.GetRequestStream();

                await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);

                stream.Close();

                req.GetResponse();
            }
            catch (Exception ex)
            {
                Logger.LogCritical(ex, ex.Message);
            }
        }

        #endregion
    }
}