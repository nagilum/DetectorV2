using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace DetectorWorker.Database.Tables
{
    [Table("ScanResults")]
    public class ScanResult
    {
        #region ORM

        [Key]
        [Column]
        public long Id { get; set; }

        [Column]
        public DateTimeOffset Created { get; set; }

        [Column]
        public DateTimeOffset Updated { get; set; }

        [Column]
        public long ResourceId { get; set; }

        [Column]
        [MaxLength(1024)]
        public string Url { get; set; }

        [Column] // Can be null
        public int? StatusCode { get; set; }

        [Column] // Can be null
        [MaxLength(32)]
        public string SslErrorCode { get; set; }

        [Column] // Can be null
        [MaxLength(128)]
        public string SslErrorMessage { get; set; }

        [Column] // Can be null
        [MaxLength(128)]
        public string ConnectingIp { get; set; }

        [Column] // Can be null
        public string ExceptionMessage { get; set; }

        #endregion

        #region Instance functions

        /// <summary>
        /// Figure out the current status based on the current result.
        /// </summary>
        /// <param name="lastResult">Include last result to compare IPs.</param>
        /// <returns>Current status.</returns>
        public string GetStatus(ScanResult lastResult)
        {
            var status = "Ok";

            // Are connecting IPs the same?
            if (lastResult != null &&
                lastResult.ConnectingIp != this.ConnectingIp)
            {
                status = "Warning";
            }

            // Was the last statuscode valid?
            var validStatusCodes = new[] { 200, 201, 203, 204 };

            if (this.StatusCode.HasValue &&
                !validStatusCodes.Contains(this.StatusCode.Value))
            {
                status = "Error";
            }

            // Do we have an SSL error?
            if (this.SslErrorCode != null)
            {
                status = "Error";
            }

            // Do we have a general error?
            if (this.ExceptionMessage != null)
            {
                status = "Error";
            }

            // Done.
            return status;
        }

        #endregion
    }
}