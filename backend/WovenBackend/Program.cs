using System.Text;
using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using StackExchange.Redis;
using WovenBackend.Auth;
using WovenBackend.Data;
using WovenBackend.Endpoints;
using WovenBackend.Hubs;
using WovenBackend.Infrastructure;
using WovenBackend.Services;
using WovenBackend.Services.Security;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------
// JSON
// ----------------------------------------------------
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// ----------------------------------------------------
// CORS (configuration-driven, no environment branching)
// ----------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCorsPolicy", policy =>
    {
        // 1. Try binding as string[] (works with JSON arrays + indexed env vars)
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

        // 2. Fallback: read as comma-separated string (works with single env var)
        if (origins == null || origins.Length == 0)
        {
            origins = builder.Configuration["Cors:AllowedOrigins"]?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? Array.Empty<string>();
        }

        // 3. Filter out wildcards — never allow "*" with credentials
        origins = origins.Where(o => o != "*").ToArray();

        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ----------------------------------------------------
// DATABASE
// ----------------------------------------------------
// Phase 1A: UseVector() must be called on NpgsqlDataSourceBuilder (Npgsql 9+ API),
// not on NpgsqlDbContextOptionsBuilder. Build the data source first, then pass it to EF Core.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
var npgsqlDataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
npgsqlDataSourceBuilder.UseVector();
var npgsqlDataSource = npgsqlDataSourceBuilder.Build();

// IEncryptionService (singleton) is registered above, so the DI container will
// automatically use WovenDbContext's two-parameter constructor to inject it.
builder.Services.AddDbContext<WovenDbContext>(options =>
    options.UseNpgsql(
        npgsqlDataSource,
        npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorCodesToAdd: null);
            npgsqlOptions.CommandTimeout(30);
            npgsqlOptions.UseVector();
        }));

// ----------------------------------------------------
// SWAGGER
// ----------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Woven API",
        Version = "v1",
        Description = "API for Woven matchmaking MVP"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste JWT token only (no Bearer prefix)"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ----------------------------------------------------
// AUTH
// ----------------------------------------------------
builder.Services.Configure<GoogleAuthOptions>(
    builder.Configuration.GetSection("GoogleAuth"));

builder.Services.AddScoped<IGoogleTokenVerifier, GoogleTokenVerifier>();
builder.Services.AddSingleton<JwtTokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var key = builder.Configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Jwt:Key missing");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(key)
            ),

            ClockSkew = TimeSpan.FromMinutes(
                builder.Configuration.GetValue<int>("Jwt:ClockSkewMinutes", 1)
            )
        };

        // Phase 1C: WebSocket protocol cannot send headers, so SignalR passes the JWT as
        // ?access_token=... in the query string. Read it here and set context.Token.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireClaim("role", "admin"));
});

// ============================================
// BACKGROUND WORKER SCHEDULE (all times UTC)
// ============================================
// 01:00       — SeasonTransitionWorker (nightly)
// 02:00       — TrustBatchWorker (nightly)
// 02:15       — AnalyticsRetentionWorker (1st of month only)
// 02:30       — EmbeddingBatchWorker (nightly)
// 03:00       — CfBatchWorker (nightly)
// 03:30       — GhostDetectionWorker (nightly pass)
// 04:00       — WeightLearningBatchWorker (weekly Sun)
// 04:30       — InsightBatchWorker (nightly)
// 05:00       — SecurityAuditCleanupWorker (weekly Sun)
// 06:00       — WeeklyDigestWorker (weekly Sun)
// 08:00       — FeedbackTriggerWorker (daily)
// Every 1min  — BalloonExpiryWorker (continuous)
// Every 6h    — GhostDetectionWorker (silent threads)
// ============================================

// ----------------------------------------------------
// PHASE 3E: ENCRYPTION + SECURITY AUDIT
// ----------------------------------------------------
// Singleton: constructed once; master key loaded from config at startup.
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddSingleton<ISecurityAuditService, SecurityAuditService>();
builder.Services.AddHostedService<SecurityAuditCleanupWorker>();

// KeyRotationWorker registered as singleton so AdminSecurityEndpoints can resolve it directly.
builder.Services.AddSingleton<KeyRotationWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<KeyRotationWorker>());

// OutboundPiiHandler: transient DelegatingHandler for "external-api" named client.
builder.Services.AddTransient<OutboundPiiHandler>();

// ----------------------------------------------------
// HTTP CLIENT (REQUIRED)
// ----------------------------------------------------
// NOTE: You already register a default HttpClient below,
// so you do NOT need multiple AddHttpClient() calls.
builder.Services.AddHttpClient();

// Named client for external API calls — all traffic passes through OutboundPiiHandler.
builder.Services.AddHttpClient("external-api")
    .AddHttpMessageHandler<OutboundPiiHandler>();

// ----------------------------------------------------
// HTTP CLIENT + OPENAI REWRITE SERVICE
// ----------------------------------------------------
builder.Services.AddHttpClient<OpenAiRewriteService>();

// ----------------------------------------------------
// AI PROFILE SERVICE
// ----------------------------------------------------
builder.Services.AddScoped<IAiProfileService, AiProfileService>();

// ----------------------------------------------------
// FOUNDATIONAL CYCLE SERVICE
// ----------------------------------------------------
builder.Services.AddScoped<FoundationalCycleService>();

builder.Services.AddScoped<WovenBackend.Services.Moments.InteractionBudgetService>();
builder.Services.AddScoped<WovenBackend.Services.Moments.MomentsMatchService>();
builder.Services.AddHostedService<WovenBackend.Services.Moments.BalloonExpiryWorker>();

builder.Services.AddHttpClient<OpenAiDynamicIntakeRewriteService>();
builder.Services.AddScoped<OpenAiDynamicIntakeRewriteService>();
builder.Services.AddScoped<DynamicIntakeCycleService>();

// ----------------------------------------------------
// MATCHMAKING ENGINE SERVICES
// ----------------------------------------------------

// HttpClient for MatchExplanationService
builder.Services.AddHttpClient<WovenBackend.Services.Matchmaking.MatchExplanationService>();

// Tagging service (uses existing OpenAI HttpClient pattern)
builder.Services.AddHttpClient<WovenBackend.Services.Matchmaking.IOpenAiTaggingService,
    WovenBackend.Services.Matchmaking.OpenAiTaggingService>();

// Core matchmaking services (scoped = one instance per request)
builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IUserVectorBuilder,
    WovenBackend.Services.Matchmaking.UserVectorBuilder>();

builder.Services.AddScoped<WovenBackend.Services.Matchmaking.ICandidatePoolService,
    WovenBackend.Services.Matchmaking.CandidatePoolService>();

builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IMatchScoringService,
    WovenBackend.Services.Matchmaking.MatchScoringService>();

builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IDeliveryBoostService,
    WovenBackend.Services.Matchmaking.DeliveryBoostService>();

builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IDeckSelectionService,
    WovenBackend.Services.Matchmaking.DeckSelectionService>();

builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IMatchExplanationService,
    WovenBackend.Services.Matchmaking.MatchExplanationService>();

// Phase 1A: pgvector cosine similarity queries
builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IVectorSearchService,
    WovenBackend.Services.Matchmaking.VectorSearchService>();

// ----------------------------------------------------
// PHASE 1C: SIGNALR + PUSH NOTIFICATIONS
// ----------------------------------------------------
// Redis backplane allows pushes to work across all Container Apps replicas.
// AbortOnConnectFail=false: app starts even if Redis is briefly unavailable.
builder.Services.AddSignalR()
    .AddStackExchangeRedis(
        builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379",
        opts => { opts.Configuration.AbortOnConnectFail = false; });

builder.Services.AddSingleton<INotificationService, NotificationService>();

// ----------------------------------------------------
// PHASE 1B: REDIS CACHE
// ----------------------------------------------------
// Singleton IConnectionMultiplexer — one TCP connection shared across the process.
// AbortOnConnectFail=false: app starts even if Redis is temporarily unavailable;
// the multiplexer reconnects in the background. CacheService wraps all ops in
// try/catch, so a Redis outage is never user-visible.
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var redisConnStr = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
    var cfg = ConfigurationOptions.Parse(redisConnStr);
    cfg.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(cfg);
});
builder.Services.AddSingleton<ICacheService, CacheService>();

// ----------------------------------------------------
// PHASE 1D: AZURE BLOB STORAGE (MEDIA)
// ----------------------------------------------------
// BlobServiceClient is thread-safe — singleton is correct.
builder.Services.AddSingleton<BlobServiceClient>(sp =>
    new BlobServiceClient(builder.Configuration["Azure:Storage:ConnectionString"]
        ?? throw new InvalidOperationException("Azure:Storage:ConnectionString is required")));
builder.Services.AddScoped<IMediaService, MediaService>();
builder.Services.AddHostedService<WovenBackend.Services.Media.MediaLifecycleWorker>();

builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IDailyDeckOrchestrator,
    WovenBackend.Services.Matchmaking.DailyDeckOrchestrator>();

// ----------------------------------------------------
// PHASE 2A: TILES
// ----------------------------------------------------
// TileEmbeddingService is a singleton so it can be safely called from fire-and-forget
// Task.Run after the originating request scope ends (uses IServiceScopeFactory internally).
builder.Services.AddSingleton<WovenBackend.Services.Tiles.TileEmbeddingService>();
builder.Services.AddScoped<WovenBackend.Services.Tiles.ITileService,
    WovenBackend.Services.Tiles.TileService>();
builder.Services.AddHostedService<WovenBackend.Services.Tiles.TileExpiryWorker>();

builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IMatchOutcomeService,
    WovenBackend.Services.Matchmaking.MatchOutcomeService>();

// ----------------------------------------------------
// PHASE 2B: MODERATION + TRUST
// ----------------------------------------------------
builder.Services.AddHttpClient<WovenBackend.Services.Moderation.ModerationService>();
builder.Services.AddScoped<WovenBackend.Services.Moderation.IModerationService,
    WovenBackend.Services.Moderation.ModerationService>();
builder.Services.AddHostedService<WovenBackend.Services.Moderation.ModerationWorker>();

builder.Services.AddScoped<WovenBackend.Services.Trust.ITrustService,
    WovenBackend.Services.Trust.TrustService>();
builder.Services.AddHostedService<WovenBackend.Services.Trust.TrustBatchWorker>();

// ----------------------------------------------------
// PHASE 2C: COMMONS FEED
// ----------------------------------------------------
builder.Services.AddScoped<WovenBackend.Services.Commons.ICommonsFeedService,
    WovenBackend.Services.Commons.CommonsFeedService>();

// ----------------------------------------------------
// PHASE 3A: ORBIT + FRIEND BRIDGE
// ----------------------------------------------------
builder.Services.AddScoped<WovenBackend.Services.Orbit.IOrbitService,
    WovenBackend.Services.Orbit.OrbitService>();
builder.Services.AddScoped<WovenBackend.Services.Orbit.IFriendBridgeService,
    WovenBackend.Services.Orbit.FriendBridgeService>();

// ----------------------------------------------------
// PHASE 3B: SEASONS
// ----------------------------------------------------
builder.Services.AddScoped<WovenBackend.Services.Seasons.ISeasonService,
    WovenBackend.Services.Seasons.SeasonService>();
builder.Services.AddHostedService<WovenBackend.Services.Seasons.SeasonTransitionWorker>();

// ----------------------------------------------------
// PHASE 3C: COLLABORATIVE FILTERING
// ----------------------------------------------------
builder.Services.AddScoped<WovenBackend.Services.Recommendations.ICollaborativeFilteringService,
    WovenBackend.Services.Recommendations.CollaborativeFilteringService>();
builder.Services.AddHostedService<WovenBackend.Services.Recommendations.CfBatchWorker>();

// ----------------------------------------------------
// PHASE 3D: ENHANCED EMBEDDINGS + WEIGHT LEARNING
// ----------------------------------------------------
builder.Services.AddHttpClient<WovenBackend.Services.Embeddings.IPhotoEmbeddingService,
    WovenBackend.Services.Embeddings.PhotoEmbeddingService>();
builder.Services.AddHttpClient<WovenBackend.Services.Embeddings.IVoiceEmbeddingService,
    WovenBackend.Services.Embeddings.VoiceEmbeddingService>();
builder.Services.AddScoped<WovenBackend.Services.Embeddings.IStyleEmbeddingService,
    WovenBackend.Services.Embeddings.StyleEmbeddingService>();
builder.Services.AddScoped<WovenBackend.Services.Embeddings.IHumorEmbeddingService,
    WovenBackend.Services.Embeddings.HumorEmbeddingService>();
builder.Services.AddScoped<WovenBackend.Services.Embeddings.ILifestyleEmbeddingService,
    WovenBackend.Services.Embeddings.LifestyleEmbeddingService>();
builder.Services.AddScoped<WovenBackend.Services.Embeddings.IEmotionalRhythmService,
    WovenBackend.Services.Embeddings.EmotionalRhythmService>();
builder.Services.AddScoped<WovenBackend.Services.Embeddings.IAttachmentProxyService,
    WovenBackend.Services.Embeddings.AttachmentProxyService>();
builder.Services.AddScoped<WovenBackend.Services.Embeddings.IVisualPreferenceService,
    WovenBackend.Services.Embeddings.VisualPreferenceService>();
builder.Services.AddHostedService<WovenBackend.Services.Embeddings.EmbeddingBatchWorker>();

builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IWeightLearningService,
    WovenBackend.Services.Matchmaking.WeightLearningService>();
builder.Services.AddHostedService<WovenBackend.Services.Matchmaking.WeightLearningBatchWorker>();

// ----------------------------------------------------
// OPENAI RESILIENCE SERVICES (circuit breaker, cost tracking)
// ----------------------------------------------------
builder.Services.AddSingleton<ICircuitBreakerService, CircuitBreakerService>();
builder.Services.AddSingleton<IOpenAiCostTracker, OpenAiCostTracker>();
builder.Services.AddHttpClient<IOpenAiResilientClient, OpenAiResilientClient>();

// ----------------------------------------------------
// GAME SERVICES (add after matchmaking services)
// ----------------------------------------------------

// Game agents (use IOpenAiResilientClient, not HttpClient)
builder.Services.AddScoped<WovenBackend.Services.Games.KnowMeAgent>();
builder.Services.AddScoped<WovenBackend.Services.Games.RedGreenFlagAgent>();

// Core game services
builder.Services.AddScoped<WovenBackend.Services.Games.IGameService,
    WovenBackend.Services.Games.GameService>();

builder.Services.AddScoped<WovenBackend.Services.Games.IGameAgentFactory,
    WovenBackend.Services.Games.GameAgentFactory>();

builder.Services.AddScoped<WovenBackend.Services.Games.IGameOutcomeService,
    WovenBackend.Services.Games.GameOutcomeService>();

// Add more agents as you build them:
// builder.Services.AddHttpClient<WovenBackend.Services.Games.Top10Agent>();
// builder.Services.AddHttpClient<WovenBackend.Services.Games.RapidFireAgent>();

// ----------------------------------------------------
// PHASE 5C: ANALYTICS ENGINE
// ----------------------------------------------------
builder.Services.AddSingleton<WovenBackend.Services.Analytics.IAnalyticsService,
    WovenBackend.Services.Analytics.AnalyticsService>();
// Anonymizes user_id_hash + session_id for events older than 12 months on the 1st of each month at 2am UTC.
builder.Services.AddHostedService<WovenBackend.Services.Analytics.AnalyticsRetentionWorker>();
builder.Services.AddScoped<WovenBackend.Services.Verification.IVerificationService,
    WovenBackend.Services.Verification.VerificationService>();

// ----------------------------------------------------
// PHASE 4E: CATFISH DETECTION
// ----------------------------------------------------
builder.Services.AddScoped<WovenBackend.Services.Trust.ICatfishDetectionService,
    WovenBackend.Services.Trust.CatfishDetectionService>();
builder.Services.AddScoped<WovenBackend.Services.Feedback.FeedbackInsightService>();

// PHASE 4D: PRE-DATE BRIDGE
// ----------------------------------------------------
builder.Services.AddScoped<WovenBackend.Services.Venues.IVenueService,
    WovenBackend.Services.Venues.VenueService>();
builder.Services.AddScoped<WovenBackend.Services.Feedback.IDateFeedbackService,
    WovenBackend.Services.Feedback.DateFeedbackService>();
builder.Services.AddHostedService<WovenBackend.Services.Feedback.FeedbackTriggerWorker>();

// PHASE 4C: INSIGHTS + OPINIONS
// ----------------------------------------------------
builder.Services.AddScoped<WovenBackend.Services.Insights.IInsightService,
    WovenBackend.Services.Insights.InsightService>();
builder.Services.AddHostedService<WovenBackend.Services.Insights.WeeklyDigestWorker>();
builder.Services.AddHostedService<WovenBackend.Services.Insights.InsightBatchWorker>();

// ----------------------------------------------------
// PHASE 4B: CONVERSATION NUDGES
// ----------------------------------------------------
builder.Services.AddScoped<WovenBackend.Services.Nudges.INudgeService,
    WovenBackend.Services.Nudges.NudgeService>();

// ----------------------------------------------------
// PHASE 4A: ANTI-GHOSTING
// ----------------------------------------------------
builder.Services.AddScoped<WovenBackend.Services.AntiGhosting.IGhostDetectionService,
    WovenBackend.Services.AntiGhosting.GhostDetectionService>();
builder.Services.AddHostedService<WovenBackend.Services.AntiGhosting.GhostDetectionWorker>();

// ----------------------------------------------------
// BUILD APP
// ----------------------------------------------------
var app = builder.Build();

// ----------------------------------------------------
// STARTUP LOGGING
// ----------------------------------------------------
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
logger.LogInformation("Environment: {Env}", app.Environment.EnvironmentName);
logger.LogInformation("ASPNETCORE_URLS: {Urls}", Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "(not set)");
logger.LogInformation("DB connection configured: {HasDb}", !string.IsNullOrEmpty(builder.Configuration.GetConnectionString("DefaultConnection")));

// ----------------------------------------------------
// AUTO-MIGRATE DATABASE
// ----------------------------------------------------
// Applies any pending EF Core migrations on startup.
// Safe: uses __EFMigrationsHistory table to skip already-applied migrations.
// Required for Container Apps where the DB is on a private VNet
// and cannot be reached from local dev machines.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
    try
    {
        logger.LogInformation("Applying pending database migrations...");
        db.Database.Migrate();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to apply database migrations. The app will start but some features may not work.");
    }
}

// ----------------------------------------------------
// MIDDLEWARE
// ----------------------------------------------------

// Azure Container Apps terminates TLS at the ingress and forwards HTTP.
// ForwardedHeaders ensures the app sees the original scheme/IP from X-Forwarded-* headers.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseCors("DefaultCorsPolicy");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
    app.UseHttpsRedirection();
}
else
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred" });
        });
    });
    // HTTPS redirect NOT used in production — Container Apps handles TLS termination at ingress
}

app.UseAuthentication();
app.UseAuthorization();

// Phase 4A/4C/5C: LastActiveAt — fire-and-forget DB write + re-engagement insight on 5-day absence + AppOpened analytics
app.Use(async (context, next) =>
{
    await next(context);

    if (context.User.Identity?.IsAuthenticated == true)
    {
        var uidClaim = context.User.FindFirst("uid")?.Value
                    ?? context.User.FindFirst("sub")?.Value;
        if (int.TryParse(uidClaim, out var userId))
        {
            var scopeFactory = context.RequestServices.GetRequiredService<IServiceScopeFactory>();
            var analytics = context.RequestServices.GetRequiredService<WovenBackend.Services.Analytics.IAnalyticsService>();
            var cache = context.RequestServices.GetRequiredService<ICacheService>();

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

                    var prev = await db.Users.AsNoTracking()
                        .Where(u => u.Id == userId)
                        .Select(u => u.LastActiveAt)
                        .FirstOrDefaultAsync();

                    await db.Users
                        .Where(u => u.Id == userId)
                        .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastActiveAt, DateTimeOffset.UtcNow));

                    // Track AppOpened only when a new analytics session starts (TTL 2h)
                    var sessionKey = $"analytics:session:{userId}";
                    var existingSession = await cache.GetAsync<string>(sessionKey, CancellationToken.None);
                    if (existingSession == null)
                    {
                        var daysSinceLastOpen = prev != null
                            ? (int)Math.Max(0, (DateTimeOffset.UtcNow - prev.Value).TotalDays)
                            : -1;
                        _ = analytics.TrackAsync(userId, null, WovenBackend.Services.Analytics.AnalyticsEvents.AppOpened,
                            new { daysSinceLastOpen });
                    }

                    // Re-engagement insight if absent 5+ days
                    if (prev != null && prev < DateTimeOffset.UtcNow.AddDays(-5))
                    {
                        var insights = scope.ServiceProvider
                            .GetRequiredService<WovenBackend.Services.Insights.IInsightService>();
                        await insights.DeliverInsightAtMomentAsync(userId, "reengagement");
                    }
                }
                catch { /* non-critical */ }
            });
        }
    }
});

// Phase 1C: SignalR hub — must come after UseAuthentication/UseAuthorization
// so the [Authorize] attribute on WovenHub is enforced.
app.MapHub<WovenHub>("/hubs/woven");

// ----------------------------------------------------
// HEALTH ENDPOINTS
// ----------------------------------------------------
// /health/live  — Liveness: "is the process alive?" No external deps. Must always return 200.
//                 Used by Azure Container Apps liveness_probe. If this fails, the container is killed.
// /health/ready — Readiness: "can I serve traffic?" Checks DB connectivity.
//                 Used by Azure Container Apps readiness_probe. If this fails, traffic is routed away.
// /health       — Lightweight check for backwards compatibility and general monitoring.

app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));

app.MapGet("/health/ready", async (WovenDbContext db) =>
{
    try
    {
        await db.Database.CanConnectAsync();
        return Results.Ok(new { status = "ready", database = "connected" });
    }
    catch (Exception ex)
    {
        return Results.Json(
            new { status = "not_ready", database = "unavailable", error = ex.Message },
            statusCode: 503
        );
    }
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// ----------------------------------------------------
// ENDPOINTS
// ----------------------------------------------------
app.MapAuthEndpoints();
app.MapOnboardingEndpoints();
app.MapMomentsEndpoints();
app.MapChatEndpoints();
app.MapGameEndpoints();
app.MapMatchesEndpoints();
app.MapDynamicIntakeEndpoints();
app.MapMediaEndpoints();
app.MapTileEndpoints();
app.MapAdminEndpoints();
app.MapAdminSecurityEndpoints();
app.MapUserDataEndpoints();
app.MapCommonsEndpoints();
app.MapOrbitEndpoints();
app.MapSeasonEndpoints();
app.MapMeEndpoints();
app.MapFeedbackEndpoints();
app.MapVerificationEndpoints();
app.MapAdminAnalyticsEndpoints();
app.MapLegalEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapDevAuthEndpoints();
    app.MapDevMatchmakingSmokeEndpoints();
    app.MapDevSeedEndpoints();
}

// ----------------------------------------------------
// DEBUG ENDPOINTS (Development Only)
// ----------------------------------------------------
if (app.Environment.IsDevelopment())
{
    var debugGroup = app.MapGroup("/debug").RequireAuthorization();

    // GET /debug/me/ai-profile - Returns AiProfile for current user
    debugGroup.MapGet("/me/ai-profile", async (
        HttpContext ctx,
        IAiProfileService aiProfile,
        CancellationToken ct) =>
    {
        var userId = int.Parse(ctx.User.FindFirst("sub")?.Value ?? "0");
        if (userId == 0) return Results.Unauthorized();

        var profile = await aiProfile.GetProfileAsync(userId, ct);
        if (profile == null) return Results.NotFound(new { error = "Profile not found" });

        return Results.Ok(profile);
    });

    // GET /debug/me/vector - Returns raw UserVector data
    debugGroup.MapGet("/me/vector", async (
        HttpContext ctx,
        WovenDbContext db,
        CancellationToken ct) =>
    {
        var userId = int.Parse(ctx.User.FindFirst("sub")?.Value ?? "0");
        if (userId == 0) return Results.Unauthorized();

        var vector = await db.UserVectors
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (vector == null) return Results.NotFound(new { error = "Vector not found" });

        return Results.Ok(new
        {
            vector.UserId,
            vector.Version,
            pillarScores = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(vector.PillarScoresJson),
            vectorData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(vector.VectorJson),
            vector.CreatedAt
        });
    });

    // GET /debug/match/{candidateId}/pair-context - Returns PairContext
    debugGroup.MapGet("/match/{candidateId}/pair-context", async (
        int candidateId,
        HttpContext ctx,
        IAiProfileService aiProfile,
        CancellationToken ct) =>
    {
        var userId = int.Parse(ctx.User.FindFirst("sub")?.Value ?? "0");
        if (userId == 0) return Results.Unauthorized();

        var pairContext = await aiProfile.GetPairContextAsync(userId, candidateId, ct);
        if (pairContext == null) return Results.NotFound(new { error = "Could not compute pair context" });

        return Results.Ok(new
        {
            userProfile = new { pairContext.UserProfile.UserId, pairContext.UserProfile.TopPillars, pairContext.UserProfile.ConversationTone },
            candidateProfile = new { pairContext.CandidateProfile.UserId, pairContext.CandidateProfile.TopPillars, pairContext.CandidateProfile.ConversationTone },
            pairContext.SharedTags,
            pairContext.AlignedPillars,
            pairContext.SharedHobbies,
            pairContext.ToneAlignment,
            pairContext.IntentAlignment,
            intentAlignmentDescription = pairContext.GetIntentAlignmentDescription()
        });
    });

    // GET /debug/match/{candidateId}/explanation - Shows match explanation with context
    debugGroup.MapGet("/match/{candidateId}/explanation", async (
        int candidateId,
        HttpContext ctx,
        WovenDbContext db,
        IAiProfileService aiProfile,
        CancellationToken ct) =>
    {
        var userId = int.Parse(ctx.User.FindFirst("sub")?.Value ?? "0");
        if (userId == 0) return Results.Unauthorized();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var explanation = await db.MatchExplanations
            .Where(e => e.UserId == userId && e.CandidateId == candidateId && e.DateUtc == today)
            .FirstOrDefaultAsync(ct);

        var pairContext = await aiProfile.GetPairContextAsync(userId, candidateId, ct);

        return Results.Ok(new
        {
            explanation = explanation != null ? new
            {
                explanation.Headline,
                bullets = System.Text.Json.JsonSerializer.Deserialize<List<string>>(explanation.BulletsJson),
                explanation.DateIdea,
                explanation.Tone
            } : null,
            pairContext = pairContext != null ? new
            {
                pairContext.SharedTags,
                pairContext.AlignedPillars,
                pairContext.SharedHobbies,
                pairContext.ToneAlignment,
                pairContext.IntentAlignment
            } : null
        });
    });

    // POST /debug/test/foundational-rewrite - Tests question personalization
    debugGroup.MapPost("/test/foundational-rewrite", async (
        HttpContext ctx,
        OpenAiRewriteService rewriteService,
        IAiProfileService aiProfileService,
        CancellationToken ct) =>
    {
        var userId = int.Parse(ctx.User.FindFirst("sub")?.Value ?? "0");
        if (userId == 0) return Results.Unauthorized();

        var profile = await aiProfileService.GetProfileAsync(userId, ct);
        var baseQuestions = FoundationalQuestionBank.GetBaseFiveForVersion(1);

        var rewritten = await rewriteService.RewriteAsync(
            baseQuestions,
            new OpenAiRewriteService.RewriteUserContext(
                profile?.FirstName,
                profile?.Gender,
                profile?.Intent,
                userId
            ),
            "warm, human, dating app",
            ct
        );

        return Results.Ok(new
        {
            userContext = new
            {
                profile?.FirstName,
                profile?.Age,
                profile?.Gender,
                profile?.Intent,
                topTraits = profile?.GetTopTraitsFormatted(),
                keyTags = profile?.GetKeyTagsFormatted(),
                hobbies = profile?.GetHobbiesFormatted(),
                vibe = profile?.ConversationTone
            },
            baseQuestions = baseQuestions.Select(q => new { q.Id, q.Text }),
            rewrittenQuestions = rewritten.Select(q => new { q.Id, q.Text }),
            wasRewritten = !baseQuestions.SequenceEqual(rewritten)
        });
    });

    // POST /debug/test/dynamic-rewrite - Tests intake personalization
    debugGroup.MapPost("/test/dynamic-rewrite", async (
        HttpContext ctx,
        OpenAiDynamicIntakeRewriteService rewriteService,
        IAiProfileService aiProfileService,
        CancellationToken ct) =>
    {
        var userId = int.Parse(ctx.User.FindFirst("sub")?.Value ?? "0");
        if (userId == 0) return Results.Unauthorized();

        var profile = await aiProfileService.GetProfileAsync(userId, ct);
        var baseQuestions = DynamicQuestionBank.GetBaseThree();

        var rewritten = await rewriteService.RewriteAsync(
            baseQuestions,
            new OpenAiDynamicIntakeRewriteService.RewriteContext(
                profile?.FirstName,
                profile?.Gender,
                profile?.Intent,
                userId
            ),
            "minimalist, calm, playful",
            ct
        );

        return Results.Ok(new
        {
            userContext = new
            {
                profile?.FirstName,
                profile?.Age,
                topTraits = profile?.GetTopTraitsFormatted(),
                vibe = profile?.ConversationTone
            },
            baseQuestions = baseQuestions.Select(q => new { q.Id, q.Text }),
            rewrittenQuestions = rewritten.Select(q => new { q.Id, q.Text }),
            wasRewritten = !AreQuestionsIdentical(baseQuestions, rewritten)
        });

        static bool AreQuestionsIdentical(DynamicBankQuestion[] a, DynamicBankQuestion[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].Text != b[i].Text) return false;
                for (int j = 0; j < a[i].Options.Length; j++)
                {
                    if (a[i].Options[j].Label != b[i].Options[j].Label) return false;
                }
            }
            return true;
        }
    });

    // GET /debug/me/game-analytics - Returns game performance stats
    debugGroup.MapGet("/me/game-analytics", async (
        HttpContext ctx,
        WovenBackend.Services.Games.IGameOutcomeService outcomeService,
        CancellationToken ct) =>
    {
        var userId = int.Parse(ctx.User.FindFirst("sub")?.Value ?? "0");
        if (userId == 0) return Results.Unauthorized();

        var analytics = await outcomeService.GetGameAnalyticsAsync(userId, ct);
        return Results.Ok(analytics);
    });

    // GET /debug/me/game-outcomes?limit=10 - Returns recent game outcomes
    debugGroup.MapGet("/me/game-outcomes", async (
        HttpContext ctx,
        WovenBackend.Services.Games.IGameOutcomeService outcomeService,
        int? limit,
        CancellationToken ct) =>
    {
        var userId = int.Parse(ctx.User.FindFirst("sub")?.Value ?? "0");
        if (userId == 0) return Results.Unauthorized();

        var outcomes = await outcomeService.GetRecentOutcomesAsync(userId, limit ?? 10, ct);
        return Results.Ok(outcomes.Select(o => new
        {
            o.Id,
            o.SessionId,
            o.GameType,
            o.Difficulty,
            o.Tone,
            o.Bucket,
            o.IntentAlignment,
            o.InitiatorScore,
            o.PartnerScore,
            o.CompletionStatus,
            o.CreatedAt
        }));
    });
}

// SpeechBrain smoke test (Development only — never blocks startup)
if (app.Environment.IsDevelopment())
{
    try
    {
        var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "python3",
            Arguments = "scripts/speechbrain_embed.py --test",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        if (proc != null) await proc.WaitForExitAsync();
        logger.LogInformation("SpeechBrain: OK — voice embedding available");
    }
    catch
    {
        logger.LogWarning("SpeechBrain: UNAVAILABLE — voice embedding will be skipped. Install: pip install speechbrain torch torchaudio");
    }
}

app.Run();
