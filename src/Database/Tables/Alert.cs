using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DetectorWorker.Database.Tables
{
    [Table("Alerts")]
    public class Alert
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
        public long IssueId { get; set; }

        [Column]
        [MaxLength(16)]
        public string Type { get; set; }

        [Column]
        [MaxLength(1024)]
        public string Url { get; set; }

        [Column]
        [MaxLength(1024)]
        public string Message { get; set; }

        [Column]
        public DateTimeOffset? PostedToSlack { get; set; }

        #endregion
    }
}