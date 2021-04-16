using DetectorWorker.Core;
using DetectorWorker.Database;
using DetectorWorker.Database.Tables;
using DetectorWorker.Exceptions;
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

namespace DetectorWorker.Workers
{
    /// <summary>
    /// Scan all the resources regularly.
    /// </summary>
    public class Scanner : BackgroundService
    {
        /// <summary>
        /// Worker logger.
        /// </summary>
        private readonly ILogger<Scanner> Logger;

        /// <summary>
        /// Init the worker.
        /// </summary>
        public Scanner(ILogger<Scanner> logger)
        {
            this.Logger = logger;
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
                    this.Logger.LogCritical(ex, ex.Message);

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

                this.Logger.LogInformation("Scans complete. Waiting for next cycle..");

                // All resources scanned, wait 10 sec and go at it again.
                await Task.Delay(new TimeSpan(0, 0, 10), cancellationToken);
            }
        }

        #region Resource scanning functions

        /// <summary>
        /// Scan a resource.
        /// </summary>
        /// <param name="id">Id of resource.</param>
        /// <param name="cancellationToken">Passed cancellation token.</param>
        private async Task ScanResource(long id, CancellationToken cancellationToken)
        {
            await using var db = new DatabaseContext();

            Resource resource;

            try
            {
                resource = await db.Resources
                    .FirstOrDefaultAsync(n => !n.Deleted.HasValue &&
                                              n.Id == id,
                        cancellationToken);

                if (resource == null)
                {
                    throw new Exception($"Resource not found: {id}");
                }
            }
            catch (Exception ex)
            {
                // Log locally.
                this.Logger.LogCritical(ex, ex.Message);

                // Log to db.
                await Log.LogCritical(ex.Message);

                // Exit stage.
                return;
            }

            // Prepare new scan result.
            var scanResult = new ScanResult
            {
                Created = DateTimeOffset.Now,
                ResourceId = resource.Id,
                Url = resource.Url
            };

            string issueType;
            string issueMessage;

            // Log locally.
            this.Logger.LogInformation($"[{resource.Identifier}] [SCAN] {resource.Url}");

            // Perform tests.
            try
            {
                // Get connecting IP.
                scanResult.ConnectingIp = await ResolveConnectingIp(resource.Url);

                if (scanResult.ConnectingIp == null)
                {
                    throw new UnableToResolveIpException($"Unable to resolve IP for {resource.Url}");
                }

                // Does the resource have the connecting IP saved?
                if (resource.ConnectingIp == null)
                {
                    resource.ConnectingIp = scanResult.ConnectingIp;
                    await db.SaveChangesAsync(cancellationToken);
                }

                // Verify connecting IP.
                if (resource.ConnectingIp != scanResult.ConnectingIp)
                {
                    throw new InvalidConnectingIpException(
                        $"Resource and scan IP mismatch. Resource says {resource.ConnectingIp}. Scan says {scanResult.ConnectingIp}",
                        scanResult.ConnectingIp);
                }

                // Check SSL cert.
                await CheckSslCert(resource.Url);

                // Check HTTP status code.
                AttemptGetRequest(resource.Url);

                // If we arrive here, all issues must be resolved!
                var unresolvedIssues = await db.Issues
                    .Where(n => n.ResourceId == resource.Id &&
                                !n.Resolved.HasValue)
                    .ToListAsync(cancellationToken);

                foreach (var issue in unresolvedIssues)
                {
                    // Set it as resolved.
                    issue.Updated = DateTimeOffset.Now;
                    issue.Resolved = DateTimeOffset.Now;

                    await db.SaveChangesAsync(cancellationToken);

                    // Create an alert.
                    await CreateAlert(resource, issue, cancellationToken);
                }

                // Log to db.
                await Log.LogInformation("Everything is ok.", refType: "resource", refId: resource.Id);

                // Update resource.
                resource.LastScan = DateTimeOffset.Now;
                resource.NextScan = DateTimeOffset.Now.AddSeconds(60);
                resource.Status = "Ok";

                await db.SaveChangesAsync(cancellationToken);

                return;
            }
            catch (SslException ex)
            {
                // Set result.
                scanResult.SslErrorCode = ex.Code;
                scanResult.SslErrorMessage = ex.Message;

                // Create issue message.
                issueType = "ssl_error";
                issueMessage = $"SSL Error: {ex.Code} - {ex.Message}";
            }
            catch (InvalidHttpStatusCodeException ex)
            {
                // Set result.
                scanResult.HttpStatusCode = ex.HttpStatusCode;

                // Create issue message.
                issueType = "invalid_http_status_code";
                issueMessage = ex.Message;
            }
            catch (InvalidConnectingIpException ex)
            {
                // Create issue message.
                issueType = "invalid_connecting_ip";
                issueMessage = ex.Message;
            }
            catch (UnableToResolveIpException ex)
            {
                // Create issue message.
                issueType = "unable_to_resolve_ip";
                issueMessage = ex.Message;
            }
            catch (Exception ex)
            {
                // Create issue message.
                issueType = "unhandled_exception";
                issueMessage = ex.Message;
            }

            // Save scan result.
            try
            {
                await db.ScanResults.AddAsync(scanResult, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await Log.LogCritical(ex.Message, refType: "resource", refId: resource.Id);
            }

            // Log locally.
            this.Logger.LogCritical($"[{resource.Identifier}] {issueMessage}");

            // Log to db.
            await Log.LogCritical(issueMessage, refType: "resource", refId: resource.Id);

            // Do we already have an unresolved issue about this?
            var createNewIssue = true;

            try
            {
                var issue = await db.Issues
                    .FirstOrDefaultAsync(n => n.ResourceId == resource.Id &&
                                              !n.Resolved.HasValue &&
                                              n.IssueType == issueType,
                        cancellationToken);

                if (issue != null)
                {
                    createNewIssue = false;
                    issue.Updated = DateTimeOffset.Now;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                await Log.LogCritical(ex.Message, refType: "resource", refId: resource.Id);
            }

            if (!createNewIssue)
            {
                return;
            }

            // Create a new issue.
            try
            {
                var issue = new Issue
                {
                    Created = DateTimeOffset.Now,
                    Updated = DateTimeOffset.Now,
                    ResourceId = resource.Id,
                    Url = resource.Url,
                    IssueType = issueType,
                    Message = issueMessage
                };

                await db.Issues.AddAsync(issue, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);

                // Create alert.
                await CreateAlert(resource, issue, cancellationToken);
            }
            catch (Exception ex)
            {
                await Log.LogCritical(ex.Message, refType: "resource", refId: resource.Id);
            }
        }

        /// <summary>
        /// Attempt to resolve the resource domain IP.
        /// </summary>
        /// <param name="url">URL to get domain from.</param>
        /// <returns>Resolved IP.</returns>
        /// <exception cref="Exception">Throw if there is an unknown error.</exception>
        private async Task<string> ResolveConnectingIp(string url)
        {
            var uri = new Uri(url);
            var hostEntry = await Dns.GetHostEntryAsync(uri.DnsSafeHost);

            return hostEntry.AddressList.Length > 0
                ? hostEntry.AddressList[0].ToString()
                : null;
        }

        /// <summary>
        /// Attempt to verify the remote SSL cert.
        /// </summary>
        /// <param name="url">URL, for verification.</param>
        /// <exception cref="SslException">Thrown if there is a problem with the remote SSL cert.</exception>
        /// <exception cref="Exception">Throw if there is an unknown error.</exception>
        private async Task CheckSslCert(string url)
        {
            if (!url.StartsWith("https", StringComparison.InvariantCultureIgnoreCase))
            {
                return;
            }

            var uri = new Uri(url);

            SslPolicyErrors? sslPolicyErrors = null;

            try
            {
                var tcpClient = new TcpClient(uri.DnsSafeHost, 443);
                var sslStream = new SslStream(
                    tcpClient.GetStream(),
                    false,
                    delegate (
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
            catch
            {
                //
            }

            if (!sslPolicyErrors.HasValue ||
                sslPolicyErrors == SslPolicyErrors.None)
            {
                return;
            }

            string code;
            string message;

            switch (sslPolicyErrors)
            {
                case SslPolicyErrors.RemoteCertificateChainErrors:
                    code = "CHAIN_ERRORS";
                    message = "Remote Certificate Chain Errors";
                    break;

                case SslPolicyErrors.RemoteCertificateNameMismatch:
                    code = "NAME_MISMATCH";
                    message = "Remote Certificate Name Mismatch";
                    break;

                case SslPolicyErrors.RemoteCertificateNotAvailable:
                    code = "NOT_AVAILABLE";
                    message = "Remote Certificate Not Available";
                    break;

                default:
                    return;
            }

            throw new SslException(message, code);
        }

        /// <summary>
        /// Attempt to connect to the resource URL and get a HTTP status code.
        /// </summary>
        /// <param name="url">URL to connect to.</param>
        /// <exception cref="WebException">Throw if the request redirects or crashes.</exception>
        /// <exception cref="Exception">Throw if there is an unknown error.</exception>
        private void AttemptGetRequest(string url)
        {
            try
            {
                if (!(WebRequest.Create(url) is HttpWebRequest req))
                {
                    throw new Exception($"Unable to create HttpWebRequest for {url}");
                }

                req.Timeout = 10 * 1000; // 10 seconds.
                req.Method = "GET";
                req.UserAgent = "Detector/1.0.0";
                req.AllowAutoRedirect = false;

                // Get response.
                if (!(req.GetResponse() is HttpWebResponse res))
                {
                    throw new Exception($"Could not get HttpWebResponse from HttpWebRequest for {url}");
                }

                // Get HTTP status code.
                var code = (int)res.StatusCode;
                var validStatusCodes = new[] { 200, 201, 203, 204 };

                if (validStatusCodes.Contains(code))
                {
                    return;
                }

                throw new InvalidHttpStatusCodeException($"Invalid HTTP status code {code}", code);
            }
            catch (WebException ex)
            {
                if (!(ex.Response is HttpWebResponse res))
                {
                    throw new Exception($"Unable to get HttpWebResponse from WebException for {url}");
                }

                // Get HTTP status code.
                var code = (int)res.StatusCode;

                throw new InvalidHttpStatusCodeException($"Invalid HTTP status code {code}", code);
            }
        }

        /// <summary>
        /// Create alert object and distribute to various medias.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="issue">Issue.</param>
        /// <param name="cancellationToken">Passed cancellation token.</param>
        private async Task CreateAlert(Resource resource, Issue issue, CancellationToken cancellationToken)
        {
            try
            {
                await using var db = new DatabaseContext();

                var alert = new Alert
                {
                    Created = DateTimeOffset.Now,
                    Updated = DateTimeOffset.Now,
                    ResourceId = resource.Id,
                    IssueId = issue.Id,
                    Type = issue.Resolved.HasValue
                        ? "positive"
                        : "negative",
                    Url = issue.Url,
                    Message = issue.Message
                };

                await db.Alerts.AddAsync(alert, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);

                // Post to Slack.
                await this.PostToSlack(issue, alert, cancellationToken);

                alert.PostedToSlack = DateTimeOffset.Now;
                await db.SaveChangesAsync(cancellationToken);

                // TODO: Send as e-mail.
            }
            catch (Exception ex)
            {
                // Log locally.
                this.Logger.LogCritical(ex, ex.Message);

                // Log to db.
                await Log.LogCritical(
                    ex.Message,
                    refType: "issue",
                    refId: issue.Id);
            }
        }

        /// <summary>
        /// Post a message to Slack.
        /// </summary>
        /// <param name="alert">Alert to post.</param>
        /// <param name="issue">Issue alert is attached to.</param>
        /// <param name="cancellationToken">Passed cancellation token.</param>
        private async Task PostToSlack(Issue issue, Alert alert, CancellationToken cancellationToken)
        {
            try
            {
                var url = Config.Get("slack", "url");

                if (url == null)
                {
                    throw new Exception("No Slack url in config under 'slack.url'!");
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

                if (!(WebRequest.Create(url) is HttpWebRequest req))
                {
                    throw new Exception($"Unable to create HttpWebRequest from {url}");
                }

                req.Method = "POST";
                req.UserAgent = "Detector/1.0.0";
                req.ContentType = "application/json";

                var fixedStr = issue.Resolved.HasValue
                    ? "[FIXED] "
                    : string.Empty;

                var text =
                    $"{alert.Url}{Environment.NewLine}" +
                    $"{fixedStr}{alert.Message}{Environment.NewLine}";

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
                // Log locally.
                this.Logger.LogCritical(ex, ex.Message);

                // Log to db.
                await Log.LogCritical(
                    ex.Message,
                    refType: "alert",
                    refId: alert.Id);
            }
        }

        #endregion
    }
}