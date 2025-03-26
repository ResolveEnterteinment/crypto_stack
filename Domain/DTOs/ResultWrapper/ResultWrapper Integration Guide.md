# ResultWrapper Integration Guide

This guide explains how to use the enhanced `ResultWrapper<T>` class to improve error handling throughout your application.

## Core Concepts

The refactored `ResultWrapper<T>` provides:

1. **Rich Error Context**: Detailed error information including error codes, categorized failure reasons, and validation errors
2. **Type Safety**: Generic typing for proper data handling
3. **Functional Programming Features**: Methods like `Map`, `MapAsync`, and `Match` for elegant transformations
4. **Consistent Error Handling**: Integration with domain exceptions and HTTP responses
5. **Extension Methods**: Utilities for working with controllers and combining results

## Basic Usage

### Creating Success Results

```csharp
// Success with data
var result = ResultWrapper<int>.Success(42);

// Success with data and message
var result = ResultWrapper<int>.Success(42, "Operation completed successfully");

// For void operations (no data)
var result = ResultWrapper.Success("User updated successfully");

// Implicit conversion from value to success result
ResultWrapper<string> result = "Hello World";
```

### Creating Failure Results

```csharp
// Basic failure with reason
var result = ResultWrapper<int>.Failure(
    FailureReason.ValidationError,
    "Invalid input provided"
);

// Detailed failure with error code and validation errors
var result = ResultWrapper<int>.Failure(
    FailureReason.ValidationError,
    "Username is not available",
    "USERNAME_TAKEN",
    new Dictionary<string, string[]>
    {
        ["username"] = new[] { "This username is already in use" }
    }
);

// Create from exception
try
{
    // ... code that may throw
}
catch (Exception ex)
{
    return ResultWrapper<int>.FromException(ex);
}
```

## Working with Domain Exceptions

The `ResultWrapper` integrates with our domain-specific exceptions:

```csharp
// Converting exceptions to result wrappers
try
{
    // ... business logic
}
catch (ValidationException ex)
{
    // Will include validation errors automatically
    return ResultWrapper<T>.FromException(ex);
}
catch (ResourceNotFoundException ex)
{
    // Will set appropriate error code and failure reason
    return ResultWrapper<T>.FromException(ex);
}
catch (Exception ex)
{
    // Will categorize the exception based on type
    return ResultWrapper<T>.FromException(ex);
}
```

## Controller Integration

Use the extension methods to seamlessly integrate with ASP.NET controllers:

```csharp
[HttpGet("users/{id}")]
public async Task<IActionResult> GetUserById(Guid id)
{
    // Method 1: Using ToActionResult
    var result = await _userService.GetUserByIdAsync(id);
    return result.ToActionResult(this);
    
    // Method 2: Using ProcessResult (more concise)
    return await _userService.GetUserByIdAsync(id).ProcessResult(this);
}
```

## Functional Transformations

Transform successful results without affecting error state:

```csharp
// Transform data with Map
var userResult = await _userService.GetUserByIdAsync(id);
var nameResult = userResult.Map(user => user.FullName);

// Chain multiple operations
var result = await _paymentService.ProcessPaymentAsync(paymentData)
    .MapAsync(async paymentId => await _orderService.CreateOrderAsync(paymentId, items))
    .Map(order => new OrderViewModel(order));
```

## Pattern Matching

Use `Match` for different handling of success and failure:

```csharp
var message = result.Match(
    onSuccess: data => $"Operation completed with result: {data}",
    onFailure: (error, reason) => $"Operation failed: {error}"
);
```

## Callbacks for Side Effects

Use callbacks for logging or other side effects:

```csharp
await _userService.RegisterUserAsync(registerRequest)
    .OnSuccess(userId => _logger.LogInformation("User {UserId} registered successfully", userId))
    .OnFailure((error, reason) => _logger.LogWarning("User registration failed: {Error}", error))
    .ProcessResult(this);
```

## Working with Multiple Results

Combine multiple results:

```csharp
var results = await Task.WhenAll(
    _service1.DoSomethingAsync(),
    _service2.DoSomethingElseAsync(),
    _service3.YetAnotherOperationAsync()
);

var combinedResult = results.Combine();

if (combinedResult.IsSuccess)
{
    // All operations succeeded
    var allData = combinedResult.Data;
}
else
{
    // At least one operation failed
    var errorMessage = combinedResult.ErrorMessage; // Combined error messages
}
```

## Integration with Existing APIs

If you are working with existing APIs that don't use `ResultWrapper`, you can easily convert:

```csharp
// Convert from try-catch pattern
public async Task<ResultWrapper<User>> GetUserByIdAsync(Guid id)
{
    try
    {
        var user = await _existingUserRepository.GetByIdAsync(id);
        if (user == null)
            return ResultWrapper<User>.Failure(FailureReason.NotFound, $"User with ID {id} not found");
            
        return ResultWrapper<User>.Success(user);
    }
    catch (Exception ex)
    {
        return ResultWrapper<User>.FromException(ex);
    }
}

// Convert from tuple return pattern
public async Task<ResultWrapper<Order>> CreateOrderAsync(OrderRequest request)
{
    var (success, order, errorMessage) = await _existingOrderService.TryCreateOrderAsync(request);
    
    if (!success)
        return ResultWrapper<Order>.Failure(FailureReason.ValidationError, errorMessage);
        
    return ResultWrapper<Order>.Success(order);
}
```

## Best Practices

1. **Return Early**: Check errors at the beginning of your methods and return failure results early.

2. **Preserve Context**: When mapping from one result to another, ensure error details are preserved.

3. **Be Specific**: Use the most specific `FailureReason` that applies to the situation.

4. **Include Validation Details**: For validation failures, always include field-specific validation errors.

5. **User-Friendly Messages**: Error messages should be clear and actionable for end users.

6. **Use Domain Exceptions**: Prefer throwing domain-specific exceptions within your domain layer, then convert to ResultWrapper at service boundaries.

7. **Consistent HTTP Status Codes**: Let the ResultWrapper determine the correct HTTP status code based on the failure reason.