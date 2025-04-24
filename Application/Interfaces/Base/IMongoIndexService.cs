using Domain.Models;
using MongoDB.Driver;

namespace Application.Interfaces.Base
{
    public interface IMongoIndexService<T> where T : BaseEntity
    {
        Task EnsureIndexesAsync(IEnumerable<CreateIndexModel<T>> indexModels);
    }
}