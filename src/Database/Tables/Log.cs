using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;

namespace DetectorWorker.Database.Tables
{
    [Table("Logs")]
    public class Log
    {
        #region ORM

        [Key]
        [Column]
        public long Id { get; set; }

        [Column]
        public DateTimeOffset Created { get; set; }

        [Column]
        public long? UserId { get; set; }

        [Column]
        public string Message { get; set; }

        [Column]
        [MaxLength(32)]
        public string Type { get; set; }

        [Column]
        [MaxLength(32)]
        public string ReferenceType { get; set; }

        [Column]
        public long? ReferenceId { get; set; }

        #endregion

        #region Database handler

        /// <summary>
        /// Save the actual entry to db.
        /// </summary>
        /// <param name="message">Log message.</param>
        /// <param name="type">Log type.</param>
        /// <param name="userId">User who made the log.</param>
        /// <param name="refType">Reference to another type.</param>
        /// <param name="refId">Reference id to alt object.</param>
        private static async Task LogToDb(
            string message,
            string type,
            long? userId = null,
            string refType = null,
            long? refId = null)
        {
            try
            {
                await using var db = new DatabaseContext();

                var entry = new Log
                {
                    Created = DateTimeOffset.Now,
                    UserId = userId,
                    Message = message,
                    Type = type,
                    ReferenceType = refType,
                    ReferenceId = refId
                };

                await db.Logs.AddAsync(entry);
                await db.SaveChangesAsync();
            }
            catch
            {
                //
            }
        }

        #endregion

        #region Log functions

        /// <summary>
        /// Save critical message to logs.
        /// </summary>
        /// <param name="message">Log message.</param>
        /// <param name="userId">User who made the log.</param>
        /// <param name="refType">Reference to another type.</param>
        /// <param name="refId">Reference id to alt object.</param>
        public static async Task LogCritical(
            string message,
            long? userId = null,
            string refType = null,
            long? refId = null)
        {
            await LogToDb(message, "critical", userId, refType, refId);
        }

        /// <summary>
        /// Save info message to logs.
        /// </summary>
        /// <param name="message">Log message.</param>
        /// <param name="userId">User who made the log.</param>
        /// <param name="refType">Reference to another type.</param>
        /// <param name="refId">Reference id to alt object.</param>
        public static async Task LogInformation(
            string message,
            long? userId = null,
            string refType = null,
            long? refId = null)
        {
            await LogToDb(message, "info", userId, refType, refId);
        }

        /// <summary>
        /// Save warning message to logs.
        /// </summary>
        /// <param name="message">Log message.</param>
        /// <param name="userId">User who made the log.</param>
        /// <param name="refType">Reference to another type.</param>
        /// <param name="refId">Reference id to alt object.</param>
        public static async Task LogWarning(
            string message,
            long? userId = null,
            string refType = null,
            long? refId = null)
        {
            await LogToDb(message, "warning", userId, refType, refId);
        }

        #endregion
    }
}
