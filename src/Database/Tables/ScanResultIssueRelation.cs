using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DetectorWorker.Database.Tables
{
    [Table("ScanResult_Issue_Relations")]
    public class ScanResultIssueRelation
    {
        #region ORM

        [Key]
        [Column]
        public long Id { get; set; }

        [Column]
        public long ScanResultId { get; set; }

        [Column]
        public long IssueId { get; set; }

        #endregion
    }
}