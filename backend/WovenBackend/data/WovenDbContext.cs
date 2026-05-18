using Microsoft.EntityFrameworkCore;
using WovenBackend.Data.Converters;
using WovenBackend.Data.Entities;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Data.Entities.Games;
using WovenBackend.Services.Security;

namespace WovenBackend.Data;

public class WovenDbContext : DbContext
{
    private readonly IEncryptionService? _enc;

    public WovenDbContext(DbContextOptions<WovenDbContext> options) : base(options) { }

    public WovenDbContext(DbContextOptions<WovenDbContext> options, IEncryptionService enc)
        : base(options)
    {
        _enc = enc;
    }

    // User tables
    public DbSet<User> Users => Set<User>();
    public DbSet<AuthIdentity> AuthIdentities => Set<AuthIdentity>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<UserPhoto> UserPhotos => Set<UserPhoto>();
    public DbSet<UserIntent> UserIntents => Set<UserIntent>();
    public DbSet<UserFoundationalV1> UserFoundationalV1s => Set<UserFoundationalV1>();
    public DbSet<UserFoundationalQuestionSet> UserFoundationalQuestionSets => Set<UserFoundationalQuestionSet>();
    public DbSet<UserOptionalField> UserOptionalFields => Set<UserOptionalField>();
    public DbSet<UserWeeklyVibe> UserWeeklyVibes => Set<UserWeeklyVibe>();
    public DbSet<UserDynamicIntakeSet> UserDynamicIntakeSets => Set<UserDynamicIntakeSet>();

    // Moments tables
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<DailyInteraction> DailyInteractions => Set<DailyInteraction>();
    public DbSet<PendingMatch> PendingMatches => Set<PendingMatch>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<MomentResponse> MomentResponses => Set<MomentResponse>();
    public DbSet<ChatThread> ChatThreads => Set<ChatThread>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<UserRating> UserRatings => Set<UserRating>();

    // Matchmaking engine tables
    public DbSet<UserVector> UserVectors => Set<UserVector>();
    public DbSet<UserVectorTag> UserVectorTags => Set<UserVectorTag>();
    public DbSet<DailyDeck> DailyDecks => Set<DailyDeck>();
    public DbSet<MatchExplanation> MatchExplanations => Set<MatchExplanation>();
    public DbSet<MatchOutcome> MatchOutcomes => Set<MatchOutcome>();
    public DbSet<CandidateExposure> CandidateExposures => Set<CandidateExposure>();
    public DbSet<CandidateSignal> CandidateSignals => Set<CandidateSignal>();

    // Games tables
    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<GameRound> GameRounds => Set<GameRound>();
    public DbSet<GameResult> GameResults => Set<GameResult>();
    public DbSet<GameAnalytic> GameAnalytics => Set<GameAnalytic>();
    public DbSet<GameOutcome> GameOutcomes => Set<GameOutcome>();

    // Tiles (Phase 2A)
    public DbSet<Tile> Tiles => Set<Tile>();
    public DbSet<Highlight> Highlights => Set<Highlight>();

    // Moderation + Trust (Phase 2B)
    public DbSet<ModerationQueue> ModerationQueues => Set<ModerationQueue>();
    public DbSet<TileReport> TileReports => Set<TileReport>();

    // Commons feed (Phase 2C)
    public DbSet<TileView> TileViews => Set<TileView>();
    public DbSet<UserEnergyMeter> UserEnergyMeters => Set<UserEnergyMeter>();

    // Orbit + Friend Bridge (Phase 3A)
    public DbSet<TileOrbit> TileOrbits => Set<TileOrbit>();
    public DbSet<TileEngagement> TileEngagements => Set<TileEngagement>();
    public DbSet<FriendBridge> FriendBridges => Set<FriendBridge>();
    public DbSet<OrbitGravity> OrbitGravities => Set<OrbitGravity>();

    // Seasons (Phase 3B)
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<UserSeasonResponse> UserSeasonResponses => Set<UserSeasonResponse>();

    // Collaborative Filtering (Phase 3C)
    public DbSet<CfScore> CfScores => Set<CfScore>();

    // Enhanced Embeddings (Phase 3D)
    public DbSet<PhotoEmbedding> PhotoEmbeddings => Set<PhotoEmbedding>();
    public DbSet<UserVisualDecision> UserVisualDecisions => Set<UserVisualDecision>();
    public DbSet<UserVisualPreference> UserVisualPreferences => Set<UserVisualPreference>();
    public DbSet<UserVoicePreference> UserVoicePreferences => Set<UserVoicePreference>();
    public DbSet<UserMatchingWeight> UserMatchingWeights => Set<UserMatchingWeight>();

    // Security + Audit (Phase 3E)
    public DbSet<SecurityAuditLog> SecurityAuditLogs => Set<SecurityAuditLog>();

    // Insights + Opinions (Phase 4C)
    public DbSet<UserInsight> UserInsights => Set<UserInsight>();

    // Pre-Date Bridge (Phase 4D)
    public DbSet<ChatAvailabilitySignal> ChatAvailabilitySignals => Set<ChatAvailabilitySignal>();
    public DbSet<DateFeedback> DateFeedbacks => Set<DateFeedback>();
    public DbSet<DateFeedbackPrompt> DateFeedbackPrompts => Set<DateFeedbackPrompt>();

    // Catfish Detection (Phase 4E)
    public DbSet<ReferencePhotoEmbedding> ReferencePhotoEmbeddings => Set<ReferencePhotoEmbedding>();

    // Identity Verification (Phase 5A)
    public DbSet<UserVerification> UserVerifications => Set<UserVerification>();

    // Analytics Engine (Phase 5C)
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
    public DbSet<AbExperiment> AbExperiments => Set<AbExperiment>();
    public DbSet<AbAssignment> AbAssignments => Set<AbAssignment>();
    public DbSet<AbConversion> AbConversions => Set<AbConversion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User entity configuration
        modelBuilder.Entity<User>()
            .HasIndex(x => x.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .Property(x => x.ProfileStatus)
            .HasConversion<string>();

        // AuthIdentity: provider + subject must be unique
        modelBuilder.Entity<AuthIdentity>()
            .HasIndex(x => new { x.Provider, x.ProviderSubject })
            .IsUnique();

        modelBuilder.Entity<AuthIdentity>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // UserProfile: 1:1 with User
        modelBuilder.Entity<UserProfile>()
            .HasOne(x => x.User)
            .WithOne()
            .HasForeignKey<UserProfile>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserProfile>()
            .HasIndex(x => x.UserId)
            .IsUnique();

        // UserPreference: 1:1 with User
        modelBuilder.Entity<UserPreference>()
            .HasOne(x => x.User)
            .WithOne()
            .HasForeignKey<UserPreference>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserPreference>()
            .HasIndex(x => x.UserId)
            .IsUnique();

        modelBuilder.Entity<UserPreference>()
            .Property(x => x.AgeMin)
            .HasDefaultValue(18);

        modelBuilder.Entity<UserPreference>()
            .Property(x => x.AgeMax)
            .HasDefaultValue(99);

        // UserPhoto: 1:many with User
        modelBuilder.Entity<UserPhoto>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserPhoto>()
            .HasIndex(x => new { x.UserId, x.SortOrder });

        // UserIntent: 1:1 with User
        modelBuilder.Entity<UserIntent>()
            .HasOne(x => x.User)
            .WithOne()
            .HasForeignKey<UserIntent>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserIntent>()
            .HasIndex(x => x.UserId)
            .IsUnique();

        // UserFoundationalV1: 1:1 with User
        modelBuilder.Entity<UserFoundationalV1>()
            .HasOne(x => x.User)
            .WithOne()
            .HasForeignKey<UserFoundationalV1>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserFoundationalV1>()
            .HasIndex(x => x.UserId)
            .IsUnique();

        // UserFoundationalQuestionSet: 1:many with User
        modelBuilder.Entity<UserFoundationalQuestionSet>()
            .HasIndex(x => new { x.UserId, x.Version })
            .IsUnique();

        modelBuilder.Entity<UserFoundationalQuestionSet>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Ensure only ONE active (unanswered) set per user
        modelBuilder.Entity<UserFoundationalQuestionSet>()
            .HasIndex(x => x.UserId)
            .HasFilter("\"AnsweredAt\" IS NULL")
            .IsUnique();

        // UserOptionalField: 1:many with User
        modelBuilder.Entity<UserOptionalField>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserOptionalField>()
            .HasIndex(x => new { x.UserId, x.Key })
            .IsUnique();

        modelBuilder.Entity<UserOptionalField>()
            .Property(x => x.Visibility)
            .HasConversion<string>();

        // UserWeeklyVibe: 1:1 with User (but can be replaced weekly)
        modelBuilder.Entity<UserWeeklyVibe>()
            .HasOne(x => x.User)
            .WithOne()
            .HasForeignKey<UserWeeklyVibe>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserWeeklyVibe>()
            .HasIndex(x => x.UserId)
            .IsUnique();

        modelBuilder.Entity<UserWeeklyVibe>()
            .HasIndex(x => x.ExpiresAt);

        // UserDynamicIntakeSet: 1:many with User
        modelBuilder.Entity<UserDynamicIntakeSet>()
            .HasIndex(x => new { x.UserId, x.CycleId })
            .IsUnique();

        modelBuilder.Entity<UserDynamicIntakeSet>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DailyInteraction>()
            .HasKey(x => new { x.UserId, x.DateUtc });

        modelBuilder.Entity<Block>()
            .HasKey(x => new { x.BlockerId, x.BlockedId });

        // ===============================
        // Moments: Relationships (FKs)
        // ===============================

        // matches -> users
        modelBuilder.Entity<Match>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserAId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Match>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserBId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Match>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.EdgeOwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // daily_interactions -> users
        modelBuilder.Entity<DailyInteraction>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // pending_matches -> users
        modelBuilder.Entity<PendingMatch>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PendingMatch>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.TargetUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // blocks -> users
        modelBuilder.Entity<Block>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.BlockerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Block>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.BlockedId)
            .OnDelete(DeleteBehavior.Cascade);

        // moment_responses -> users
        modelBuilder.Entity<MomentResponse>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.FromUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MomentResponse>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ToUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ===============================
        // Chat: Relationships (FKs)
        // ===============================

        // 1 thread per match
        modelBuilder.Entity<ChatThread>()
            .HasIndex(x => x.MatchId)
            .IsUnique();

        modelBuilder.Entity<ChatThread>()
            .HasOne<Match>()
            .WithMany()
            .HasForeignKey(x => x.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // Message rules
        modelBuilder.Entity<ChatMessage>()
            .HasIndex(x => x.ThreadId);

        modelBuilder.Entity<ChatMessage>()
            .HasOne<ChatThread>()
            .WithMany()
            .HasForeignKey(x => x.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ChatMessage>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.SenderUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ===============================
        // Moments: Enum conversions to string
        // ===============================
        modelBuilder.Entity<Match>()
            .Property(x => x.MatchType)
            .HasConversion<string>();

        modelBuilder.Entity<Match>()
            .Property(x => x.BalloonState)
            .HasConversion<string>();

        modelBuilder.Entity<Match>()
            .Property(x => x.ClosedReason)
            .HasConversion<string>();

        modelBuilder.Entity<MomentResponse>()
            .Property(x => x.Choice)
            .HasConversion<string>();

        // ===============================
        // Moments: Basic safety checks
        // ===============================
        modelBuilder.Entity<Match>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_matches_no_self",
                "\"user_a_id\" <> \"user_b_id\""
            ));

        modelBuilder.Entity<PendingMatch>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_pending_no_self",
                "\"user_id\" <> \"target_user_id\""
            ));

        modelBuilder.Entity<Block>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_blocks_no_self",
                "\"blocker_id\" <> \"blocked_id\""
            ));

        modelBuilder.Entity<MomentResponse>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_moment_responses_no_self",
                "\"from_user_id\" <> \"to_user_id\""
            ));

        // Chat: Basic safety checks
        modelBuilder.Entity<ChatMessage>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_chat_messages_body_len",
                "length(\"body\") >= 1 AND length(\"body\") <= 1000"
            ));

        // ===============================
        // Moments: Invariants (CHECK + UNIQUE)
        // ===============================

        // 1) matches: ACTIVE vs CLOSED consistency
        modelBuilder.Entity<Match>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_matches_state_closed_fields",
                @"
                (
                  ""balloon_state"" = 'ACTIVE' AND
                  ""closed_reason"" IS NULL AND
                  ""closed_at"" IS NULL
                )
                OR
                (
                  ""balloon_state"" = 'CLOSED' AND
                  ""closed_reason"" IS NOT NULL AND
                  ""closed_at"" IS NOT NULL
                )
                "
            ));

        // 2) matches: PURE vs EDGE consistency
        modelBuilder.Entity<Match>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_matches_type_edge_owner",
                @"
                (
                  ""match_type"" = 'PURE' AND
                  ""edge_owner_id"" IS NULL
                )
                OR
                (
                  ""match_type"" = 'EDGE' AND
                  ""edge_owner_id"" IS NOT NULL
                )
                "
            ));

        // 3) matches: expires_at must be after created_at
        modelBuilder.Entity<Match>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_matches_expires_after_created",
                "\"expires_at\" > \"created_at\""
            ));

        // 4) daily_interactions caps
        modelBuilder.Entity<DailyInteraction>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_daily_total_cap",
                "\"total_used\" >= 0 AND \"total_used\" <= 5"
            ));

        modelBuilder.Entity<DailyInteraction>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_daily_pending_cap",
                "\"pending_used\" >= 0 AND \"pending_used\" <= 2"
            ));

        modelBuilder.Entity<DailyInteraction>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_daily_pending_le_total",
                "\"pending_used\" <= \"total_used\""
            ));

        // 5) pending_matches: prevent duplicates (user_id, target_user_id)
        modelBuilder.Entity<PendingMatch>()
            .HasIndex(x => new { x.UserId, x.TargetUserId })
            .IsUnique();

        // 6) moment_responses: prevent duplicate response spam (one response per day/from/to)
        modelBuilder.Entity<MomentResponse>()
            .HasIndex(x => new { x.DateUtc, x.FromUserId, x.ToUserId })
            .IsUnique();

        // ===============================
        // Moments: Indexes
        // ===============================

        // matches: prevent duplicate ACTIVE matches between same users
        modelBuilder.Entity<Match>()
            .HasIndex(x => new { x.UserAId, x.UserBId, x.BalloonState })
            .IsUnique()
            .HasFilter("\"balloon_state\" = 'ACTIVE'");

        // matches queries: find user's active matches
        modelBuilder.Entity<Match>()
            .HasIndex(x => new { x.UserAId, x.BalloonState, x.ExpiresAt });

        modelBuilder.Entity<Match>()
            .HasIndex(x => new { x.UserBId, x.BalloonState, x.ExpiresAt });

        // expiry job: find active expired balloons quickly
        modelBuilder.Entity<Match>()
            .HasIndex(x => new { x.BalloonState, x.ExpiresAt });

        // pending queries: find user's pending matches
        modelBuilder.Entity<PendingMatch>()
            .HasIndex(x => new { x.UserId, x.CreatedAt });

        // blocks queries: who did I block / who blocked me
        modelBuilder.Entity<Block>()
            .HasIndex(x => x.BlockerId);

        modelBuilder.Entity<Block>()
            .HasIndex(x => x.BlockedId);

        // ===============================
        // MATCHMAKING ENGINE CONFIGURATION
        // ===============================

        // Phase 1A: register pgvector extension so EF Core migrations emit CREATE EXTENSION
        modelBuilder.HasPostgresExtension("vector");

        // UserPreference: Add RelationshipStructure enum conversion
        modelBuilder.Entity<UserPreference>()
            .Property(x => x.RelationshipStructure)
            .HasConversion<string>();

        // UserVector: 1:many with User (versioned)
        modelBuilder.Entity<UserVector>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserVector>()
            .HasIndex(x => new { x.UserId, x.Version })
            .IsUnique();

        // PillarEmbedding: 1536-dim pgvector column (upgraded from 8-dim in Phase 3D).
        // HNSW index created in migration via raw SQL.
        modelBuilder.Entity<UserVector>()
            .Property(x => x.PillarEmbedding)
            .HasColumnType("vector(1536)");

        // UserVectorTag: 1:many with User (for fast tag queries)
        modelBuilder.Entity<UserVectorTag>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserVectorTag>()
            .HasIndex(x => new { x.UserId, x.Version, x.TagType });

        modelBuilder.Entity<UserVectorTag>()
            .HasIndex(x => new { x.Tag, x.TagType });

        modelBuilder.Entity<CandidateExposure>()
            .HasIndex(x => new { x.ViewerUserId, x.DateUtc });

        modelBuilder.Entity<CandidateExposure>()
            .HasIndex(x => new { x.ShownUserId, x.CreatedAt });

        modelBuilder.Entity<CandidateExposure>()
            .HasIndex(x => new { x.ViewerUserId, x.ShownUserId, x.DateUtc, x.Surface })
            .IsUnique();

        modelBuilder.Entity<CandidateSignal>()
            .HasIndex(x => new { x.FromUserId, x.ToUserId, x.Type, x.CreatedAt });

        modelBuilder.Entity<CandidateSignal>()
            .HasIndex(x => new { x.ToUserId, x.ExpiresAt });

        // DailyDeck: 1:many with User (one deck per day)
        modelBuilder.Entity<DailyDeck>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DailyDeck>()
            .HasIndex(x => new { x.UserId, x.DateUtc })
            .IsUnique();

        // MatchExplanation: 1:many with User
        modelBuilder.Entity<MatchExplanation>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MatchExplanation>()
            .HasIndex(x => new { x.UserId, x.CandidateId, x.DateUtc });

        // MatchOutcome: relationships
        modelBuilder.Entity<MatchOutcome>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MatchOutcome>()
            .HasIndex(x => x.MatchId);

        modelBuilder.Entity<MatchOutcome>()
            .HasIndex(x => new { x.UserId, x.CandidateId, x.DateUtc });

        // ===============================
        // GAMES: Relationships & Indexes
        // ===============================

        // GameSession -> Match
        modelBuilder.Entity<GameSession>()
            .HasOne<Match>()
            .WithMany()
            .HasForeignKey(x => x.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GameSession>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.InitiatorUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GameSession>()
            .HasIndex(x => new { x.MatchId, x.Status });

        modelBuilder.Entity<GameSession>()
            .HasIndex(x => new { x.ExpiresAt, x.Status });

        // GameRound -> GameSession
        modelBuilder.Entity<GameRound>()
            .HasOne<GameSession>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GameRound>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.GuesserUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GameRound>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.TargetUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GameRound>()
            .HasIndex(x => new { x.SessionId, x.RoundNumber });

        // GameResult -> GameSession
        modelBuilder.Entity<GameResult>()
            .HasOne<GameSession>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GameResult>()
            .HasOne<Match>()
            .WithMany()
            .HasForeignKey(x => x.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GameResult>()
            .HasIndex(x => new { x.MatchId, x.CreatedAt });

        // GameAnalytic -> GameSession
        modelBuilder.Entity<GameAnalytic>()
            .HasOne<GameSession>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GameAnalytic>()
            .HasIndex(x => new { x.GameType, x.Completed });

        // Add games_initiated check constraint
        modelBuilder.Entity<DailyInteraction>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_daily_games_cap",
                "\"games_initiated\" >= 0 AND \"games_initiated\" <= 2"
            ));

        // ===============================
        // GAME OUTCOMES CONFIGURATION
        // ===============================

        // GameOutcome -> GameSession
        modelBuilder.Entity<GameOutcome>()
            .HasOne(x => x.Session)
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        // GameOutcome -> Initiator User
        modelBuilder.Entity<GameOutcome>()
            .HasOne(x => x.InitiatorUser)
            .WithMany()
            .HasForeignKey(x => x.InitiatorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // GameOutcome -> Partner User
        modelBuilder.Entity<GameOutcome>()
            .HasOne(x => x.PartnerUser)
            .WithMany()
            .HasForeignKey(x => x.PartnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // GameOutcome -> Match
        modelBuilder.Entity<GameOutcome>()
            .HasOne(x => x.Match)
            .WithMany()
            .HasForeignKey(x => x.MatchId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes for GameOutcome
        modelBuilder.Entity<GameOutcome>()
            .HasIndex(x => x.SessionId)
            .IsUnique();

        modelBuilder.Entity<GameOutcome>()
            .HasIndex(x => x.InitiatorUserId);

        modelBuilder.Entity<GameOutcome>()
            .HasIndex(x => x.PartnerUserId);

        modelBuilder.Entity<GameOutcome>()
            .HasIndex(x => x.MatchId);

        modelBuilder.Entity<GameOutcome>()
            .HasIndex(x => new { x.InitiatorUserId, x.CreatedAt });

        modelBuilder.Entity<GameOutcome>()
            .HasIndex(x => new { x.PartnerUserId, x.CreatedAt });

        // ===============================
        // PHASE 2A: TILES & HIGHLIGHTS
        // ===============================

        // Tile -> Users
        modelBuilder.Entity<Tile>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Tile>()
            .HasIndex(x => new { x.UserId, x.IsExpired });

        modelBuilder.Entity<Tile>()
            .HasIndex(x => new { x.ExpiresAt, x.IsExpired });

        modelBuilder.Entity<Tile>()
            .HasIndex(x => new { x.IsModerated, x.IsExpired, x.CreatedAt });

        // vector column type — HNSW index added in migration via raw SQL
        modelBuilder.Entity<Tile>()
            .Property(x => x.Embedding)
            .HasColumnType("vector(1536)");

        modelBuilder.Entity<Tile>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_tiles_content_type",
                "\"content_type\" IN ('text','photo','video','voice')"));

        modelBuilder.Entity<Tile>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_tiles_expires_after_created",
                "\"expires_at\" > \"created_at\""));

        // Highlight -> Users
        modelBuilder.Entity<Highlight>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Highlight -> Tile (cascade: removing a tile removes its highlight rows)
        modelBuilder.Entity<Highlight>()
            .HasOne(x => x.Tile)
            .WithMany()
            .HasForeignKey(x => x.TileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Highlight>()
            .HasIndex(x => new { x.UserId, x.SlotNumber })
            .IsUnique();

        modelBuilder.Entity<Highlight>()
            .HasIndex(x => x.UserId);

        modelBuilder.Entity<Highlight>()
            .HasIndex(x => x.TileId);

        modelBuilder.Entity<Highlight>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_highlights_slot_range",
                "\"slot_number\" >= 1 AND \"slot_number\" <= 9"));

        // UserVector.ExpressionEmbedding (Phase 2A) — HNSW index in migration SQL
        modelBuilder.Entity<UserVector>()
            .Property(x => x.ExpressionEmbedding)
            .HasColumnType("vector(1536)");

        // ===============================
        // USER RATINGS CONFIGURATION
        // ===============================

        modelBuilder.Entity<UserRating>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RatedUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserRating>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RaterUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserRating>()
            .HasOne<Match>()
            .WithMany()
            .HasForeignKey(x => x.MatchId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<UserRating>()
            .HasIndex(x => new { x.RatedUserId, x.RaterUserId, x.MatchId });

        modelBuilder.Entity<UserRating>()
            .HasIndex(x => x.RatedUserId);

        // Rating value check constraint (-100 to +100)
        modelBuilder.Entity<UserRating>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_rating_value_range",
                "\"rating_value\" >= -100 AND \"rating_value\" <= 100"
            ));

        // ===============================
        // PHASE 2B: MODERATION + TRUST
        // ===============================

        // ModerationQueue -> Tile
        modelBuilder.Entity<ModerationQueue>()
            .HasOne(x => x.Tile)
            .WithMany()
            .HasForeignKey(x => x.TileId)
            .HasConstraintName("fk_moderation_tile")
            .OnDelete(DeleteBehavior.Cascade);

        // ModerationQueue -> User
        modelBuilder.Entity<ModerationQueue>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .HasConstraintName("fk_moderation_user")
            .OnDelete(DeleteBehavior.Cascade);

        // Partial unique: one pending record per tile
        modelBuilder.Entity<ModerationQueue>()
            .HasIndex(x => x.TileId)
            .HasDatabaseName("uq_moderation_queue_tile")
            .HasFilter("reviewed_at IS NULL")
            .IsUnique();

        modelBuilder.Entity<ModerationQueue>()
            .HasIndex(x => x.QueuedAt)
            .HasDatabaseName("IX_moderation_queue_queued_at")
            .HasFilter("reviewed_at IS NULL");

        modelBuilder.Entity<ModerationQueue>()
            .HasIndex(x => x.UserId)
            .HasDatabaseName("IX_moderation_queue_user_id");

        modelBuilder.Entity<ModerationQueue>()
            .Property(x => x.Decision)
            .HasMaxLength(20);

        modelBuilder.Entity<ModerationQueue>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_moderation_decision",
                "decision IN ('approved','rejected')"));

        // TileReport -> Tile
        modelBuilder.Entity<TileReport>()
            .HasOne(x => x.Tile)
            .WithMany()
            .HasForeignKey(x => x.TileId)
            .HasConstraintName("fk_tile_reports_tile")
            .OnDelete(DeleteBehavior.Cascade);

        // TileReport -> User
        modelBuilder.Entity<TileReport>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ReporterId)
            .HasConstraintName("fk_tile_reports_user")
            .OnDelete(DeleteBehavior.Cascade);

        // One report per (tile, reporter)
        modelBuilder.Entity<TileReport>()
            .HasIndex(x => new { x.TileId, x.ReporterId })
            .HasDatabaseName("uq_tile_reports_user_tile")
            .IsUnique();

        modelBuilder.Entity<TileReport>()
            .HasIndex(x => x.TileId)
            .HasDatabaseName("IX_tile_reports_tile_id");

        modelBuilder.Entity<TileReport>()
            .HasIndex(x => x.ReporterId)
            .HasDatabaseName("IX_tile_reports_reporter_id");

        // User.TrustScore default
        modelBuilder.Entity<User>()
            .Property(x => x.TrustScore)
            .HasDefaultValue(0.5f);

        // ===============================
        // PHASE 2C: COMMONS FEED
        // ===============================

        modelBuilder.Entity<TileView>()
            .HasKey(x => new { x.UserId, x.TileId, x.ViewedAt });

        modelBuilder.Entity<TileView>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TileView>()
            .HasOne(x => x.Tile)
            .WithMany()
            .HasForeignKey(x => x.TileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TileView>()
            .HasIndex(x => new { x.UserId, x.ViewedAt })
            .HasDatabaseName("ix_tile_views_user_date");

        modelBuilder.Entity<TileView>()
            .HasIndex(x => x.TileId)
            .HasDatabaseName("ix_tile_views_tile_id");

        modelBuilder.Entity<UserEnergyMeter>()
            .HasKey(x => new { x.UserId, x.DateUtc });

        modelBuilder.Entity<UserEnergyMeter>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserEnergyMeter>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_energy_tiles_nonnegative",
                "\"tiles_viewed\" >= 0"));

        // ===============================
        // PHASE 3A: ORBIT + FRIEND BRIDGE
        // ===============================

        modelBuilder.Entity<TileOrbit>()
            .HasOne(x => x.Tile)
            .WithMany()
            .HasForeignKey(x => x.TileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TileOrbit>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.OrbiterId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TileOrbit>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.TileOwnerId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<TileOrbit>()
            .HasIndex(x => new { x.OrbiterId, x.TileId })
            .HasDatabaseName("uq_tile_orbits_orbiter_tile")
            .IsUnique();

        modelBuilder.Entity<TileOrbit>()
            .HasIndex(x => x.OrbiterId)
            .HasDatabaseName("ix_tile_orbits_orbiter_id");

        modelBuilder.Entity<TileOrbit>()
            .HasIndex(x => x.TileOwnerId)
            .HasDatabaseName("ix_tile_orbits_tile_owner_id");

        modelBuilder.Entity<TileOrbit>()
            .HasIndex(x => x.TileId)
            .HasDatabaseName("ix_tile_orbits_tile_id");

        modelBuilder.Entity<TileOrbit>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_tile_orbits_relationship_type",
                "\"relationship_type\" IN ('romantic', 'social')"));

        modelBuilder.Entity<TileEngagement>()
            .HasOne(x => x.Tile)
            .WithMany()
            .HasForeignKey(x => x.TileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TileEngagement>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TileEngagement>()
            .HasIndex(x => new { x.UserId, x.CreatedAt })
            .HasDatabaseName("ix_tile_engagement_user_created");

        modelBuilder.Entity<TileEngagement>()
            .HasIndex(x => x.TileId)
            .HasDatabaseName("ix_tile_engagement_tile_id");

        modelBuilder.Entity<TileEngagement>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_tile_engagement_type",
                "\"engagement_type\" IN ('viewed','expanded','media_played','media_completed','replayed')"));

        modelBuilder.Entity<FriendBridge>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserAId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FriendBridge>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserBId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<FriendBridge>()
            .HasIndex(x => new { x.UserAId, x.UserBId })
            .HasDatabaseName("uq_friend_bridges_pair")
            .IsUnique();

        modelBuilder.Entity<FriendBridge>()
            .HasIndex(x => new { x.UserAId, x.Status })
            .HasDatabaseName("ix_friend_bridges_user_a");

        modelBuilder.Entity<FriendBridge>()
            .HasIndex(x => new { x.UserBId, x.Status })
            .HasDatabaseName("ix_friend_bridges_user_b");

        modelBuilder.Entity<FriendBridge>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_friend_bridges_status",
                "\"status\" IN ('pending_both','a_accepted','b_accepted','active','declined')"));

        modelBuilder.Entity<OrbitGravity>()
            .HasKey(x => new { x.UserId, x.CandidateId });

        modelBuilder.Entity<OrbitGravity>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OrbitGravity>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CandidateId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<OrbitGravity>()
            .HasIndex(x => new { x.UserId, x.Score })
            .HasDatabaseName("ix_orbit_gravity_user_score");

        // ===============================
        // PHASE 3B: SEASONS
        // ===============================

        modelBuilder.Entity<Season>()
            .HasIndex(x => x.SeasonNumber)
            .HasDatabaseName("uq_seasons_season_number")
            .IsUnique();

        modelBuilder.Entity<UserSeasonResponse>()
            .HasOne(x => x.Season)
            .WithMany(x => x.Responses)
            .HasForeignKey(x => x.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserSeasonResponse>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserSeasonResponse>()
            .HasIndex(x => new { x.UserId, x.SeasonId, x.PillarId })
            .HasDatabaseName("uq_user_season_pillar")
            .IsUnique();

        modelBuilder.Entity<UserSeasonResponse>()
            .HasIndex(x => new { x.UserId, x.SeasonId })
            .HasDatabaseName("ix_user_season_responses_user_season");

        // ===============================
        // PHASE 3C: COLLABORATIVE FILTERING
        // ===============================

        modelBuilder.Entity<CfScore>()
            .HasKey(x => new { x.UserId, x.CandidateId });

        modelBuilder.Entity<CfScore>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CfScore>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CandidateId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CfScore>()
            .HasIndex(x => new { x.UserId, x.Score })
            .HasDatabaseName("ix_cf_scores_user_score");

        modelBuilder.Entity<CfScore>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_cf_scores_no_self",
                "\"user_id\" <> \"candidate_id\""));

        // ===============================
        // PHASE 3D: ENHANCED EMBEDDINGS
        // ===============================

        // UserVector: new Phase 3D embedding columns
        modelBuilder.Entity<UserVector>()
            .Property(x => x.IntentEmbedding)
            .HasColumnType("vector(1536)");

        modelBuilder.Entity<UserVector>()
            .Property(x => x.StyleEmbedding)
            .HasColumnType("vector(128)");

        modelBuilder.Entity<UserVector>()
            .Property(x => x.HumorEmbedding)
            .HasColumnType("vector(64)");

        modelBuilder.Entity<UserVector>()
            .Property(x => x.LifestyleEmbedding)
            .HasColumnType("vector(128)");

        modelBuilder.Entity<UserVector>()
            .Property(x => x.EmotionalRhythmEmbedding)
            .HasColumnType("vector(48)");

        modelBuilder.Entity<UserVector>()
            .Property(x => x.AttachmentProxyEmbedding)
            .HasColumnType("vector(4)");

        // Tile.VoiceEmbedding
        modelBuilder.Entity<Tile>()
            .Property(x => x.VoiceEmbedding)
            .HasColumnType("vector(192)");

        // PhotoEmbedding
        modelBuilder.Entity<PhotoEmbedding>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PhotoEmbedding>()
            .Property(x => x.Embedding)
            .HasColumnType("vector(512)");

        modelBuilder.Entity<PhotoEmbedding>()
            .HasIndex(x => x.UserId)
            .HasDatabaseName("ix_photo_embeddings_user_id");

        // UserVisualDecision
        modelBuilder.Entity<UserVisualDecision>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ViewerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserVisualDecision>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.TargetUserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<UserVisualDecision>()
            .HasOne(x => x.PhotoEmbedding)
            .WithMany()
            .HasForeignKey(x => x.PhotoEmbeddingId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<UserVisualDecision>()
            .HasIndex(x => new { x.ViewerUserId, x.TargetUserId })
            .HasDatabaseName("ix_user_visual_decisions_viewer_target");

        modelBuilder.Entity<UserVisualDecision>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_visual_decision_choice",
                "\"choice\" IN ('YES','NO','PENDING')"));

        // UserVisualPreference
        modelBuilder.Entity<UserVisualPreference>()
            .HasOne(x => x.User)
            .WithOne()
            .HasForeignKey<UserVisualPreference>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserVisualPreference>()
            .Property(x => x.PreferenceEmbedding)
            .HasColumnType("vector(512)");

        modelBuilder.Entity<UserVisualPreference>()
            .Property(x => x.AversionEmbedding)
            .HasColumnType("vector(512)");

        // UserVoicePreference
        modelBuilder.Entity<UserVoicePreference>()
            .HasOne(x => x.User)
            .WithOne()
            .HasForeignKey<UserVoicePreference>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserVoicePreference>()
            .Property(x => x.PreferenceEmbedding)
            .HasColumnType("vector(192)");

        // UserMatchingWeight
        modelBuilder.Entity<UserMatchingWeight>()
            .HasKey(x => new { x.UserId, x.Component });

        modelBuilder.Entity<UserMatchingWeight>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserMatchingWeight>()
            .HasIndex(x => x.UserId)
            .HasDatabaseName("ix_user_matching_weights_user_id");

        // ===============================
        // PHASE 3E: SECURITY + ENCRYPTION
        // ===============================

        // SecurityAuditLog
        modelBuilder.Entity<SecurityAuditLog>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<SecurityAuditLog>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ActorId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<SecurityAuditLog>()
            .HasIndex(x => new { x.UserId, x.CreatedAt })
            .HasDatabaseName("ix_security_audit_log_user_id")
            .HasFilter("\"user_id\" IS NOT NULL");

        modelBuilder.Entity<SecurityAuditLog>()
            .HasIndex(x => new { x.EventType, x.CreatedAt })
            .HasDatabaseName("ix_security_audit_log_event_type");

        modelBuilder.Entity<SecurityAuditLog>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_audit_event_type",
                "\"event_type\" IN ('external_api_call','pii_access','encryption_key_rotation'," +
                "'admin_data_access','bulk_data_export','suspicious_pattern','failed_decryption')"));

        // ===============================
        // PHASE 4C: INSIGHTS + OPINIONS
        // ===============================

        modelBuilder.Entity<UserInsight>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .HasConstraintName("fk_user_insights_user")
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserInsight>()
            .Property(x => x.InsightsJson)
            .HasDefaultValue("[]");

        modelBuilder.Entity<UserInsight>()
            .Property(x => x.ComputedAt)
            .HasDefaultValueSql("NOW()");

        // ===============================
        // PHASE 4D: PRE-DATE BRIDGE
        // ===============================

        modelBuilder.Entity<ChatAvailabilitySignal>()
            .HasOne<ChatThread>()
            .WithMany()
            .HasForeignKey(x => x.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ChatAvailabilitySignal>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ChatAvailabilitySignal>()
            .HasIndex(x => x.ThreadId)
            .HasDatabaseName("ix_chat_avail_thread");

        modelBuilder.Entity<DateFeedback>()
            .HasOne<Match>()
            .WithMany()
            .HasForeignKey(x => x.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DateFeedback>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DateFeedback>()
            .HasIndex(x => new { x.MatchId, x.UserId })
            .HasDatabaseName("uq_date_feedback_match_user")
            .IsUnique();

        modelBuilder.Entity<DateFeedbackPrompt>()
            .HasOne<Match>()
            .WithMany()
            .HasForeignKey(x => x.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DateFeedbackPrompt>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DateFeedbackPrompt>()
            .HasIndex(x => new { x.MatchId, x.UserId })
            .HasDatabaseName("uq_date_fb_prompt_match_user")
            .IsUnique();

        modelBuilder.Entity<DateFeedbackPrompt>()
            .HasIndex(x => new { x.ScheduledFor, x.SentAt })
            .HasDatabaseName("ix_date_fb_prompts_scheduled");

        // ===============================
        // PHASE 4E: CATFISH DETECTION
        // ===============================

        modelBuilder.Entity<ReferencePhotoEmbedding>()
            .Property(x => x.Embedding)
            .HasColumnType("vector(512)");

        // ===============================
        // PHASE 5A: IDENTITY VERIFICATION
        // ===============================

        modelBuilder.Entity<User>()
            .Property(x => x.IsVerified)
            .HasDefaultValue(false);

        modelBuilder.Entity<UserVerification>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserVerification>()
            .HasIndex(x => new { x.UserId, x.Status })
            .HasDatabaseName("ix_user_verifications_user_status");

        modelBuilder.Entity<UserPreference>()
            .Property(x => x.ReduceMotion)
            .HasDefaultValue(false);

        modelBuilder.Entity<UserPreference>()
            .Property(x => x.HighContrast)
            .HasDefaultValue(false);

        // ===============================
        // PHASE 5C: ANALYTICS ENGINE
        // ===============================

        modelBuilder.Entity<AnalyticsEvent>()
            .HasIndex(x => x.UserIdHash)
            .HasDatabaseName("ix_analytics_events_user_id_hash")
            .HasFilter("\"user_id_hash\" IS NOT NULL");

        modelBuilder.Entity<AnalyticsEvent>()
            .HasIndex(x => x.EventType)
            .HasDatabaseName("ix_analytics_events_event_type");

        modelBuilder.Entity<AnalyticsEvent>()
            .HasIndex(x => x.CreatedAt)
            .HasDatabaseName("ix_analytics_events_created_at");

        modelBuilder.Entity<AbAssignment>()
            .HasOne<AbExperiment>()
            .WithMany()
            .HasForeignKey(x => x.ExperimentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AbAssignment>()
            .HasIndex(x => new { x.UserId, x.ExperimentId })
            .HasDatabaseName("uq_ab_assignments_user_experiment")
            .IsUnique();

        modelBuilder.Entity<AbAssignment>()
            .HasIndex(x => x.UserId)
            .HasDatabaseName("ix_ab_assignments_user_id");

        modelBuilder.Entity<AbConversion>()
            .HasOne<AbExperiment>()
            .WithMany()
            .HasForeignKey(x => x.ExperimentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AbConversion>()
            .HasIndex(x => new { x.UserId, x.ExperimentId })
            .HasDatabaseName("ix_ab_conversions_user_experiment");

        // Column encryption via AES-256-GCM value converters.
        // Vector-type columns (pgvector) use Pgvector's own converter — cannot stack a second.
        // ChatMessages.Body skipped — existing CHECK constraint caps stored length at 1000 chars;
        //   encrypted ciphertext exceeds that. Needs a separate migration to widen/drop constraint.
        if (_enc != null)
        {
            var strConv = new EncryptedStringConverter(_enc);

            // CS8620: nullable annotation mismatch between converter and EF property — runtime behaviour is correct.
#pragma warning disable CS8620
            modelBuilder.Entity<User>()
                .Property(x => x.Email)
                .HasConversion(strConv);

            modelBuilder.Entity<User>()
                .Property(x => x.FullName)
                .HasConversion(strConv);

            modelBuilder.Entity<UserProfile>()
                .Property(x => x.City)
                .HasConversion(strConv);

            modelBuilder.Entity<UserProfile>()
                .Property(x => x.State)
                .HasConversion(strConv);

            modelBuilder.Entity<UserOptionalField>()
                .Property(x => x.Value)
                .HasConversion(strConv);

            modelBuilder.Entity<UserIntent>()
                .Property(x => x.ReflectionSentence)
                .HasConversion(strConv);
#pragma warning restore CS8620
        }
    }
}
