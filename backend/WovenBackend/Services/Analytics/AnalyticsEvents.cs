namespace WovenBackend.Services.Analytics;

public static class AnalyticsEvents
{
    // User lifecycle
    public const string UserRegistered              = "user_registered";
    public const string OnboardingStepCompleted     = "onboarding_step_completed";
    public const string OnboardingAbandoned         = "onboarding_abandoned";
    public const string ProfileUpdated              = "profile_updated";
    public const string AccountDeleted              = "account_deleted";

    // Discovery
    public const string MomentsDeckViewed           = "moments_deck_viewed";
    public const string MomentResponded             = "moment_responded";
    public const string PendingSaved                = "pending_saved";
    public const string PendingConverted            = "pending_converted";
    public const string CommonsSessionStarted       = "commons_session_started";
    public const string TileViewed                  = "tile_viewed";
    public const string TileOrbited                 = "tile_orbited";
    public const string TileEngaged                 = "tile_engaged";
    public const string TilePosted                  = "tile_posted";

    // Matching
    public const string MatchCreated                = "match_created";
    public const string MatchExpired                = "match_expired";
    public const string MatchUnmatched              = "match_unmatched";
    public const string BalloonTimerStarted         = "balloon_timer_started";
    public const string FindLoveUnlocked            = "find_love_unlocked";

    // Conversation
    public const string ChatStarted                 = "chat_started";
    public const string MessageSent                 = "message_sent";
    public const string GameInvited                 = "game_invited";
    public const string GameAccepted                = "game_accepted";
    public const string GameRejected                = "game_rejected";
    public const string GameCompleted               = "game_completed";
    public const string NudgeShown                  = "nudge_shown";
    public const string NudgeActedOn                = "nudge_acted_on";
    public const string NudgeDismissed              = "nudge_dismissed";

    // Dating
    public const string DateInterestExpressed       = "date_interest_expressed";
    public const string DateInterestMutual          = "date_interest_mutual";
    public const string VenueSuggestionsViewed      = "venue_suggestions_viewed";
    public const string AvailabilitySignalSent      = "availability_signal_sent";
    public const string DateFeedbackPrompted        = "date_feedback_prompted";
    public const string DateFeedbackSubmitted       = "date_feedback_submitted";

    // Trust + Safety
    public const string VerificationStarted        = "verification_started";
    public const string VerificationCompleted      = "verification_completed";
    public const string VerificationFailed         = "verification_failed";
    public const string CatfishFlagTriggered       = "catfish_flag_triggered";
    public const string ContentModerated           = "content_moderated";
    public const string ReportSubmitted            = "report_submitted";
    public const string AccountBlocked             = "account_blocked";

    // Seasons + Vectors
    public const string SeasonResponseSubmitted     = "season_response_submitted";
    public const string WeeklyPulseSubmitted        = "weekly_pulse_submitted";
    public const string InsightViewed               = "insight_viewed";
    public const string OpinionSubmitted            = "opinion_submitted";
    public const string WeightLearningRun           = "weight_learning_run";

    // Engagement
    public const string AppOpened                  = "app_opened";
    public const string NotificationReceived       = "notification_received";
    public const string NotificationTapped         = "notification_tapped";
    public const string NotificationDismissed      = "notification_dismissed";
    public const string WeeklyDigestViewed         = "weekly_digest_viewed";
    public const string SessionEnded               = "session_ended";

    // A/B
    public const string AbExperimentAssigned       = "ab_experiment_assigned";
    public const string AbConversion               = "ab_conversion";
}
