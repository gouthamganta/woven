using Microsoft.Extensions.Logging.Abstractions;
using WovenBackend.Services;

namespace WovenBackend.Tests;

public class CircuitBreakerServiceTests
{
    private readonly CircuitBreakerService _svc = new(NullLogger<CircuitBreakerService>.Instance);

    [Fact]
    public async Task ExecuteAsync_SuccessfulCall_ReturnsClosed()
    {
        var result = await _svc.ExecuteAsync("test-ok", _ => Task.FromResult("hello"));

        Assert.Equal("hello", result);
        Assert.Equal(CircuitState.Closed, _svc.GetState("test-ok"));
    }

    [Fact]
    public async Task ExecuteAsync_ThreeFailures_OpensCircuit()
    {
        for (int i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _svc.ExecuteAsync<string>("test-fail", _ => throw new InvalidOperationException("boom")));
        }

        Assert.Equal(CircuitState.Open, _svc.GetState("test-fail"));
    }

    [Fact]
    public async Task ExecuteAsync_OpenCircuit_ThrowsCircuitBreakerOpenException()
    {
        // Trip the circuit
        for (int i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _svc.ExecuteAsync<string>("test-open", _ => throw new InvalidOperationException("boom")));
        }

        // Next call should be rejected immediately
        await Assert.ThrowsAsync<CircuitBreakerOpenException>(() =>
            _svc.ExecuteAsync("test-open", _ => Task.FromResult("should not run")));
    }

    [Fact]
    public void Reset_AfterOpen_ClosesCircuit()
    {
        // Manually verify reset works on an unknown circuit (returns Closed by default)
        Assert.Equal(CircuitState.Closed, _svc.GetState("nonexistent"));

        _svc.Reset("nonexistent");
        Assert.Equal(CircuitState.Closed, _svc.GetState("nonexistent"));
    }

    [Fact]
    public void GetState_UnknownCircuit_ReturnsClosed()
    {
        Assert.Equal(CircuitState.Closed, _svc.GetState("never-used-" + Guid.NewGuid()));
    }
}
