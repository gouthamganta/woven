using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using StackExchange.Redis;
using WovenBackend.Auth;
using WovenBackend.Data;
using WovenBackend.Endpoints;
using WovenBackend.Hubs;
using WovenBackend.Infrastructure;
using WovenBackend.Services;
using WovenBackend.Services.Queue;
using WovenBackend.Services.Security;

// ── Serilog bootstrap logger ─────────────────────────────────────────────────
// Captures startup errors before full configuration is loaded.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

// ── Azure Key Vault configuration (production) ─────────────────────────────────
// Load secrets from Azure Key Vault in production environments
if (builder.Environment.IsProduction())
{
    var keyVaultName = builder.Configuration["KeyVault:Name"];
    if (!string.IsNullOrEmpty(keyVaultName))
    {
        var keyVaultUri = new Uri($"https://{keyVaultName}.vault.azure.net/");
        builder.Configuration.AddAzureKeyVault(
            keyVaultUri,
            new Azure.Identity.DefaultAzureCredential());

        Log.Information("[Startup] Azure Key Vault configured: {KeyVaultUri}", keyVaultUri);
    }
    else
    {
        Log.Warning("[Startup] KeyVault:Name not configured — secrets will not be loaded from Key Vault");
    }
}

// ── Full Serilog configuration ────────────────────────────────────────────────
builder.Host.UseSerilog((context, services, config) =>
{
    var isProduction = context.HostingEnvironment.IsProduction();

    config
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithThreadId()
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command",
            isProduction ? LogEventLevel.Warning : LogEventLevel.Information)
        .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore.Mvc",     LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore.Routing", LogEventLevel.Warning);

    if (isProduction)
        config.WriteTo.Console(new CompactJsonFormatter());   // JSON for log aggregators
    else
        config.WriteTo.Console();                             // human-readable in dev
});

// API pods disable heavy nightly batch workers; those run on the dedicated workers pod (min=max=1).
// Lightweight real-time workers (BalloonExpiry, Moderation, TileExpiry, etc.) still run on all pods.
var batchWorkersDisabled = builder.Configuration.GetValue<bool>("WOVEN_DISABLE_BATCH_WORKERS", false);

// ----------------------------------------------------
// OBSERVABILITY — Correlation IDs + Structured Error Handling
// ----------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICorrelationService, CorrelationService>();
builder.Services.AddExceptionHandler<AuthExceptionHandler>();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ----------------------------------------------------
// RATE LIMITING
// ----------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.OnRejected = async (ctx, ct) =>
    {
        var correlationId = ctx.HttpContext.Items[CorrelationIdMiddleware.ItemsKey] as string ?? "?";
        ctx.HttpContext.Response.Headers["X-Correlation-ID"] = correlationId;
        ctx.HttpContext.Response.Headers["Retry-After"] = "60";
        await ctx.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Rate limit exceeded. Please slow down.",
            correlationId,
            retryAfterSeconds = 60
        }, ct);
    };

    // Global per-user sliding window — 120 requests / 60s
    options.AddPolicy("user", context =>
    {
        var userId = context.User.FindFirst("uid")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit          = 120,
            Window               = TimeSpan.FromSeconds(60),
            SegmentsPerWindow    = 6,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit           = 0
        });
    });

    // Strict rate limit for AI-heavy endpoints (deck generation, games, explanations)
    options.AddPolicy("ai-heavy", context =>
    {
        var userId = context.User.FindFirst("uid")?.Value ?? "anon";
        return RateLimitPartition.GetTokenBucketLimiter(userId, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit               = 10,
            QueueProcessingOrder     = QueueProcessingOrder.OldestFirst,
            QueueLimit               = 0,
            ReplenishmentPeriod      = TimeSpan.FromSeconds(60),
            TokensPerPeriod          = 10,
            AutoReplenishment        = true
        });
    });

    // OpenAI proxy limit — 5 concurrent outbound AI calls globally (prevent quota exhaustion)
    options.AddPolicy("openai-global", _ =>
        RateLimitPartition.GetConcurrencyLimiter("openai", _ => new ConcurrencyLimiterOptions
        {
            PermitLimit  = 5,
            QueueLimit   = 20,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        }));
});

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
// Explicit pool bounds prevent saturation from 14+ background workers + real-time traffic.
// PostgreSQL default max_connections is 100; we stay under it with room for migrations/admin.
npgsqlDataSourceBuilder.ConnectionStringBuilder.MaxPoolSize = 50;
npgsqlDataSourceBuilder.ConnectionStringBuilder.MinPoolSize = 2;
npgsqlDataSourceBuilder.ConnectionStringBuilder.ConnectionIdleLifetime = 300;
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
        // Phase 1D: Also accept JWT from httpOnly cookies for XSS protection.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Priority 1: SignalR query string token
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                    return Task.CompletedTask;
                }

                // Priority 2: httpOnly cookie
                var cookieToken = WovenBackend.Auth.CookieAuthHelper.GetAccessTokenFromCookie(context.Request);
                if (!string.IsNullOrEmpty(cookieToken))
                {
                    context.Token = cookieToken;
                }

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
// 03:45       — SelfDisclosureBatchWorker (nightly)
// 03:50       — ConnectionScoreBatchWorker (nightly, ECHO Phase 2)
// every 4h   — ChatNoteEmbeddingWorker
// 04:00       — WeightLearningBatchWorker (weekly Sun)
// 04:15       — PreferenceDriftBatchWorker (nightly, ECHO Phase 6)
// 05:00       — CfScoreBatchWorker (daily, collaborative filtering)
// 04:20       — LinUcbBatchWorker (nightly, ECHO Phase 7)
// 04:30       — InsightBatchWorker (nightly)
// 05:00       — SecurityAuditCleanupWorker (weekly Sun)
// 06:00       — WeeklyDigestWorker (weekly Sun)
// 18:00 Wed  — CoachingSummaryWorker (weekly Wed)
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
if (!batchWorkersDisabled) builder.Services.AddHostedService<SecurityAuditCleanupWorker>();

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
// OPENAI CLIENT — single entry point for all AI calls
// ----------------------------------------------------
// All OpenAI calls go through IOpenAiClient:
//   - Correlation ID on every request
//   - Exponential backoff on 429/5xx (up to 3 retries)
//   - Token usage logged per call
//   - Rate limit enforced via "openai-global" policy
builder.Services.AddScoped<IOpenAiClient, OpenAiClient>();

// ----------------------------------------------------
// OPENAI REWRITE SERVICE
// ----------------------------------------------------
builder.Services.AddScoped<OpenAiRewriteService>();

// ----------------------------------------------------
// AI PROFILE SERVICE
// ----------------------------------------------------
builder.Services.AddScoped<IAiProfileService, AiProfileService>();

// ----------------------------------------------------
// FOUNDATIONAL CYCLE SERVICE
// ----------------------------------------------------
builder.Services.AddScoped<FoundationalCycleService>();

builder.Services.AddScoped<WovenBackend.Services.Moments.InteractionBudgetService>();
builder.Services.AddScoped<WovenBackend.Services.Moments.SparkWalletService>();
builder.Services.AddScoped<WovenBackend.Services.Moments.MomentsMatchService>();
builder.Services.AddHostedService<WovenBackend.Services.Moments.BalloonExpiryWorker>();

builder.Services.AddScoped<OpenAiDynamicIntakeRewriteService>();
builder.Services.AddScoped<DynamicIntakeCycleService>();

// ----------------------------------------------------
// MATCHMAKING ENGINE SERVICES
// ----------------------------------------------------

// Tagging service
builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IOpenAiTaggingService,
    WovenBackend.Services.Matchmaking.OpenAiTaggingService>();

// Core matchmaking services (scoped = one instance per request)
builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IUserVectorBuilder,
    WovenBackend.Services.Matchmaking.UserVectorBuilder>();

builder.Services.AddScoped<WovenBackend.Services.Matchmaking.ICandidatePoolService,
    WovenBackend.Services.Matchmaking.CandidatePoolService>();
// ECHO Phase 3: hard filter — only age reciprocity + distance are binary exclusions
builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IHardFilterService,
    WovenBackend.Services.Matchmaking.HardFilterService>();

builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IMatchScoringService,
    WovenBackend.Services.Matchmaking.MatchScoringService>();

builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IDeliveryBoostService,
    WovenBackend.Services.Matchmaking.DeliveryBoostService>();

builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IDeckSelectionService,
    WovenBackend.Services.Matchmaking.DeckSelectionService>();

builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IMatchExplanationService,
    WovenBackend.Services.Matchmaking.MatchExplanationService>();

builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IMatchNarratorService,
    WovenBackend.Services.Matchmaking.MatchNarratorService>();

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
builder.Services.AddScoped<WovenBackend.Services.PushNotifications.IWebPushService, WovenBackend.Services.PushNotifications.WebPushService>();

// ECHO Phase 1: match signal ledger
builder.Services.AddScoped<IMatchSignalService, MatchSignalService>();

// Idempotency service for critical mutations
builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();

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
// PHASE 2A: TILES + EMBEDDING QUEUE
// ----------------------------------------------------
// TileEmbeddingService: singleton — safe to call from BackgroundService workers (IServiceScopeFactory).
builder.Services.AddSingleton<WovenBackend.Services.Tiles.TileEmbeddingService>();
builder.Services.AddScoped<WovenBackend.Services.Tiles.ITileService,
    WovenBackend.Services.Tiles.TileService>();
builder.Services.AddHostedService<WovenBackend.Services.Tiles.TileExpiryWorker>();

// Embedding task queue: durable (Azure Service Bus) when configured, in-process Channel fallback for dev.
// Service Bus queue name: "tile-embedding" — provision in Azure before production deployment.
var serviceBusConnStr = builder.Configuration["ServiceBus:ConnectionString"];
if (!string.IsNullOrWhiteSpace(serviceBusConnStr))
{
    var sbClient = new ServiceBusClient(serviceBusConnStr);
    builder.Services.AddSingleton(sbClient);
    builder.Services.AddSingleton<IEmbeddingQueue, ServiceBusEmbeddingQueue>();
    builder.Services.AddHostedService<ServiceBusEmbeddingWorker>();
}
else
{
    var inMemQueue = new InMemoryEmbeddingQueue();
    builder.Services.AddSingleton(inMemQueue);
    builder.Services.AddSingleton<IEmbeddingQueue>(inMemQueue);
    builder.Services.AddHostedService<InMemoryEmbeddingWorker>();
}

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
if (!batchWorkersDisabled) builder.Services.AddHostedService<WovenBackend.Services.Trust.TrustBatchWorker>();

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
if (!batchWorkersDisabled) builder.Services.AddHostedService<WovenBackend.Services.Seasons.SeasonTransitionWorker>();

// ----------------------------------------------------
// PHASE 3C: COLLABORATIVE FILTERING
// ----------------------------------------------------
builder.Services.AddScoped<WovenBackend.Services.Recommendations.ICollaborativeFilteringService,
    WovenBackend.Services.Recommendations.CollaborativeFilteringService>();
if (!batchWorkersDisabled) builder.Services.AddHostedService<WovenBackend.Services.Recommendations.CfBatchWorker>();
builder.Services.AddHostedService<WovenBackend.Services.Commons.TileViewProcessorWorker>();

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
// ECHO Phase 5: behavioral fingerprint (16-dim from MatchSignalLogs, no OpenAI)
builder.Services.AddScoped<WovenBackend.Services.Embeddings.IBehavioralFingerprintService,
    WovenBackend.Services.Embeddings.BehavioralFingerprintService>();
if (!batchWorkersDisabled) builder.Services.AddHostedService<WovenBackend.Services.Embeddings.EmbeddingBatchWorker>();
if (!batchWorkersDisabled) builder.Services.AddHostedService<WovenBackend.Services.Embeddings.ChatNoteEmbeddingWorker>();

// ECHO PHASE 2: CONNECTION SCORES (must run before WeightLearning)
if (!batchWorkersDisabled) builder.Services.AddHostedService<WovenBackend.Services.Matchmaking.ConnectionScoreBatchWorker>();

builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IWeightLearningService,
    WovenBackend.Services.Matchmaking.WeightLearningService>();
if (!batchWorkersDisabled) builder.Services.AddHostedService<WovenBackend.Services.Matchmaking.WeightLearningBatchWorker>();

// CF Score batch worker (05:00 daily)
if (!batchWorkersDisabled) builder.Services.AddHostedService<WovenBackend.Services.Matchmaking.CfScoreBatchWorker>();

// ECHO PHASE 6: PREFERENCE DRIFT
builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IPreferenceDriftService,
    WovenBackend.Services.Matchmaking.PreferenceDriftService>();
if (!batchWorkersDisabled) builder.Services.AddHostedService<WovenBackend.Services.Matchmaking.PreferenceDriftBatchWorker>();

// ECHO PHASE 7: LinUCB BANDIT
builder.Services.AddScoped<WovenBackend.Services.Matchmaking.ILinUcbService,
    WovenBackend.Services.Matchmaking.LinUcbService>();
if (!batchWorkersDisabled) builder.Services.AddHostedService<WovenBackend.Services.Matchmaking.LinUcbBatchWorker>();

// ECHO PHASE 8: SYNTHETIC PERSONA VALIDATION
builder.Services.AddSingleton<WovenBackend.Services.Validation.IPersonaValidationService,
    WovenBackend.Services.Validation.PersonaValidationService>();

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
if (!batchWorkersDisabled) builder.Services.AddHostedService<WovenBackend.Services.Analytics.AnalyticsRetentionWorker>();
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
if (!batchWorkersDisabled) builder.Services.AddHostedService<WovenBackend.Services.Feedback.FeedbackTriggerWorker>();

// PHASE 4C: INSIGHTS + OPINIONS
// ----------------------------------------------------
builder.Services.AddScoped<WovenBackend.Services.Insights.IInsightService,
    WovenBackend.Services.Insights.InsightService>();
if (!batchWorkersDisabled) builder.Services.AddHostedService<WovenBackend.Services.Insights.WeeklyDigestWorker>();
if (!batchWorkersDisabled) builder.Services.AddHostedService<WovenBackend.Services.Insights.InsightBatchWorker>();
if (!batchWorkersDisabled) builder.Services.AddHostedService<WovenBackend.Services.Coaching.CoachingSummaryWorker>();

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
if (!batchWorkersDisabled) builder.Services.AddHostedService<WovenBackend.Services.AntiGhosting.GhostDetectionWorker>();

// ----------------------------------------------------
// ECHO PHASE 1: SELF-DISCLOSURE SIGNAL
// ----------------------------------------------------
if (!batchWorkersDisabled) builder.Services.AddHostedService<WovenBackend.Services.Moments.SelfDisclosureBatchWorker>();

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
// DATABASE MIGRATIONS
// ----------------------------------------------------
// Development: always auto-migrate.
// Production first deploy: set WOVEN_RUN_MIGRATIONS=true in Terraform (var.run_migrations).
//   Remove it after the first successful deploy — schema exists, no need to run on every pod start.
// Production normal: migrations checked; error logged if pending (run via CI/CD step).
var runMigrations = app.Environment.IsDevelopment()
    || app.Configuration.GetValue<bool>("WOVEN_RUN_MIGRATIONS", false);

if (runMigrations)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
    try
    {
        logger.LogInformation("Applying pending database migrations...");
        db.Database.Migrate();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to apply database migrations.");
    }
}
else
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
    var pending = db.Database.GetPendingMigrations().ToList();
    if (pending.Count > 0)
        logger.LogError(
            "[STARTUP] {Count} pending migration(s) in production: {Migrations}. " +
            "Run via CI/CD or set WOVEN_RUN_MIGRATIONS=true for first deploy.",
            pending.Count, string.Join(", ", pending));
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

// ── 1. Correlation ID — must be first so all downstream logs carry it ─────────
app.UseMiddleware<CorrelationIdMiddleware>();

// ── 2. Serilog request logging — logs every request with timing + status code ─
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} → {StatusCode} in {Elapsed:0.000}ms " +
        "[CorrelationId={CorrelationId} UserId={UserId}]";

    options.EnrichDiagnosticContext = (diag, context) =>
    {
        diag.Set("CorrelationId", context.Items[CorrelationIdMiddleware.ItemsKey] ?? "?");
        diag.Set("UserId",        context.User.FindFirst("uid")?.Value ?? "anon");
        diag.Set("UserAgent",     context.Request.Headers.UserAgent.ToString());
    };
});

// ── 3. Structured exception handling (replaces the old errorApp lambda) ───────
app.UseExceptionHandler();

// ── 4. Rate limiting ──────────────────────────────────────────────────────────
app.UseRateLimiter();

app.UseCors("DefaultCorsPolicy");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseHttpsRedirection();
}
// HTTPS redirect NOT used in production — Container Apps handles TLS termination at ingress

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
app.MapCoachingEndpoints();
app.MapDynamicIntakeEndpoints();
app.MapMediaEndpoints();
app.MapTileEndpoints();
app.MapAdminEndpoints();
app.MapAdminSecurityEndpoints();
app.MapAdminValidationEndpoints();
app.MapUserDataEndpoints();
app.MapPushEndpoints();

var pushNotifications = app.MapGroup("/push-notifications");
pushNotifications.MapPushNotificationEndpoints();

app.MapCommonsEndpoints();
app.MapOrbitEndpoints();
app.MapSparkEndpoints();
app.MapSeasonEndpoints();
app.MapMeEndpoints();
app.MapFeedbackEndpoints();
app.MapVerificationEndpoints();
app.MapAdminAnalyticsEndpoints();
app.MapLegalEndpoints();
app.MapSupportEndpoints();

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
        var baseQuestions = FoundationalQuestionBank.GetQuestionsForVersion(1);

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

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();   // Ensure all buffered log entries are flushed before process exits
}
