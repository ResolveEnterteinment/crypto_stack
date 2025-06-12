using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.Models.Idempotency;

namespace Application.Interfaces
{
    public interface IIdempotencyService : IBaseService<IdempotencyData>
    {
        /// <summary>
        /// Gets the result of a previously executed operation by its idempotency key.
        /// </summary>
        /// <typeparam name="T">The return type of the operation.</typeparam>
        /// <param name="idempotencyKey">The unique key identifying the operation.</param>
        /// <returns>A tuple containing whether the result exists and the result itself if it exists.</returns>
        Task<(bool exists, T result)> GetResultAsync<T>(string idempotencyKey);

        /// <summary>
        /// Stores the result of an operation with the given idempotency key.
        /// </summary>
        /// <typeparam name="T">The type of the result.</typeparam>
        /// <param name="idempotencyKey">The unique key identifying the operation.</param>
        /// <param name="result">The result to store.</param>
        /// <param name="expiration">Optional custom expiration timespan for this record.</param>
        Task StoreResultAsync<T>(string idempotencyKey, T result, TimeSpan? expiration = null);

        /// <summary>
        /// Generates an ETag key from a given content.
        /// </summary>
        /// <typeparam name="T">The type of the result.</typeparam>
        /// <param name="content">The data object used to hash.</param>
        string GenerateETagFromContent<T>(T content);

        /// <summary>
        /// Checks if an operation with the given idempotency key has been executed.
        /// </summary>
        /// <param name="idempotencyKey">The unique key identifying the operation.</param>
        /// <returns>True if the operation has been executed, otherwise false.</returns>
        Task<bool> HasKeyAsync(string idempotencyKey);

        /// <summary>
        /// Executes an operation with idempotency guarantees.
        /// </summary>
        /// <typeparam name="T">The return type of the operation.</typeparam>
        /// <param name="idempotencyKey">The unique key identifying the operation.</param>
        /// <param name="operation">The operation to execute.</param>
        /// <param name="expiration">Optional custom expiration timespan for this record.</param>
        /// <returns>The result of the operation.</returns>
        Task<T> ExecuteIdempotentOperationAsync<T>(string idempotencyKey, Func<Task<T>> operation, TimeSpan? expiration = null);

        Task<bool> RemoveKeyAsync(string key);

        Task<ResultWrapper<long>> PurgeExpiredRecordsAsync();
    }
}
