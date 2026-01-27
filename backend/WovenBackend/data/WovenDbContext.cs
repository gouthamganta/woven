using Microsoft.EntityFrameworkCore;
using WovenBackend.Data.Entities;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Data.Entities.Games;

namespace WovenBackend.Data;

public class WovenDbContext : DbContext
{
    public WovenDbContext(DbContextOptions<WovenDbContext> options) : base(options) { }

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
    }
}
