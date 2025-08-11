using MongoDB.Driver;

namespace Domain.DTOs.Settings
{
    public class BaseServiceSettings<T>
    {
        public IEnumerable<CreateIndexModel<T>>? IndexModels { get; set; } = null;
        public bool PublishCRUDEvents { get; set; } = true;
        public bool SendCRUDNotifications { get; set; } = false;
    }
}
