namespace WovenBackend.Services;

/// <summary>
/// BankQuestion = canonical “instrument” question:
/// - Id is stable forever (answers map to this)
/// - Text is canonical base wording (OpenAI rewrites it)
/// - Pillars are internal signal tags (can be 1–2 per question)
/// </summary>
public record BankQuestion(string Id, string Text, string[] Pillars);

public static class FoundationalQuestionBank
{
    /// <summary>
    /// ✅ Source of truth: stable IDs + stable semantic meaning.
    /// ✅ For now: always the same 5 instruments.
    /// Later: you can rotate phrasing candidates or pillar coverage by version
    /// WITHOUT ever changing IDs.
    /// </summary>
    /// <summary>
    /// Canonical 8 pillars used for scoring:
    /// Lifestyle, Energy, Values, Communication, Ambition, Stability, Curiosity, Affection
    /// </summary>
    public static readonly string[] CanonicalPillars = new[]
    {
        "Lifestyle", "Energy", "Values", "Communication", "Ambition", "Stability", "Curiosity", "Affection"
    };

    public static BankQuestion[] GetBaseFiveForVersion(int version)
    {
        // 5 questions intentionally cover 8 canonical pillars via overlap.
        // IMPORTANT: Only use canonical pillar names to ensure proper scoring.
        return new[]
        {
            new BankQuestion(
                Id: "q1",
                Text: "When you have a free evening, what do you usually crave doing most?",
                Pillars: new[] { "Lifestyle", "Energy" }
            ),
            new BankQuestion(
                Id: "q2",
                Text: "What kind of connection makes you feel most comfortable with someone new?",
                Pillars: new[] { "Communication", "Affection" } // Fixed: was Connection, Attachment
            ),
            new BankQuestion(
                Id: "q3",
                Text: "What's a small habit or routine that genuinely makes your life better?",
                Pillars: new[] { "Lifestyle", "Stability" } // Fixed: was Habits, Stability
            ),
            new BankQuestion(
                Id: "q4",
                Text: "What's something you're proud of that doesn't show up on a resume?",
                Pillars: new[] { "Values", "Curiosity" } // Fixed: was Identity, SelfWorth
            ),
            new BankQuestion(
                Id: "q5",
                Text: "What does a good relationship feel like to you in everyday moments?",
                Pillars: new[] { "Affection", "Communication" } // Fixed: was Relationship, ConflictRepair
            )
        };
    }
}
