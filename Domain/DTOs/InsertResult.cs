using MongoDB.Bson;

namespace Domain.DTOs
{
    public class InsertResult
    {
        /// <summary>
        /// True if the operation was acknowledged by the server.
        /// </summary>
        public bool IsAcknowledged { get; set; }

        /// <summary>
        /// The document that was inserted.
        /// </summary>
        public BsonValue? InsertedId { get; set; }

        /// <summary>
        /// An error message, if an exception occurred.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}