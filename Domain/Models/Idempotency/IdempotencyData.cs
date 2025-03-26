namespace Domain.Models.Idempotency
{
    /// <summary>
    /// Represents an idempotency record for ensuring operations are executed only once
    /// </summary>
    public class IdempotencyData : BaseEntity
    {
        /// <summary>
        /// Idempotency key used to identify the operation
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// JSON serialized result of the operation
        /// </summary>
        public string ResultJson { get; set; }

        /// <summary>
        /// Timestamp when the record should expire
        /// </summary>
        public DateTime ExpiresAt { get; set; }
    }
}