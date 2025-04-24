using Application.Interfaces;
using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.Events;
using Domain.Events.Entity;
using Domain.Exceptions;
using Domain.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Reflection;

namespace Infrastructure.Services.Base
{
    public abstract class BaseService<T> : IBaseService<T> where T : BaseEntity
    {
        protected readonly ICrudRepository<T> Repository;
        protected readonly ICacheService<T> CacheService;
        protected readonly IMongoIndexService<T> IndexService;
        protected readonly ILogger Logger;
        protected readonly IEventService? EventService;

        private static readonly IReadOnlySet<string> _validPropertyNames;

        static BaseService()
        {
            _validPropertyNames = typeof(T)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        protected BaseService(
            ICrudRepository<T> repository,
            ICacheService<T> cacheService,
            IMongoIndexService<T> indexService,
            ILogger logger,
            IEventService? eventService = null,
            IEnumerable<CreateIndexModel<T>>? indexModels = null)
        {
            Repository = repository;
            CacheService = cacheService;
            IndexService = indexService;
            Logger = logger;
            EventService = eventService;

            if (indexModels is not null)
                _ = IndexService.EnsureIndexesAsync(indexModels);
        }

        public virtual Task<ResultWrapper<T>> GetByIdAsync(Guid id, CancellationToken ct = default)
            => FetchCached(
                CacheService.GetCacheKey(id),
                () => Repository.GetByIdAsync(id, ct),
                TimeSpan.FromMinutes(5),
                () => new ResourceNotFoundException(typeof(T).Name, id.ToString())
            );

        public virtual Task<ResultWrapper<T>> GetOneAsync(FilterDefinition<T> filter, CancellationToken ct = default)
            => FetchCached(
                CacheService.GetFilterCacheKey() + ":one:" + filter,
                () => Repository.GetOneAsync(filter, ct),
                TimeSpan.FromMinutes(5),
                () => new KeyNotFoundException($"{typeof(T).Name} not found with specified filter.")
            );

        public virtual Task<ResultWrapper<List<T>>> GetManyAsync(FilterDefinition<T> filter, CancellationToken ct = default)
            => FetchCached(
                CacheService.GetFilterCacheKey() + ":many:" + filter,
                () => Repository.GetAllAsync(filter, ct),
                TimeSpan.FromMinutes(5),
                () => new KeyNotFoundException($"No {typeof(T).Name} entities found with specified filter.")
            );

        public virtual Task<ResultWrapper<List<T>>> GetAllAsync(CancellationToken ct = default)
            => FetchCached(
                CacheService.GetCollectionCacheKey(),
                () => Repository.GetAllAsync(null, ct),
                TimeSpan.FromMinutes(5),
                () => new KeyNotFoundException($"{typeof(T).Name} collection is empty.")
            );

        public virtual Task<ResultWrapper<CrudResult>> InsertAsync(T entity, CancellationToken ct = default)
            => SafeExecute(async () =>
            {
                Logger.LogInformation("Inserting {EntityType} {Id}", typeof(T).Name, entity.Id);
                CacheService.Invalidate(CacheService.GetCollectionCacheKey());
                var crudResult = await Repository.InsertAsync(entity, ct);  // returns CrudResult with AffectedIds, etc.
                if (crudResult == null || !crudResult.IsSuccess)
                    throw new DatabaseException(crudResult.ErrorMessage!);

                var id = crudResult.AffectedIds.First();
                if (EventService is not null)
                    await EventService.Publish(new EntityCreatedEvent<T>(id, entity));
                return crudResult;
            });


        public virtual Task<ResultWrapper<CrudResult>> UpdateAsync(Guid id, object fields, CancellationToken ct = default)
            => SafeExecute(async () =>
            {
                Logger.LogInformation("Updating {EntityType} {Id}", typeof(T).Name, id);
                CacheService.Invalidate(CacheService.GetCacheKey(id));
                CacheService.Invalidate(CacheService.GetCollectionCacheKey());

                // ensure the entity exists (will throw if not)
                var toUpdate = await Repository.GetByIdAsync(id, ct)
                    ?? throw new ResourceNotFoundException(typeof(T).Name, id.ToString());

                var crudResult = await Repository.UpdateAsync(id, fields, ct);
                if (!crudResult.IsSuccess)
                    throw new DatabaseException(crudResult.ErrorMessage!);

                var updated = await Repository.GetByIdAsync(id, ct);
                await EventService?.Publish(new EntityUpdatedEvent<T>(id, updated!));
                return crudResult;
            });

        public virtual Task<ResultWrapper<CrudResult>> DeleteAsync(Guid id, CancellationToken ct = default)
            => SafeExecute(async () =>
            {
                Logger.LogInformation("Deleting {EntityType} {Id}", typeof(T).Name, id);
                CacheService.Invalidate(CacheService.GetCacheKey(id));
                CacheService.Invalidate(CacheService.GetCollectionCacheKey());

                // fetch so we can emit the deleted entity in the event
                var toDelete = await Repository.GetByIdAsync(id, ct)
                    ?? throw new ResourceNotFoundException(typeof(T).Name, id.ToString());

                var crudResult = await Repository.DeleteAsync(id, ct);
                if (!crudResult.IsSuccess)
                    throw new DatabaseException(crudResult.ErrorMessage!);

                await EventService?.Publish(new EntityDeletedEvent<T>(id, toDelete));
                return crudResult;
            });

        public virtual Task<ResultWrapper<CrudResult<T>>> DeleteManyAsync(FilterDefinition<T> filter, CancellationToken ct = default)
            => SafeExecute(async () =>
            {
                Logger.LogInformation("Deleting many {EntityType} where {Filter}", typeof(T).Name, filter);
                // invalidate all affected caches
                var toDelete = await Repository.GetAllAsync(filter, ct)
                    ?? throw new ResourceNotFoundException(typeof(T).Name, filter.ToString());
                foreach (var e in toDelete)
                    CacheService.Invalidate(CacheService.GetCacheKey(e.Id));
                CacheService.Invalidate(CacheService.GetCollectionCacheKey());
                CacheService.Invalidate(CacheService.GetFilterCacheKey());

                var crudResult = await Repository.DeleteManyAsync(filter, ct);
                if (!crudResult.IsSuccess)
                    throw new DatabaseException(crudResult.ErrorMessage!);

                await EventService?.Publish(new CollectionDeletedEvent<T>(toDelete));
                return crudResult;
            });

        public virtual Task<ResultWrapper<bool>> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
            => SafeExecute(async () =>
            {
                if (CacheService.TryGetValue(CacheService.GetCacheKey(id), out T _))
                    return true;
                return await Repository.CheckExistsAsync(id, cancellationToken);
            });

        public virtual Task<ResultWrapper<PaginatedResult<T>>> GetPaginatedAsync(
            FilterDefinition<T> filter,
            int page = 1,
            int pageSize = 20,
            string sortField = null,
            bool sortAscending = false,
            CancellationToken cancellationToken = default)
            => SafeExecute(async () =>
            {
                page = Math.Max(1, page);
                pageSize = Math.Clamp(pageSize, 1, 100);
                var total = await Repository.CountAsync(filter, cancellationToken);
                SortDefinition<T> sortDef = !string.IsNullOrEmpty(sortField) && _validPropertyNames.Contains(sortField)
                    ? (sortAscending ? Builders<T>.Sort.Ascending(sortField) : Builders<T>.Sort.Descending(sortField))
                    : Builders<T>.Sort.Descending("Id");
                var skip = (page - 1) * pageSize;
                var items = await Repository.GetPaginatedAsync(filter, sortDef, page, pageSize, cancellationToken);
                return items;
            });

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<IClientSessionHandle, Task<TResult>> action,
            CancellationToken cancellationToken = default)
        {
            using var session = await Repository.Client.StartSessionAsync(cancellationToken: cancellationToken);
            session.StartTransaction();
            try
            {
                var result = await action(session);
                await session.CommitTransactionAsync(cancellationToken);
                return result;
            }
            catch
            {
                await session.AbortTransactionAsync(cancellationToken);
                throw;
            }
        }

        protected async Task<ResultWrapper<TResult>> SafeExecute<TResult>(Func<Task<TResult>> work)
        {
            try
            {
                var data = await work();
                return ResultWrapper<TResult>.Success(data);
            }
            catch (MongoWriteException mwx) when (mwx.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                return ResultWrapper<TResult>.FromException(new ConcurrencyException(typeof(T).Name, mwx.Message));
            }
            catch (MongoCommandException mcx)
            {
                return ResultWrapper<TResult>.FromException(new DatabaseException(mcx.Message, mcx));
            }
            catch (TimeoutException tex)
            {
                return ResultWrapper<TResult>.FromException(new DatabaseException(tex.Message, tex));
            }
            catch (Exception ex)
            {
                return ResultWrapper<TResult>.FromException(new DatabaseException(ex.Message, ex));
            }
        }

        protected async Task<ResultWrapper> SafeExecute(Func<Task> work)
        {
            var result = await SafeExecute(async () => { await work(); return true; });
            return result.IsSuccess ? ResultWrapper.Success() : ResultWrapper.Failure(result.Reason, result.ErrorMessage);
        }

        protected async Task<ResultWrapper<TItem>> FetchCached<TItem>(
            string cacheKey,
            Func<Task<TItem?>> factory,
            TimeSpan duration,
            Func<Exception>? notFoundFactory = null)
        {
            try
            {
                var item = await CacheService.GetAnyCachedAsync(cacheKey, factory, duration);
                if (item is null)
                    throw notFoundFactory?.Invoke() ?? new KeyNotFoundException(cacheKey);
                return ResultWrapper<TItem>.Success(item);
            }
            catch (Exception ex)
            {
                return ResultWrapper<TItem>.FromException(ex);
            }
        }
    }
}
