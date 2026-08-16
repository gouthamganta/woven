namespace WovenBackend.Data.Entities;

public class MatchSignalLog
{
    public long Id { get; set; }
    public int ViewerId { get; set; }
    public int CandidateId { get; set; }

    // One of the MatchSignalEventTypes constants
    public string EventType { get; set; } = "";

    // Numeric payload — interpretation depends on EventType:
    //   binary events (BalloonPop, TrialAccepted, etc.)   → 1.0
    //   TrialRejected                                      → 0.0
    //   MessageResponseLatencyMs / TimeToFirstMessageMs    → milliseconds (float)
    //   SelfDisclosureRatio                                → [0.0, 1.0]
    //   ProfileVisitDepth                                  → tile count viewed
    //   TileDwell / VoiceDwell                             → dwell ms
    //   ExplicitFeedback                                   → [0.0, 1.0] normalized from 1-5
    public float EventValue { get; set; }

    // Optional extra context (tile type, match stage, etc.)
    public string? MetadataJson { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class MatchSignalEventTypes
{
    public const string TileDwell              = "TileDwell";
    public const string VoiceDwell            = "VoiceDwell";
    public const string ProfileVisitDepth     = "ProfileVisitDepth";
    public const string BalloonPop            = "BalloonPop";
    public const string TrialRequested        = "TrialRequested";
    public const string TrialAccepted         = "TrialAccepted";
    public const string TrialRejected         = "TrialRejected";
    public const string MessageSent           = "MessageSent";
    public const string MessageResponseLatencyMs = "MessageResponseLatencyMs";
    public const string TimeToFirstMessageMs     = "TimeToFirstMessageMs";
    public const string SelfDisclosureRatio   = "SelfDisclosureRatio";
    public const string GameCompleted         = "GameCompleted";
    public const string DateIdeaAccepted      = "DateIdeaAccepted";
    public const string DateIdeaRejected      = "DateIdeaRejected";
    public const string ChatNoteLove          = "ChatNoteLove";
    public const string MessageLove           = "MessageLove";
    public const string ExplicitFeedback      = "ExplicitFeedback";

    // Game-derived disclosure and alignment signals
    public const string KnowMeDisclosureDepth = "KnowMeDisclosureDepth"; // completed_rounds / total_rounds
    public const string RedFlagGameDepth       = "RedFlagGameDepth";       // completed_rounds / total_rounds
    public const string FlagAgreementRate      = "FlagAgreementRate";      // per-statement flag agreement [0,1]

    // Trial end reason signals — viewer ended trial, candidate is the other person
    // EventValue = 1.0 for all; preference learning uses the type, not the value
    public const string TrialEndedNoSpark      = "TrialEndedNoSpark";
    public const string TrialEndedWrongTiming  = "TrialEndedWrongTiming";
    public const string TrialEndedNotMyType    = "TrialEndedNotMyType";

    // How many messages were exchanged during the trial window
    // EventValue = message count (float)
    public const string TrialMessageCount      = "TrialMessageCount";

    // Safety/trust signal — viewer flagged candidate after interaction
    // EventValue = 1.0; MetadataJson = { reason: "uncomfortable"|"inappropriate" }
    // Feeds trust score only — never ECHO compatibility scoring
    public const string UserFlagged            = "UserFlagged";

    // Voice note engagement signals
    public const string MutualVoiceExchange    = "MutualVoiceExchange";    // both sent voice notes
    public const string VoiceNoteListenComplete = "VoiceNoteListenComplete"; // listener played to end
}
