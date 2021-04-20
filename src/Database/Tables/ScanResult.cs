using System;
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
        public double? ResponseTimeMs { get; set; }

        [Column]
        [MaxLength(16)]
        public string Status { get; set; }

        [Column] // Can be null.
        public string ErrorMessage { get; set; }

        #endregion
    }
}