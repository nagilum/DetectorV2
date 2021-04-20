using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DetectorWorker.Database.Tables
{
    [Table("MonthlyReports")]
    public class MonthlyReport
    {
        #region ORM

        [Key]
        [Column]
        public long Id { get; set; }

        [Column]
        public DateTimeOffset Created { get; set; }

        [Column]
        public int Year { get; set; }

        [Column]
        public int Month { get; set; }

        [Column]
        public string SentTo { get; set; }

        [Column]
        public string Html { get; set; }

        #endregion
    }
}