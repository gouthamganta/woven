using System.Collections.Concurrent;

namespace WovenBackend.Services;

/// <summary>
/// Circuit breaker service to prevent cascading failures from external dependencies (OpenAI).
/// Implements the Circuit Breaker pattern with Closed, Open, and HalfOpen states.
/// </summary>
public interface ICircuitBreakerService
{
    /// <summary>
    /// Executes an operation through the circuit breaker.
    /// Throws CircuitBreakerOpenException if circuit is open.
    /// </summary>
    Task<T> ExecuteAsync<T>(
        string circuitName,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the current state of a circuit.
    /// </summary>
    CircuitState GetState(string circuitName);

    /// <summary>
    /// Manually resets a circuit to closed state.
    /// </summary>
    void Reset(string circuitName);
}

public class CircuitBreakerService : ICircuitBreakerService
{
    private readonly ILogger<CircuitBreakerService> _logger;
    private readonly ConcurrentDictionary<string, CircuitBreakerState> _circuits = new();

    // Configuration
    private const int FailureThreshold = 3;
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public CircuitBreakerService(ILogger<CircuitBreakerService> logger)
    {
        _logger = logger;
    }

    public async Task<T> ExecuteAsync<T>(
        string circuitName,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
    {
        var state = _circuits.GetOrAdd(circuitName, _ => new CircuitBreakerState());

        // Check circuit state
        lock (state.Lock)
        {
            switch (state.State)
            {
                case CircuitState.Open:
                    // Check if cooldown period has elapsed
                    if (DateTimeOffset.UtcNow - state.OpenedAt >= CooldownPeriod)
                    {
                        _logger.LogInformation("[CircuitBreaker] {CircuitName} transitioning to HalfOpen for trial",
                            circuitName);
                        state.State = CircuitState.HalfOpen;
                    }
                    else
                    {
                        var remainingCooldown = CooldownPeriod - (DateTimeOffset.UtcNow - state.OpenedAt);
                        _logger.LogWarning("[CircuitBreaker] {CircuitName} is OPEN. Rejecting request. Cooldown remaining: {Cooldown}s",
                            circuitName, remainingCooldown.TotalSeconds);
                        throw new CircuitBreakerOpenException(circuitName, remainingCooldown);
                    }
                    break;

                case CircuitState.HalfOpen:
                    _logger.LogInformation("[CircuitBreaker] {CircuitName} in HalfOpen state, allowing trial request",
                        circuitName);
                    break;

                case CircuitState.Closed:
                    // Normal operation
                    break;
            }
        }

        // Execute operation with timeout
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(DefaultTimeout);

            var result = await operation(timeoutCts.Token);

            // Success - reset failure count
            lock (state.Lock)
            {
                if (state.State == CircuitState.HalfOpen)
                {
                    _logger.LogInformation("[CircuitBreaker] {CircuitName} trial succeeded, closing circuit",
                        circuitName);
                    state.State = CircuitState.Closed;
                }

                state.FailureCount = 0;
                state.LastSuccessAt = DateTimeOffset.UtcNow;
            }

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // User-initiated cancellation, don't count as failure
            throw;
        }
        catch (Exception ex)
        {
            // Record failure
            lock (state.Lock)
            {
                state.FailureCount++;
                state.LastFailureAt = DateTimeOffset.UtcNow;
                state.LastException = ex.Message;

                _logger.LogWarning(ex, "[CircuitBreaker] {CircuitName} operation failed. Failure count: {FailureCount}/{Threshold}",
                    circuitName, state.FailureCount, FailureThreshold);

                if (state.State == CircuitState.HalfOpen)
                {
                    // Trial failed, reopen circuit
                    _logger.LogError("[CircuitBreaker] {CircuitName} trial FAILED, reopening circuit",
                        circuitName);
                    state.State = CircuitState.Open;
                    state.OpenedAt = DateTimeOffset.UtcNow;
                }
                else if (state.FailureCount >= FailureThreshold)
                {
                    // Threshold exceeded, open circuit
                    _logger.LogError("[CircuitBreaker] {CircuitName} threshold exceeded, opening circuit for {Cooldown} minutes",
                        circuitName, CooldownPeriod.TotalMinutes);
                    state.State = CircuitState.Open;
                    state.OpenedAt = DateTimeOffset.UtcNow;
                }
            }

            throw;
        }
    }

    public CircuitState GetState(string circuitName)
    {
        if (_circuits.TryGetValue(circuitName, out var state))
        {
            lock (state.Lock)
            {
                return state.State;
            }
        }

        return CircuitState.Closed;
    }

    public void Reset(string circuitName)
    {
        if (_circuits.TryGetValue(circuitName, out var state))
        {
            lock (state.Lock)
            {
                _logger.LogInformation("[CircuitBreaker] Manually resetting {CircuitName} to Closed", circuitName);
                state.State = CircuitState.Closed;
                state.FailureCount = 0;
                state.LastException = null;
            }
        }
    }
}

internal class CircuitBreakerState
{
    public object Lock { get; } = new();
    public CircuitState State { get; set; } = CircuitState.Closed;
    public int FailureCount { get; set; } = 0;
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset LastFailureAt { get; set; }
    public DateTimeOffset LastSuccessAt { get; set; }
    public string? LastException { get; set; }
}

public enum CircuitState
{
    Closed,    // Normal operation
    Open,      // Circuit is open, rejecting requests
    HalfOpen   // Trial mode, testing if service recovered
}

public class CircuitBreakerOpenException : Exception
{
    public string CircuitName { get; }
    public TimeSpan RemainingCooldown { get; }

    public CircuitBreakerOpenException(string circuitName, TimeSpan remainingCooldown)
        : base($"Circuit breaker '{circuitName}' is open. Remaining cooldown: {remainingCooldown.TotalSeconds:F0}s")
    {
        CircuitName = circuitName;
        RemainingCooldown = remainingCooldown;
    }
}
