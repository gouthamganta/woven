namespace WovenBackend.Services;

public record DynamicBankOption(string Key, string Label, string SubLabel);
public record DynamicBankQuestion(string Id, string Text, DynamicBankOption[] Options);

public static class DynamicQuestionBank
{
    public static readonly string[] QuestionIds = { "d1_battery", "d2_tone", "d3_role" };

    public static DynamicBankQuestion[] GetBaseThree()
    {
        return new[]
        {
            new DynamicBankQuestion(
                Id: "d1_battery",
                Text: "What’s your social energy level right now?",
                Options: new[]
                {
                    new DynamicBankOption("high", "High", "Ready to perform, charm, and go out."),
                    new DynamicBankOption("medium", "Medium", "Down for a good conversation, but keep it chill."),
                    new DynamicBankOption("low", "Low", "Just want to observe and exchange a few messages.")
                }
            ),
            new DynamicBankQuestion(
                Id: "d2_tone",
                Text: "Which frequency are you tuned into today?",
                Options: new[]
                {
                    new DynamicBankOption("playful", "Playful", "Banter, memes, and lighthearted roasting."),
                    new DynamicBankOption("serious", "Serious", "Deep thoughts and real talk."),
                    new DynamicBankOption("calm", "Calm", "Soft, easy, and stress-free interaction.")
                }
            ),
            new DynamicBankQuestion(
                Id: "d3_role",
                Text: "How do you want to show up in a match today?",
                Options: new[]
                {
                    new DynamicBankOption("driver", "The Driver", "I’m ready to lead the chat and make plans."),
                    new DynamicBankOption("copilot", "The Co-Pilot", "I want to go back and forth with someone."),
                    new DynamicBankOption("passenger", "The Passenger", "I’m busy/tired; I’d love for someone else to take the lead.")
                }
            )
        };
    }

    public static HashSet<string> KeysFor(string questionId)
    {
        return questionId switch
        {
            "d1_battery" => new HashSet<string>(new[] { "high", "medium", "low" }),
            "d2_tone"    => new HashSet<string>(new[] { "playful", "serious", "calm" }),
            "d3_role"    => new HashSet<string>(new[] { "driver", "copilot", "passenger" }),
            _ => new HashSet<string>()
        };
    }
}
