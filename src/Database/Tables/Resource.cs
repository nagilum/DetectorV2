using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DetectorV2.Database.Tables
{
    [Table("Resources")]
    public class Resource
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
        public DateTimeOffset? Deleted { get; set; }

        [Column]
        public DateTimeOffset? NextScan { get; set; }

        [Column]
        [MaxLength(64)]
        public string Identifier { get; set; }

        [Column]
        [MaxLength(64)]
        public string Name { get; set; }

        [Column]
        [MaxLength(1024)]
        public string Url { get; set; }

        #endregion

        #region Overrides

        /// <summary>
        /// Mostly for debugging.
        /// </summary>
        public override string ToString()
        {
            return $"{this.Identifier} {this.Url}";
        }

        #endregion
    }
}