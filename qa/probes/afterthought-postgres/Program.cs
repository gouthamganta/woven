using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Endpoints;
using WovenBackend.Services;
using WovenBackend.Services.Moments;
using WovenBackend.Services.Trust;

// Run only against the separately created ephemeral local QA database. No reset,
// migrations, workers, provider calls or private application settings are loaded.
var connection = Environment.GetEnvironmentVariable("WOVEN_QA_ATOMIC_CONNECTION")
    ?? throw new InvalidOperationException("Set WOVEN_QA_ATOMIC_CONNECTION to the isolated local target");
var parsed = new NpgsqlConnectionStringBuilder(connection);
if (parsed.Host != "127.0.0.1" || parsed.Port != 55439 || parsed.Database != "woven_qa_atomic_0f5dc4b" || parsed.Username != "woven_qa")
    throw new InvalidOperationException("Refusing a target outside the explicitly named loopback QA database");
await using (var identity = new NpgsqlConnection(connection))
{
    await identity.OpenAsync();
    await using var command = new NpgsqlCommand("SELECT current_database(), current_user, inet_server_addr()::text, version()", identity);
    await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
    if (reader.GetString(0) != parsed.Database || reader.GetString(1) != parsed.Username)
        throw new InvalidOperationException("Effective database identity does not match the isolated QA target");
    Console.WriteLine(JsonSerializer.Serialize(new { target = "127.0.0.1:55439", database = reader.GetString(0), user = reader.GetString(1), serverAddress = reader.GetString(2), postgres = reader.GetString(3) }));
}
await using (var setup = Db()) await setup.Database.EnsureCreatedAsync();
var results = new List<object>();
await Run("configured_retry_strategy_processes_feedback", async () =>
{
    var row = await Seed(); await using var db = Db(retries: true);
    await Integration(db).ProcessAfterthoughtAsync(row.Id);
    await Verify(row, 1, processed: true);
});
await Run("retry_disabled_control_and_sequential_replay", async () =>
{
    var row = await Seed(); await using (var db = Db()) await Integration(db).ProcessAfterthoughtAsync(row.Id);
    await using (var db = Db()) await Integration(db).ProcessAfterthoughtAsync(row.Id);
    await Verify(row, 1, processed: true);
});
await Run("marker_save_failure_rolls_back_signal_and_retry_recovers", async () =>
{
    var row = await Seed(); var fault = new MarkerFailure { Armed = true };
    await using (var db = Db(interceptor: fault))
    {
        try { await Integration(db).ProcessAfterthoughtAsync(row.Id); throw new Exception("Expected injected marker failure"); }
        catch (InvalidOperationException ex) when (ex.Message == "Injected marker-save failure") { }
    }
    await Verify(row, 0, processed: false);
    await using (var db = Db()) await Integration(db).ProcessAfterthoughtAsync(row.Id);
    await Verify(row, 1, processed: true);
});
await Run("separate_scopes_skip_locked_without_duplicate", async () =>
{
    var row = await Seed(); await using var first = Db(); await using var second = Db();
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var slow = new HeldSignals(new MatchSignalService(first, NullLogger<MatchSignalService>.Instance), entered, release);
    var firstRun = Integration(first, slow).ProcessAfterthoughtAsync(row.Id);
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
    try { await Integration(second).ProcessAfterthoughtAsync(row.Id).WaitAsync(TimeSpan.FromSeconds(5)); }
    finally { release.TrySetResult(); }
    await firstRun; await Verify(row, 1, processed: true);
});
await Run("dedup_distinguishes_complete_afterthought_id_from_prefix", async () =>
{
    var row = await Seed(); await using (var db = Db())
    {
        db.MatchSignalLogs.Add(new MatchSignalLog { ViewerId = row.FromUserId, CandidateId = row.AboutUserId, EventType = MatchSignalEventTypes.ExplicitFeedback, EventValue = .75f,
            MetadataJson = $"{{\"source\":\"afterthought\",\"afterthoughtId\":{row.Id}0,\"closePath\":\"unmatch\"}}" });
        await db.SaveChangesAsync();
    }
    await using (var db = Db()) await Integration(db).ProcessAfterthoughtAsync(row.Id);
    await Verify(row, 2, processed: true);
    await using var verify = Db();
    var signals = await verify.MatchSignalLogs.Where(s => s.ViewerId == row.FromUserId).ToListAsync();
    Check(signals.Any(s => JsonDocument.Parse(s.MetadataJson!).RootElement.GetProperty("afterthoughtId").GetInt32() == row.Id), "Missing signal for the actual feedback ID");
});
await Run("pending_deferred_endpoint_reaches_real_integration", async () =>
{
    var row = await Seed(); await using var db = Db();
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); builder.Logging.ClearProviders();
    foreach (var type in typeof(WovenDbContext).Assembly.GetTypes().Where(t => t.IsInterface && t.Namespace?.StartsWith("WovenBackend.Services") == true))
        builder.Services.AddSingleton(type, _ => DispatchProxy.Create(type, typeof(UnexpectedCallStub)));
    builder.Services.AddSingleton<WovenDbContext>(db);
    builder.Services.AddSingleton<IAfterthoughtService>(new AfterthoughtService(db, Integration(db), NullLogger<AfterthoughtService>.Instance));
    builder.Services.AddAuthorization(); await using var app = builder.Build(); app.MapMatchesEndpoints();
    var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().Single(e => e.RoutePattern.RawText == "/matches/{matchId:guid}/afterthought");
    var context = new DefaultHttpContext { RequestServices = app.Services, User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("uid", row.FromUserId.ToString())], "Synthetic")) };
    context.Request.Method = "POST"; context.Request.RouteValues["matchId"] = row.MatchId.ToString();
    var bytes = JsonSerializer.SerializeToUtf8Bytes(new { rating = 3, reasonTag = "timing", noteText = (string?)null });
    context.Request.Body = new MemoryStream(bytes); context.Request.ContentType = "application/json"; context.Request.ContentLength = bytes.Length;
    context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyAvailable()); context.Response.Body = new MemoryStream();
    await endpoint.RequestDelegate!(context);
    Check(context.Response.StatusCode == 200, "Expected deferred retry200, actual" + context.Response.StatusCode);
    await Verify(row, 1, processed: true);
});
await Run("configured_retries_external_strategy_wrapper_diagnostic", async () =>
{
    var row = await Seed(); await using var db = Db(retries: true);
    // Diagnostic control, not an application fix: current production caller
    // does not have this outer execution-strategy wrapper.
    await db.Database.CreateExecutionStrategy().ExecuteAsync(() => Integration(db).ProcessAfterthoughtAsync(row.Id));
    await Verify(row, 1, processed: true);
});
await Run("existing_exact_signal_repairs_completion_marker", async () =>
{
    var row = await Seed(); await using (var db = Db())
    {
        db.MatchSignalLogs.Add(new MatchSignalLog { ViewerId = row.FromUserId, CandidateId = row.AboutUserId, EventType = MatchSignalEventTypes.ExplicitFeedback, EventValue = .5f,
            MetadataJson = JsonSerializer.Serialize(new { source = "afterthought", afterthoughtId = row.Id, closePath = "unmatch" }) });
        await db.SaveChangesAsync(); await Integration(db).ProcessAfterthoughtAsync(row.Id);
    }
    await Verify(row, 1, processed: true);
});
var report = new { application = "0f5dc4b897e5215b991e9ea928064334e696a5e4", engine = "PostgreSQL16", schema = "EnsureCreated minimal3-table probe; not application migration validation", results };
var output = Environment.GetEnvironmentVariable("WOVEN_QA_ATOMIC_REPORT");
if (output != null) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); }
Environment.ExitCode = results.Any(r => JsonSerializer.Serialize(r).Contains("\"outcome\":\"failed\"")) ? 1 : 0;

AtomicContext Db(bool retries = false, SaveChangesInterceptor? interceptor = null)
{
    var options = new DbContextOptionsBuilder<WovenDbContext>().UseNpgsql(connection, o => { if (retries) o.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null); });
    if (interceptor != null) options.AddInterceptors(interceptor);
    return new(options.Options);
}
AfterthoughtIntegrationService Integration(WovenDbContext db, IMatchSignalService? signals = null)
    => new(db, signals ?? new MatchSignalService(db, NullLogger<MatchSignalService>.Instance), DispatchProxy.Create<ITrustService, UnexpectedCallStub>(), NullLogger<AfterthoughtIntegrationService>.Instance);
async Task<MatchAfterthought> Seed()
{
    await using var db = Db(); var current = 0;
    var match = new Match { UserAId = current, UserBId = current + 1, BalloonState = BalloonState.CLOSED, ClosedReason = ClosedReason.UNMATCH };
    var row = new MatchAfterthought { Match = match, MatchId = match.Id, FromUserId = current, AboutUserId = current + 1, ClosePath = "unmatch", Rating = 3, ReasonTag = "timing" };
    db.MatchAfterthoughts.Add(row); await db.SaveChangesAsync();
    // Unique actors across reruns too; no truncation or database reset needed.
    current = 100 + row.Id * 2;
    match.UserAId = current; match.UserBId = current + 1;
    row.FromUserId = current; row.AboutUserId = current + 1;
    await db.SaveChangesAsync(); return row;
}
async Task Verify(MatchAfterthought row, int expectedSignals, bool processed)
{
    await using var db = Db();
    Check(await db.MatchSignalLogs.CountAsync(s => s.ViewerId == row.FromUserId) == expectedSignals, "Unexpected signal count; expected" + expectedSignals + ", actual" + await db.MatchSignalLogs.CountAsync(s => s.ViewerId == row.FromUserId));
    Check((await db.MatchAfterthoughts.AsNoTracking().SingleAsync(a => a.Id == row.Id)).ProcessedAt.HasValue == processed, "Unexpected completion marker");
}
async Task Run(string name, Func<Task> scenario)
{
    try { await scenario(); var result = new { name, outcome = "passed", error = (string?)null }; results.Add(result); Console.WriteLine(JsonSerializer.Serialize(result)); }
    catch (Exception ex) { var result = new { name, outcome = "failed", error = ex.GetType().Name + ": " + ex.Message }; results.Add(result); Console.WriteLine(JsonSerializer.Serialize(result)); }
}
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

sealed class AtomicContext(DbContextOptions<WovenDbContext> options) : WovenDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        var keep = new[] { typeof(Match), typeof(MatchAfterthought), typeof(MatchSignalLog) };
        foreach (var entity in builder.Model.GetEntityTypes().ToList()) if (!keep.Contains(entity.ClrType)) builder.Ignore(entity.ClrType);
        builder.Entity<Match>().ToTable("matches");
        builder.Entity<MatchSignalLog>().ToTable("MatchSignalLogs");
        builder.Entity<MatchAfterthought>().HasOne(a => a.Match).WithMany().HasForeignKey(a => a.MatchId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<MatchAfterthought>().HasIndex(a => new { a.MatchId, a.FromUserId }).IsUnique();
    }
}
sealed class MarkerFailure : SaveChangesInterceptor
{
    public bool Armed { get; set; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (Armed && data.Context!.ChangeTracker.Entries<MatchAfterthought>().Any(e => e.Entity.ProcessedAt.HasValue))
        { Armed = false; throw new InvalidOperationException("Injected marker-save failure"); }
        return base.SavingChangesAsync(data, result, ct);
    }
}
sealed class HeldSignals(IMatchSignalService inner, TaskCompletionSource entered, TaskCompletionSource release) : IMatchSignalService
{
    public async Task RecordAsync(int viewerId, int candidateId, string eventType, float eventValue, string? metadataJson = null, CancellationToken ct = default)
    { entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10), ct); await inner.RecordAsync(viewerId, candidateId, eventType, eventValue, metadataJson, ct); }
}
public class UnexpectedCallStub : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new NotSupportedException("Unexpected local probe dependency call: " + method!.Name);
}
sealed class BodyAvailable : IHttpRequestBodyDetectionFeature { public bool CanHaveBody => true; }
