using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Endpoints;
using WovenBackend.Services;
using WovenBackend.Services.Analytics;
using WovenBackend.Services.Embeddings;
using WovenBackend.Services.Matchmaking;
using WovenBackend.Services.Moments;

namespace WovenBackend.Tests;

public class MomentsRatingPrivacyTests
{
    [Theory]
    [InlineData("/moments", 0)]
    [InlineData("/moments", 5)]
    [InlineData("/moments/liked-you", 0)]
    [InlineData("/moments/liked-you", 5)]
    public async Task CardJson_DoesNotExposeRatings(string path, int ratingCount)
    {
        var builder = WebApplication.CreateBuilder();
        var dbOptions = new DbContextOptionsBuilder<WovenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        builder.Services.AddScoped<WovenDbContext>(_ => new PrivacyTestDbContext(dbOptions));
        builder.Services.AddScoped<SparkWalletService>();
        builder.Services.AddScoped<InteractionBudgetService>();
        builder.Services.AddScoped<MomentsMatchService>();
        // POST dependencies are registered for route construction, never resolved here.
        builder.Services.AddScoped<IMatchSignalService>(_ => throw new InvalidOperationException());
        builder.Services.AddScoped<IIdempotencyService>(_ => throw new InvalidOperationException());
        builder.Services.AddSingleton<IDailyDeckOrchestrator, SyntheticDeck>();
        builder.Services.AddSingleton<IVisualPreferenceService, SyntheticPhotos>();
        builder.Services.AddSingleton<IAnalyticsService, NoOpAnalytics>();
        await using var app = builder.Build();
        app.MapMomentsEndpoints();

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
        db.Users.AddRange(
            new User { Id = 1, Email = "viewer@example.test", FullName = "Synthetic Viewer" },
            new User { Id = 2, Email = "candidate@example.test", FullName = "Synthetic Candidate" });
        db.MomentResponses.Add(new MomentResponse
        {
            FromUserId = 2,
            ToUserId = 1,
            Choice = MomentChoice.MAGICAL,
            DateUtc = MomentsRules.UtcToday(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        for (var i = 0; i < ratingCount; i++)
            db.UserRatings.Add(new UserRating { RatedUserId = 2, RaterUserId = 10 + i, RatingValue = 100 });
        await db.SaveChangesAsync();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText?.TrimEnd('/') == path);
        var http = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", "1") }, "Synthetic"))
        };
        http.Request.Method = "GET";
        http.Request.Path = path;
        await using var body = new MemoryStream();
        http.Response.Body = body;
        await endpoint.RequestDelegate!(http);

        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body);
        var cards = json.RootElement.GetProperty("cards");
        Assert.Equal(1, cards.GetArrayLength());
        Assert.Equal(2, cards[0].GetProperty("userId").GetInt32());
        Assert.False(cards[0].TryGetProperty("rating", out _));
    }

    private sealed class PrivacyTestDbContext(DbContextOptions<WovenDbContext> options) : WovenDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // PostgreSQL vector storage is unrelated to these response tests and
            // has no mapping in the in-memory provider.
            modelBuilder.Ignore<Pgvector.Vector>();
            foreach (var entity in modelBuilder.Model.GetEntityTypes().ToList())
                foreach (var property in entity.ClrType.GetProperties()
                    .Where(property => property.PropertyType == typeof(Pgvector.Vector)))
                    modelBuilder.Entity(entity.ClrType).Ignore(property.Name);
        }
    }

    private sealed class SyntheticDeck : IDailyDeckOrchestrator
    {
        public Task<DailyDeckResult> GetOrCreateDeckAsync(int userId, DateOnly dateUtc, CancellationToken ct = default) =>
            Task.FromResult(new DailyDeckResult { Items = [new DeckItem { CandidateId = 2, Bucket = "BALANCED" }] });
    }

    private sealed class SyntheticPhotos : IVisualPreferenceService
    {
        public Task UpdateVisualPreferenceAsync(int userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> GetBestPhotoUrlAsync(int viewerUserId, int candidateUserId, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoOpAnalytics : IAnalyticsService
    {
        public Task TrackAsync(int? userId, string? sessionId, string eventType, object? properties = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> GetOrAssignVariantAsync(int userId, string experimentId, CancellationToken ct = default) => Task.FromResult("test");
        public Task TrackAbConversionAsync(int userId, string experimentId, string conversionType, CancellationToken ct = default) => Task.CompletedTask;
    }
}
