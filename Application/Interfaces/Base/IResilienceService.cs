using Domain.DTOs;
using Domain.DTOs.Base;
using Domain.DTOs.Logging;
using Domain.Models;

namespace Application.Interfaces.Base
{
    /// <summary>
    /// Non-generic resilience service interface for operations that don't return data.
    /// </summary>
    public interface IResilienceService
    {
        /// <summary>
        /// Creates a fluent builder for configuring resilience options that returns ResultWrapper (non-generic).
        /// </summary>
        /// <param name="scope">The scope for logging and tracing</param>
        /// <param name="work">The task to execute</param>
        /// <returns>A builder instance for configuring resilience</returns>
        IResilienceBuilder CreateBuilder(Scope scope, Func<Task> work);

        /// <summary>
        /// Executes a task with resilience, handling exceptions and providing options for success and error handling.
        /// </summary>
        /// <param name="scope">The scope for logging and tracing.</param>
        /// <param name="work">The task to execute.</param>
        /// <param name="options">Optional parameters for execution behavior.</param>
        /// <returns>A task that represents the asynchronous operation, containing the result wrapped in ResultWrapper.</returns>
        Task<ResultWrapper> SafeExecute(
            Scope scope,
            Func<Task> work,
            SafeExecuteOptions? options = null);
    }

    /// <summary>
    /// Generic base service interface providing common CRUD and pagination operations.
    /// </summary>
    /// <typeparam name="T">Entity type inheriting from BaseEntity</typeparam>
    public interface IResilienceService<T> : IResilienceService
    {
        /// <summary>
        /// Creates a fluent builder for configuring resilience options with a custom return type.
        /// </summary>
        /// <typeparam name="TResult">The custom return type for the operation</typeparam>
        /// <param name="scope">The scope for logging and tracing</param>
        /// <param name="work">The task to execute</param>
        /// <returns>A builder instance for configuring resilience</returns>
        IResilienceBuilder<TResult> CreateBuilder<TResult>(Scope scope, Func<Task<TResult>> work);

        /// <summary>
        /// Executes a task with resilience, handling exceptions and providing options for success and error handling.
        /// </summary>
        /// <param name="scope">The scope for logging and tracing.</param>
        /// <param name="work">The task to execute.</param>
        /// <param name="options">Optional parameters for execution behavior.</param>
        /// <returns>A task that represents the asynchronous operation, containing the result wrapped in ResultWrapper.</returns>
        Task<ResultWrapper<TResult>> SafeExecute<TResult>(
            Scope scope,
            Func<Task<TResult>> work,
            SafeExecuteOptions<TResult>? options = null);
    }
}