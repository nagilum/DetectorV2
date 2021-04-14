﻿using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
        public long ResourceId { get; set; }

        [Column]
        [MaxLength(1024)]
        public string Url { get; set; }

        [Column]
        [MaxLength(32)]
        public string SslErrorCode { get; set; }

        [Column]
        [MaxLength(128)]
        public string SslErrorMessage { get; set; }

        [Column]
        public int? HttpStatusCode { get; set; }

        [Column]
        public string ConnectingIp { get; set; }

        #endregion
    }
}