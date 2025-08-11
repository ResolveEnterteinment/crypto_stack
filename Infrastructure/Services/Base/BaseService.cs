using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Base;
using Domain.DTOs.Logging;
using Domain.DTOs.Settings;
using Domain.Events;
using Domain.Events.Entity;
using Domain.Exceptions;
using Domain.Models;
using Microsoft.AspNetCore.JsonPatch.Operations;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using System.Reflection;

namespace Infrastructure.Services.Base
{
    public abstract class BaseService<T> : IBaseService<T> where T : BaseEntity
    {
        protected readonly ICrudRepository<T> _repository;
        protected readonly IResilienceService<T> _resilienceService;
        protected readonly ICacheService<T> _cacheService;
        protected readonly IMongoIndexService<T> _indexService;
        protected readonly ILoggingService _loggingService;
        protected readonly IEventService _eventService;
        protected readonly INotificationService _notificationService;

        private readonly BaseServiceSettings<T> _options;
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
            IServiceProvider serviceProvider,
            BaseServiceSettings<T>? options = null)
        {
            _repository = serviceProvider.GetRequiredService<ICrudRepository<T>>() ?? throw new ArgumentNullException(nameof(ICrudRepository<T>));
            _cacheService = serviceProvider.GetRequiredService<ICacheService<T>>() ?? throw new ArgumentNullException(nameof(ICacheService<T>));
            _indexService = serviceProvider.GetRequiredService<IMongoIndexService<T>>() ?? throw new ArgumentNullException(nameof(IMongoIndexService<T>));
            _loggingService = serviceProvider.GetRequiredService<ILoggingService>() ?? throw new ArgumentNullException(nameof(ILoggingService));
            _eventService = serviceProvider.GetRequiredService<IEventService>() ?? throw new ArgumentNullException(nameof(IEventService));
            _resilienceService = serviceProvider.GetRequiredService<IResilienceService<T>>() ?? throw new ArgumentNullException(nameof(IResilienceService<T>));
            _notificationService = serviceProvider.GetRequiredService<INotificationService>() ?? throw new ArgumentNullException(nameof(INotificationService));

            _options = options ?? new()
            {
                PublishCRUDEvents = true,
            };

            if (options != null && _options.IndexModels != null)
            {
                _indexService.EnsureIndexesAsync(_options.IndexModels);
            }
        }

        // READ OPERATIONS - Use MongoDbReadResilience
        public virtual Task<ResultWrapper<T?>> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            return _resilienceService.CreateBuilder<T?>(
                CreateScope("GetByIdAsync", new { EntityId = id }),
                () => _repository.GetByIdAsync(id, ct)
            )
            .WithMongoDbReadResilience()
            .WithPerformanceMonitoring(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3))
            .ExecuteAsync();
        }

        public virtual Task<ResultWrapper<T?>> GetOneAsync(FilterDefinition<T> filter, CancellationToken ct = default)
        {
            return _resilienceService.CreateBuilder<T?>(
                CreateScope("GetOneAsync", new { Filter = filter?.ToString() }),
                () => _repository.GetOneAsync(filter, ct)
            )
            .WithMongoDbReadResilience()
            .WithPerformanceMonitoring(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4))
            .ExecuteAsync();
        }

        public virtual Task<ResultWrapper<List<T>>> GetManyAsync(FilterDefinition<T> filter, CancellationToken ct = default)
        {
            return _resilienceService.CreateBuilder<List<T>>(
                CreateScope("GetManyAsync", new { Filter = filter?.ToString() }),
                () => _repository.GetAllAsync(filter, ct)
            )
            .WithMongoDbReadResilience()
            .WithPerformanceMonitoring(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5))
            .ExecuteAsync();
        }

        public virtual Task<ResultWrapper<List<T>>> GetAllAsync(CancellationToken ct = default)
        {
            return _resilienceService.CreateBuilder<List<T>>(
                CreateScope("GetAllAsync"),
                () => _repository.GetAllAsync(null, ct)
            )
            .WithMongoDbReadResilience()
            .WithPerformanceMonitoring(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8))
            .ExecuteAsync();
        }

        public virtual Task<ResultWrapper<bool>> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return _resilienceService.CreateBuilder<bool>(
                CreateScope("ExistsAsync", new { EntityId = id }),
                () => _repository.ExistsAsync(id, cancellationToken)
            )
            .WithMongoDbReadResilience()
            .WithPerformanceMonitoring(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2))
            .ExecuteAsync();
        }

        // PAGINATION OPERATIONS - Consistent pattern
        public Task<ResultWrapper<PaginatedResult<T>>> GetPaginatedAsync(FilterDefinition<T> filter, SortDefinition<T> sort, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
        {
            return _resilienceService.CreateBuilder<PaginatedResult<T>>(
                CreateScope("GetPaginatedAsync", new
                {
                    Filter = filter?.ToString(),
                    Sort = sort?.ToString() ?? "Default (Descending by Id)",
                    Page = page,
                    PageSize = pageSize
                }),
                () => _repository.GetPaginatedAsync(filter, sort, page, pageSize, cancellationToken)
            )
            .WithMongoDbReadResilience()
            .WithPerformanceMonitoring(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6))
            .ExecuteAsync();
        }

        public virtual Task<ResultWrapper<PaginatedResult<T>>> GetPaginatedAsync(
            FilterDefinition<T> filter,
            int page = 1,
            int pageSize = 20,
            string? sortField = null,
            bool sortAscending = false,
            CancellationToken cancellationToken = default)
        {
            return _resilienceService.CreateBuilder<PaginatedResult<T>>(
                CreateScope("GetPaginatedAsync", new
                {
                    Filter = filter?.ToString(),
                    Page = page,
                    PageSize = pageSize,
                    SortField = sortField ?? "Id",
                    SortAscending = sortAscending
                }),
                async () =>
                {
                    page = Math.Max(1, page);
                    pageSize = Math.Clamp(pageSize, 1, 100);

                    var total = await _repository.CountAsync(filter, cancellationToken);
                    SortDefinition<T> sortDef = !string.IsNullOrEmpty(sortField) && _validPropertyNames.Contains(sortField)
                        ? (sortAscending ? Builders<T>.Sort.Ascending(sortField) : Builders<T>.Sort.Descending(sortField))
                        : Builders<T>.Sort.Descending("Id");
                    return await _repository.GetPaginatedAsync(filter, sortDef, page, pageSize, cancellationToken);
                }
            )
            .WithMongoDbReadResilience()
            .WithPerformanceMonitoring(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6))
            .ExecuteAsync();
        }

        // WRITE OPERATIONS - Use MongoDbWriteResilience
        public virtual Task<ResultWrapper<CrudResult<T>>> InsertAsync(T entity, CancellationToken ct = default)
        {
            return _resilienceService.CreateBuilder<CrudResult<T>>(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Base",
                    FileName = "BaseService",
                    OperationName = "InsertAsync(T entity, CancellationToken ct = default)",
                    State = new ()
                    {
                        ["EntityId"] = entity.Id,
                    },
                    LogLevel = LogLevel.Error
                },
                async () => {
                    entity.UpdatedAt = DateTime.UtcNow;
                    var crudResult = await _repository.InsertAsync(entity, ct);

                    if (crudResult == null || !crudResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to insert {typeof(T).Name} entity: {entity.Id}: {crudResult?.ErrorMessage}");
                    }

                    return crudResult;
                }
            )
            .WithMongoDbWriteResilience()
            .WithPerformanceMonitoring(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5))
            .OnSuccess(async result =>
            {
                if (_options.PublishCRUDEvents && result.AffectedIds.Any())
                {
                    await _eventService.PublishAsync(new EntityCreatedEvent<T>(result.AffectedIds.First(), entity, _loggingService.Context));
                }
            })
            .OnError(async ex =>
            {
                await _loggingService.LogTraceAsync(
                    $"Failed to insert entity {entity.Id}: {ex.Message}",
                    "InsertAsync",
                    LogLevel.Error,
                    requiresResolution: true);
            })
            .ExecuteAsync();
        }

        public virtual Task<ResultWrapper<CrudResult<T>>> UpdateAsync(Guid id, object fields, CancellationToken ct = default)
        {
            return _resilienceService.CreateBuilder<CrudResult<T>>(
                CreateScope("UpdateAsync", new { EntityId = id, Fields = fields }),
                async () =>
                {
                    // Ensure the entity exists (will throw if not)
                    var toUpdate = await _repository.GetByIdAsync(id, ct)
                        ?? throw new ResourceNotFoundException(typeof(T).Name, id.ToString());

                    var crudResult = await _repository.UpdateAsync(id, fields, ct);
                    if (!crudResult.IsSuccess)
                    {
                        throw new DatabaseException(crudResult.ErrorMessage!);
                    }

                    return crudResult;
                }
            )
            .WithMongoDbWriteResilience()
            .WithPerformanceMonitoring(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5))
            .OnSuccess(async result =>
            {
                if (_options.PublishCRUDEvents && result.Documents.Any())
                {
                    await _eventService.PublishAsync(new EntityUpdatedEvent<T>(id, result.Documents.First(), _loggingService.Context));
                }
            })
            .OnError(async ex =>
            {
                await _loggingService.LogTraceAsync(
                    $"Failed to update entity {id}: {ex.Message}",
                    "UpdateAsync",
                    LogLevel.Error,
                    requiresResolution: true);
            })
            .ExecuteAsync();
        }

        public virtual Task<ResultWrapper<CrudResult<T>>> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            return _resilienceService.CreateBuilder<CrudResult<T>>(
                CreateScope("DeleteAsync", new { EntityId = id }),
                async () =>
                {
                    var crudResult = await _repository.DeleteAsync(id, ct);
                    if (crudResult == null || !crudResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to delete {typeof(T).Name} entity ID {id}: {crudResult?.ErrorMessage ?? "Delete result returned null"}");
                    }

                    return crudResult;
                }
            )
            .WithMongoDbWriteResilience()
            .WithPerformanceMonitoring(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5))
            .OnSuccess(async result =>
            {
                if (_options.PublishCRUDEvents && result.Documents.Any())
                {
                    await _eventService.PublishAsync(new EntityDeletedEvent<T>(id, result.Documents.First(), _loggingService.Context));
                }
            })
            .OnError(async ex =>
            {
                await _loggingService.LogTraceAsync(
                    $"Failed to delete entity {id}: {ex.Message}",
                    "DeleteAsync",
                    LogLevel.Error,
                    requiresResolution: true);
            })
            .ExecuteAsync();
        }

        public virtual Task<ResultWrapper<CrudResult<T>>> DeleteManyAsync(FilterDefinition<T> filter, CancellationToken ct = default)
        {
            return _resilienceService.CreateBuilder<CrudResult<T>>(
                CreateScope("DeleteManyAsync", new { Filter = filter?.ToString() }),
                async () =>
                {
                    var crudResult = await _repository.DeleteManyAsync(filter, ct);
                    if (crudResult == null || !crudResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to delete {typeof(T).Name} filtered collection: {crudResult?.ErrorMessage ?? "Delete result returned null"}");
                    }

                    return crudResult;
                }
            )
            .WithMongoDbWriteResilience()
            .WithPerformanceMonitoring(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10))
            .OnSuccess(async result =>
            {
                if (_options.PublishCRUDEvents && result.Documents.Any())
                {
                    await _eventService.PublishAsync(new CollectionDeletedEvent<T>(result.Documents, _loggingService.Context));
                }
            })
            .OnError(async ex =>
            {
                await _loggingService.LogTraceAsync(
                    $"Failed to delete entities with filter: {ex.Message}",
                    "DeleteManyAsync",
                    LogLevel.Error,
                    requiresResolution: true);
            })
            .ExecuteAsync();
        }

        // TRANSACTION OPERATIONS - Enhanced with resilience
        public async Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<IClientSessionHandle, Task<TResult>> action,
            CancellationToken cancellationToken = default)
        {
            // Fixed: Extract the Data property from the ResultWrapper
            var result = await _resilienceService.CreateBuilder<TResult>(
                CreateScope("ExecuteInTransactionAsync"),
                async () =>
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
            )
            .WithMongoDbWriteResilience()
            .WithPerformanceMonitoring(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15))
            .OnError(async ex =>
            {
                await _loggingService.LogTraceAsync(
                    $"Transaction failed: {ex.Message}",
                    "ExecuteInTransactionAsync",
                    LogLevel.Error,
                    requiresResolution: true);
            })
            .ExecuteAsync();

            // Return the actual data or throw if the operation failed
            if (result.IsSuccess)
            {
                return result.Data;
            }

            // If we get here, the resilience service should have thrown, but just in case
            throw new DatabaseException($"Transaction failed: {result.ErrorMessage}");
        }

        // HELPER METHODS
        private Scope CreateScope(string operationName, object? state = null)
        {
            var scope = new Scope
            {
                NameSpace = "Infrastructure.Services.Base",
                FileName = "BaseService",
                OperationName = operationName,
                State = new Dictionary<string, object>(),
                LogLevel = LogLevel.Error
            };

            if (state != null)
            {
                var properties = state.GetType().GetProperties();
                foreach (var prop in properties)
                {
                    var value = prop.GetValue(state);
                    if (value != null)
                    {
                        scope.State[prop.Name] = value;
                    }
                }
            }

            return scope;
        }

        protected async Task<ResultWrapper<TItem>> FetchCached<TItem>(
            string cacheKey,
            Func<Task<TItem?>> factory,
            TimeSpan duration,
            Func<Exception>? notFoundFactory = null)
        {
            return await _resilienceService.CreateBuilder<TItem>(
                CreateScope("FetchCached", new { CacheKey = cacheKey }),
                async () =>
                {
                    var item = await _cacheService.GetAnyCachedAsync(cacheKey, factory, duration);
                    return item ?? throw (notFoundFactory?.Invoke() ?? new KeyNotFoundException(cacheKey));
                }
            )
            .WithMongoDbReadResilience()
            .WithPerformanceMonitoring(TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2))
            .OnError(async ex =>
            {
                await _loggingService.LogTraceAsync(
                    $"Cache fetch error for key {cacheKey}: {ex.Message}",
                    "FetchCached",
                    LogLevel.Error);
            })
            .ExecuteAsync();
        }
    }
}