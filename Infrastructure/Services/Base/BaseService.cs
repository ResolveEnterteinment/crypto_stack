using Application.Interfaces;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.DTOs;
using Domain.Events;
using Domain.Events.Entity;
using Domain.Exceptions;
using Domain.Models;
using MongoDB.Driver;
using System.Reflection;

namespace Infrastructure.Services.Base
{
    public abstract class BaseService<T> : IBaseService<T> where T : BaseEntity
    {
        public readonly ICrudRepository<T> _repository;
        protected readonly ICacheService<T> CacheService;
        protected readonly IMongoIndexService<T> IndexService;
        protected readonly ILoggingService Logger;
        protected readonly IEventService? EventService;

        private static readonly IReadOnlySet<string> _validPropertyNames;

        ICrudRepository<T> IBaseService<T>.Repository => _repository;

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
            ILoggingService logger,
            IEventService? eventService = null,
            IEnumerable<CreateIndexModel<T>>? indexModels = null)
        {
            _repository = repository;
            CacheService = cacheService;
            IndexService = indexService;
            Logger = logger;
            EventService = eventService;

            if (indexModels is not null)
                _ = IndexService.EnsureIndexesAsync(indexModels);
        }

        public virtual async Task<ResultWrapper<T>> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            using var Scope = Logger.BeginScope("BaseService::GetByIdAsync", new Dictionary<string, object>
            {
                ["EntityType"] = typeof(T).Name,
                ["EntityId"] = id,
            });

            var result = await FetchCached(
                CacheService.GetCacheKey(id),
                () => _repository.GetByIdAsync(id, ct),
                TimeSpan.FromMinutes(5),
                () => new ResourceNotFoundException(typeof(T).Name, id.ToString())
            );

            if (result == null || !result.IsSuccess)
            {
                await Logger.LogTraceAsync($"Entity not found: {result?.ErrorMessage ?? "Fetch result returned null"}");
            }

            return result;
        }

        public virtual async Task<ResultWrapper<T>> GetOneAsync(FilterDefinition<T> filter, CancellationToken ct = default)
        {
            using var Scope = Logger.BeginScope("BaseService::GetOneAsync", new Dictionary<string, object>
            {
                ["EntityType"] = typeof(T).Name,
                ["Filter"] = filter.ToString(),
            });

            var result = await FetchCached(
                CacheService.GetFilterCacheKey() + ":one:" + filter,
                () => _repository.GetOneAsync(filter, ct),
                TimeSpan.FromMinutes(5),
                () => new KeyNotFoundException($"{typeof(T).Name} not found with specified filter.")
            );

            if (result == null || !result.IsSuccess)
            {
                await Logger.LogTraceAsync($"Entity not found: {result?.ErrorMessage ?? "Fetch result returned null"}");
            }

            return result;
        }

        public async virtual Task<ResultWrapper<List<T>>> GetManyAsync(FilterDefinition<T> filter, CancellationToken ct = default)
        {
            using var Scope = Logger.BeginScope("BaseService::GetManyAsync", new Dictionary<string, object>
            {
                ["EntityType"] = typeof(T).Name,
                ["Filter"] = filter.ToString(),
            });

            var result = await FetchCached(
                CacheService.GetFilterCacheKey() + ":many:" + filter,
                () => _repository.GetAllAsync(filter, ct),
                TimeSpan.FromMinutes(5),
                () => new KeyNotFoundException($"No {typeof(T).Name} entities found with specified filter.")
            );

            if (result == null || !result.IsSuccess)
            {
                await Logger.LogTraceAsync($"Entity not found: {result?.ErrorMessage ?? "Fetch result returned null"}");
            }

            return result;
        }

        public virtual async Task<ResultWrapper<List<T>>> GetAllAsync(CancellationToken ct = default)
        {
            using var Scope = Logger.BeginScope("BaseService::GetAllAsync", new Dictionary<string, object>
            {
                ["EntityType"] = typeof(T).Name,
            });

            var result = await FetchCached(
                CacheService.GetCollectionCacheKey(),
                () => _repository.GetAllAsync(null, ct),
                TimeSpan.FromMinutes(5),
                () => new KeyNotFoundException($"{typeof(T).Name} collection is empty.")
            );

            if (result == null || !result.IsSuccess)
            {
                await Logger.LogTraceAsync($"Entity not found: {result?.ErrorMessage ?? "Fetch result returned null"}");
            }

            return result;
        }

        public async Task<ResultWrapper<PaginatedResult<T>>> GetPaginatedAsync(FilterDefinition<T> filter, SortDefinition<T> sort, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
        {
            using var Scope = Logger.BeginScope("BaseService::GetPaginatedAsync", new
            {
                Filter = filter,
                Sort = sort,
                Page = page,
                PageSize = pageSize,
            });

            var result = await FetchCached(
                CacheService.GetFilterCacheKey() + ":filter:" + filter + ":sort:" + sort,
                () => _repository.GetPaginatedAsync(filter, sort, page, pageSize, cancellationToken),
                TimeSpan.FromMinutes(5),
                () => new KeyNotFoundException($"{typeof(T).Name} collection is empty.")
            );

            if (result == null || !result.IsSuccess)
            {
                await Logger.LogTraceAsync($"Failed to fetch paginated records {result?.ErrorMessage ?? "Fetch result returned null"}");
            }

            return result;
        }

        public virtual async Task<ResultWrapper<CrudResult>> InsertAsync(T entity, CancellationToken ct = default)
        {
            using var Scope = Logger.BeginScope("BaseService::InsertAsync", new Dictionary<string, object>
            {
                ["EntityType"] = typeof(T).Name,
                ["Entity"] = entity,
            });

            var result = await SafeExecute(async () =>
            {
                CacheService.Invalidate(CacheService.GetCollectionCacheKey());
                var crudResult = await _repository.InsertAsync(entity, ct);  // returns CrudResult with AffectedIds, etc.
                if (crudResult == null || !crudResult.IsSuccess)
                    throw new DatabaseException(crudResult!.ErrorMessage!);

                var id = crudResult.AffectedIds.First();
                if (EventService is not null)
                    await EventService.PublishAsync(new EntityCreatedEvent<T>(id, entity, Logger.Context));
                return crudResult;
            });

            if (result == null || !result.IsSuccess)
            {
                await Logger.LogTraceAsync($"Failed to insert: {result?.ErrorMessage ?? "Insert result returned null"}");
            }

            return result;
        }


        public virtual async Task<ResultWrapper<CrudResult>> UpdateAsync(Guid id, object fields, CancellationToken ct = default)
        {
            using (Logger.BeginScope("BaseService::UpdateAsync", new Dictionary<string, object>
            {
                ["EntityType"] = typeof(T).Name,
                ["EntityId"] = id,
                ["Fields"] = fields,
            }))
            {
                var result = await SafeExecute(async () =>
                {
                    CacheService.Invalidate(CacheService.GetCacheKey(id));
                    CacheService.Invalidate(CacheService.GetCollectionCacheKey());

                    // ensure the entity exists (will throw if not)
                    var toUpdate = await _repository.GetByIdAsync(id, ct)
                        ?? throw new ResourceNotFoundException(typeof(T).Name, id.ToString());

                    var crudResult = await _repository.UpdateAsync(id, fields, ct);
                    if (!crudResult.IsSuccess)
                        throw new DatabaseException(crudResult.ErrorMessage!);

                    var updated = await _repository.GetByIdAsync(id, ct);
                    await EventService?.PublishAsync(new EntityUpdatedEvent<T>(id, updated!, Logger.Context));
                    return crudResult;
                });

                if (result == null || !result.IsSuccess)
                {
                    await Logger.LogTraceAsync($"Failed to update: {result?.ErrorMessage ?? "Update result returned null"}");
                }

                return result;
            }
        }

        public virtual async Task<ResultWrapper<CrudResult>> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            using var Scope = Logger.BeginScope("BaseService::DeleteAsync", new Dictionary<string, object>
            {
                ["EntityType"] = typeof(T).Name,
                ["EntityId"] = id,
            });

            var result = await SafeExecute(async () =>
            {
                CacheService.Invalidate(CacheService.GetCacheKey(id));
                CacheService.Invalidate(CacheService.GetCollectionCacheKey());

                // fetch so we can emit the deleted entity in the event
                var toDelete = await _repository.GetByIdAsync(id, ct)
                    ?? throw new ResourceNotFoundException(typeof(T).Name, id.ToString());

                var crudResult = await _repository.DeleteAsync(id, ct);
                if (!crudResult.IsSuccess)
                {
                    await Logger.LogTraceAsync($"Failed to delete record: {toDelete.Id}");
                    throw new DatabaseException(crudResult.ErrorMessage!);
                }

                await EventService?.PublishAsync(new EntityDeletedEvent<T>(id, toDelete, Logger.Context));
                return crudResult;
            });

            if (result == null || !result.IsSuccess)
            {
                await Logger.LogTraceAsync($"Failed to delete: {result?.ErrorMessage ?? "Delete result returned null"}");
            }

            return result;
        }

        public virtual async Task<ResultWrapper<CrudResult<T>>> DeleteManyAsync(FilterDefinition<T> filter, CancellationToken ct = default)
        {
            using var Scope = Logger.BeginScope("BaseService::DeleteManyAsync", new Dictionary<string, object>
            {
                ["EntityType"] = typeof(T).Name,
                ["Filter"] = filter,
            });

            var result = await SafeExecute(async () =>
            {
                // invalidate all affected caches
                var toDelete = await _repository.GetAllAsync(filter, ct)
                    ?? throw new ResourceNotFoundException(typeof(T).Name, filter.ToString());
                foreach (var e in toDelete)
                    CacheService.Invalidate(CacheService.GetCacheKey(e.Id));
                CacheService.Invalidate(CacheService.GetCollectionCacheKey());
                CacheService.Invalidate(CacheService.GetFilterCacheKey());

                var crudResult = await _repository.DeleteManyAsync(filter, ct);
                if (!crudResult.IsSuccess)
                    throw new DatabaseException(crudResult.ErrorMessage!);

                await EventService?.PublishAsync(new CollectionDeletedEvent<T>(toDelete, Logger.Context));
                return crudResult;
            });

            if (result == null || !result.IsSuccess)
            {
                await Logger.LogTraceAsync($"Failed to delete: {result?.ErrorMessage ?? "Delete result returned null"}");
            }

            return result;
        }
        public virtual async Task<ResultWrapper<bool>> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            using var Scope = Logger.BeginScope("BaseService::ExistsAsync", new Dictionary<string, object>
            {
                ["EntityType"] = typeof(T).Name,
                ["EntityId"] = id,
            });

            var result = await SafeExecute(async () =>
            {
                if (CacheService.TryGetValue(CacheService.GetCacheKey(id), out T _))
                    return true;
                return await _repository.CheckExistsAsync(id, cancellationToken);
            });

            if (result == null || !result.IsSuccess)
            {
                await Logger.LogTraceAsync($"Failed to check exists: {result?.ErrorMessage ?? "Check exists result returned null"}");
            }

            return result;
        }

        public async virtual Task<ResultWrapper<PaginatedResult<T>>> GetPaginatedAsync(
            FilterDefinition<T> filter,
            int page = 1,
            int pageSize = 20,
            string sortField = null,
            bool sortAscending = false,
            CancellationToken cancellationToken = default)
        {
            using var Scope = Logger.BeginScope("BaseService::GetPaginatedAsync", new Dictionary<string, object>
            {
                ["EntityType"] = typeof(T).Name,
            });

            var result = await SafeExecute(async () =>
            {
                page = Math.Max(1, page);
                pageSize = Math.Clamp(pageSize, 1, 100);
                var total = await _repository.CountAsync(filter, cancellationToken);
                SortDefinition<T> sortDef = !string.IsNullOrEmpty(sortField) && _validPropertyNames.Contains(sortField)
                    ? (sortAscending ? Builders<T>.Sort.Ascending(sortField) : Builders<T>.Sort.Descending(sortField))
                    : Builders<T>.Sort.Descending("Id");
                var skip = (page - 1) * pageSize;
                var items = await _repository.GetPaginatedAsync(filter, sortDef, page, pageSize, cancellationToken);
                return items;
            });

            if (result == null || !result.IsSuccess || result.Data == null)
            {
                await Logger.LogTraceAsync($"Failed to get paginated data: {result?.ErrorMessage ?? "Fetch result returned null"}");
            }

            return result?.Data!;
        }

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<IClientSessionHandle, Task<TResult>> action,
            CancellationToken cancellationToken = default)
        {
            using var session = await _repository.Client.StartSessionAsync(cancellationToken: cancellationToken);
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
                Logger.LogError($"Safe execute error: {mwx.Message}");
                return ResultWrapper<TResult>.FromException(new ConcurrencyException(typeof(T).Name, mwx.Message));
            }
            catch (MongoCommandException mcx)
            {
                Logger.LogError($"Safe execute error: {mcx.Message}");
                return ResultWrapper<TResult>.FromException(new DatabaseException(mcx.Message, mcx));
            }
            catch (TimeoutException tex)
            {
                Logger.LogError($"Safe execute error: {tex.Message}");
                return ResultWrapper<TResult>.FromException(new DatabaseException(tex.Message, tex));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Safe execute error: {ex.Message}");
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
                Logger.LogError($"Fetch cached error: {ex.Message}");
                return ResultWrapper<TItem>.FromException(ex);
            }
        }
    }
}
