using AspNetCore.Identity.Mongo.Model;

namespace Domain.Models.Authentication
{
    public class ApplicationUser : MongoUser<Guid>
    {
        public string Fullname { get; set; } = string.Empty;
    }
}
