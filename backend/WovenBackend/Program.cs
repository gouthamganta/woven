using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using WovenBackend.Auth;
using WovenBackend.Data;
using WovenBackend.Endpoints;
using WovenBackend.Services;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------
// JSON
// ----------------------------------------------------
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// ----------------------------------------------------
// CORS (environment-aware)
// ----------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            var allowedOrigins = builder.Configuration["Cors:AllowedOrigins"]?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? Array.Empty<string>();
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});

// ----------------------------------------------------
// DATABASE
// ----------------------------------------------------
builder.Services.AddDbContext<WovenDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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
    });

builder.Services.AddAuthorization();

// ----------------------------------------------------
// HTTP CLIENT (REQUIRED)
// ----------------------------------------------------
// NOTE: You already register a default HttpClient below,
// so you do NOT need multiple AddHttpClient() calls.
builder.Services.AddHttpClient();

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

builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IDailyDeckOrchestrator,
    WovenBackend.Services.Matchmaking.DailyDeckOrchestrator>();

builder.Services.AddScoped<WovenBackend.Services.Matchmaking.IMatchOutcomeService,
    WovenBackend.Services.Matchmaking.MatchOutcomeService>();

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
// BUILD APP
// ----------------------------------------------------
var app = builder.Build();

// ----------------------------------------------------
// MIDDLEWARE
// ----------------------------------------------------
app.UseCors("AllowAll");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
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
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// ----------------------------------------------------
// HEALTH
// ----------------------------------------------------
app.MapGet("/health", async (WovenDbContext db) =>
{
    try
    {
        await db.Database.CanConnectAsync();
        return Results.Ok(new { status = "ok", database = "connected" });
    }
    catch
    {
        return Results.Json(
            new { status = "degraded", database = "unavailable" },
            statusCode: 503
        );
    }
});

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

app.Run();
