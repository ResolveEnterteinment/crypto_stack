using MongoDB.Bson;

namespace Domain.Models
{
    public class BaseEntity
    {
        public required ObjectId _id { get; set; }
        public required DateTime CreateTime { get; set; }
    }
}
