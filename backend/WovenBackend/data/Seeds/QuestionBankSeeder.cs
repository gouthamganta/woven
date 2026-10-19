using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WovenBackend.Data.Entities;

namespace WovenBackend.Data.Seeds;

/// <summary>
/// Seeds validated personality/preference questions into the foundational_questions table.
/// Questions sourced from: IPIP-NEO (public domain), Schwartz Values, Love Languages.
/// </summary>
public static class QuestionBankSeeder
{
    public static async Task SeedQuestionsAsync(WovenDbContext db)
    {
        if (await db.FoundationalQuestions.AnyAsync())
        {
            return; // Already seeded
        }

        var questions = new List<FoundationalQuestion>
        {
            // ========== ENERGY (Extraversion) - 4 questions ==========

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Energy",
                GameType = "slider",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""After a long week, where do you get your energy?"",
                    ""leftLabel"": ""Quiet night alone"",
                    ""rightLabel"": ""Going out with friends""
                }"),
                SourceInstrument = "IPIP-NEO",
                Citation = "Goldberg, L. R. (1992). The development of markers for the Big-Five factor structure. Psychological Assessment, 4(1), 26.",
                OriginalItem = "I am the life of the party",
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Energy",
                GameType = "slider",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""In group settings, you usually..."",
                    ""leftLabel"": ""Listen more than talk"",
                    ""rightLabel"": ""Lead conversations""
                }"),
                SourceInstrument = "IPIP-NEO",
                Citation = "Goldberg, L. R. (1992). Psychological Assessment, 4(1), 26.",
                OriginalItem = "I don't talk a lot",
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Energy",
                GameType = "swipe",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""Rate how energizing these social activities are for you"",
                    ""options"": [
                        {""text"": ""Large party with new people""},
                        {""text"": ""Small dinner with close friends""},
                        {""text"": ""One-on-one deep conversation""},
                        {""text"": ""Solo activity (reading, hobbies)""},
                        {""text"": ""Group sports or team events""}
                    ]
                }"),
                SourceInstrument = "IPIP-NEO",
                Citation = "Goldberg, L. R. (1992). Psychological Assessment, 4(1), 26.",
                OriginalItem = "I feel comfortable around people",
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Energy",
                GameType = "selection",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""Which social settings feel most natural to you?"",
                    ""subtitle"": ""Pick 2"",
                    ""maxSelect"": 2,
                    ""options"": [
                        {""label"": ""Solo activities"", ""value"": ""solo""},
                        {""label"": ""One-on-one hangouts"", ""value"": ""duo""},
                        {""label"": ""Small groups (3-5)"", ""value"": ""small""},
                        {""label"": ""Big social gatherings"", ""value"": ""large""}
                    ]
                }"),
                SourceInstrument = "IPIP-NEO",
                Citation = "Goldberg, L. R. (1992). Psychological Assessment, 4(1), 26.",
                OriginalItem = "I make friends easily",
                IsActive = true,
                Weight = 100
            },

            // ========== STABILITY (Neuroticism reversed) - 3 questions ==========

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Stability",
                GameType = "slider",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""When making weekend plans..."",
                    ""leftLabel"": ""Whatever happens, happens"",
                    ""rightLabel"": ""Color-coded calendar""
                }"),
                SourceInstrument = "IPIP-NEO",
                Citation = "Goldberg, L. R. (1992). Psychological Assessment, 4(1), 26.",
                OriginalItem = "I make plans and stick to them",
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Stability",
                GameType = "slider",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""How do you typically handle unexpected changes?"",
                    ""leftLabel"": ""Stress out, need time to adjust"",
                    ""rightLabel"": ""Roll with it, no problem""
                }"),
                SourceInstrument = "IPIP-NEO",
                Citation = "Goldberg, L. R. (1992). Psychological Assessment, 4(1), 26.",
                OriginalItem = "I am relaxed most of the time",
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Stability",
                GameType = "swipe",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""How often do these describe you?"",
                    ""options"": [
                        {""text"": ""I keep my space organized""},
                        {""text"": ""I stick to routines that work""},
                        {""text"": ""Last-minute changes excite me""},
                        {""text"": ""I'm comfortable with uncertainty""},
                        {""text"": ""I plan things way in advance""}
                    ]
                }"),
                SourceInstrument = "IPIP-NEO",
                Citation = "Goldberg, L. R. (1992). Psychological Assessment, 4(1), 26.",
                OriginalItem = "I like order",
                IsActive = true,
                Weight = 100
            },

            // ========== AMBITION (Conscientiousness) - 4 questions ==========

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Ambition",
                GameType = "ranking",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""Rank what drives you most in life"",
                    ""items"": [
                        ""Career success"",
                        ""Meaningful relationships"",
                        ""Personal growth"",
                        ""Health & fitness"",
                        ""Financial freedom""
                    ]
                }"),
                SourceInstrument = "IPIP-NEO",
                Citation = "Goldberg, L. R. (1992). Psychological Assessment, 4(1), 26.",
                OriginalItem = "I work hard",
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Ambition",
                GameType = "slider",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""When it comes to goals..."",
                    ""leftLabel"": ""Take life as it comes"",
                    ""rightLabel"": ""Always striving for more""
                }"),
                SourceInstrument = "IPIP-NEO",
                Citation = "Goldberg, L. R. (1992). Psychological Assessment, 4(1), 26.",
                OriginalItem = "I am always prepared",
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Ambition",
                GameType = "swipe",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""How much do you relate to these statements?"",
                    ""options"": [
                        {""text"": ""I finish what I start""},
                        {""text"": ""I set ambitious goals""},
                        {""text"": ""Work-life balance > climbing the ladder""},
                        {""text"": ""I'm competitive with myself""},
                        {""text"": ""Success means different things to everyone""}
                    ]
                }"),
                SourceInstrument = "IPIP-NEO",
                Citation = "Goldberg, L. R. (1992). Psychological Assessment, 4(1), 26.",
                OriginalItem = "I strive for excellence in what I do",
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Ambition",
                GameType = "ranking",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""Rank these in order of importance to you"",
                    ""items"": [
                        ""Impact & legacy"",
                        ""Work-life harmony"",
                        ""Creative expression"",
                        ""Stability & security"",
                        ""Adventure & novelty""
                    ]
                }"),
                SourceInstrument = "Custom",
                Citation = "Adapted from goal orientation research",
                OriginalItem = null,
                IsActive = true,
                Weight = 100
            },

            // ========== CURIOSITY (Openness) - 3 questions ==========

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Curiosity",
                GameType = "swipe",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""How much do these things fascinate you?"",
                    ""options"": [
                        {""text"": ""How your phone actually works""},
                        {""text"": ""Street food in other countries""},
                        {""text"": ""Why some songs make you cry""},
                        {""text"": ""What strangers are thinking about""},
                        {""text"": ""Learning random skills (juggling, origami)""}
                    ]
                }"),
                SourceInstrument = "IPIP-NEO",
                Citation = "Goldberg, L. R. (1992). Psychological Assessment, 4(1), 26.",
                OriginalItem = "I have a vivid imagination",
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Curiosity",
                GameType = "slider",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""When learning something new..."",
                    ""leftLabel"": ""Stick to what works"",
                    ""rightLabel"": ""Try everything once""
                }"),
                SourceInstrument = "IPIP-NEO",
                Citation = "Goldberg, L. R. (1992). Psychological Assessment, 4(1), 26.",
                OriginalItem = "I seek adventure",
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Curiosity",
                GameType = "swipe",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""Rate your interest in these topics"",
                    ""options"": [
                        {""text"": ""Abstract art & symbolism""},
                        {""text"": ""How cultures evolve over time""},
                        {""text"": ""Philosophical questions (meaning, reality)""},
                        {""text"": ""Practical skills (cooking, fixing things)""},
                        {""text"": ""Obscure historical facts""}
                    ]
                }"),
                SourceInstrument = "IPIP-NEO",
                Citation = "Goldberg, L. R. (1992). Psychological Assessment, 4(1), 26.",
                OriginalItem = "I am interested in abstract ideas",
                IsActive = true,
                Weight = 100
            },

            // ========== COMMUNICATION - 3 questions ==========

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Communication",
                GameType = "swipe",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""Rate how much these conversation topics excite you"",
                    ""options"": [
                        {""text"": ""Why pineapple on pizza is actually good""},
                        {""text"": ""Dream startup ideas (realistic or wild)""},
                        {""text"": ""Embarrassing childhood stories""},
                        {""text"": ""The meaning of life (yes, really)""},
                        {""text"": ""Your secret talent nobody knows about""}
                    ]
                }"),
                SourceInstrument = "Custom",
                Citation = "Adapted from attachment theory & communication styles research",
                OriginalItem = null,
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Communication",
                GameType = "slider",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""When sharing feelings..."",
                    ""leftLabel"": ""Keep it to myself"",
                    ""rightLabel"": ""Open book, overshare""
                }"),
                SourceInstrument = "Custom",
                Citation = "Adapted from emotional expressiveness research",
                OriginalItem = null,
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Communication",
                GameType = "selection",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""Your ideal conversation style involves..."",
                    ""subtitle"": ""Pick 2"",
                    ""maxSelect"": 2,
                    ""options"": [
                        {""label"": ""Deep vulnerable talks"", ""value"": ""deep""},
                        {""label"": ""Witty banter & jokes"", ""value"": ""humor""},
                        {""label"": ""Sharing practical advice"", ""value"": ""practical""},
                        {""label"": ""Debating ideas"", ""value"": ""debate""}
                    ]
                }"),
                SourceInstrument = "Custom",
                Citation = "Adapted from communication styles research",
                OriginalItem = null,
                IsActive = true,
                Weight = 100
            },

            // ========== LIFESTYLE - 3 questions ==========

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Lifestyle",
                GameType = "selection",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""Your perfect Saturday includes..."",
                    ""subtitle"": ""Pick 3"",
                    ""maxSelect"": 3,
                    ""options"": [
                        {""label"": ""Hiking or sports"", ""value"": ""outdoors""},
                        {""label"": ""Art, music, or DIY"", ""value"": ""creative""},
                        {""label"": ""Learning a new skill"", ""value"": ""learning""},
                        {""label"": ""Brunch with friends"", ""value"": ""social""},
                        {""label"": ""Sleeping in"", ""value"": ""rest""},
                        {""label"": ""Home movie marathon"", ""value"": ""home""}
                    ]
                }"),
                SourceInstrument = "Custom",
                Citation = "Lifestyle compatibility research",
                OriginalItem = null,
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Lifestyle",
                GameType = "slider",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""Your typical energy level is..."",
                    ""leftLabel"": ""Low-key homebody"",
                    ""rightLabel"": ""Always on the move""
                }"),
                SourceInstrument = "Custom",
                Citation = "Lifestyle compatibility research",
                OriginalItem = null,
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Lifestyle",
                GameType = "selection",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""What does 'adventure' mean to you?"",
                    ""subtitle"": ""Pick 2"",
                    ""maxSelect"": 2,
                    ""options"": [
                        {""label"": ""Trying exotic food"", ""value"": ""food""},
                        {""label"": ""Traveling to new places"", ""value"": ""travel""},
                        {""label"": ""Learning a creative skill"", ""value"": ""skill""},
                        {""label"": ""Spontaneous road trips"", ""value"": ""spontaneous""},
                        {""label"": ""Indoor cozy experiences"", ""value"": ""cozy""}
                    ]
                }"),
                SourceInstrument = "Custom",
                Citation = "Lifestyle compatibility research",
                OriginalItem = null,
                IsActive = true,
                Weight = 100
            },

            // ========== VALUES - 4 questions ==========

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Values",
                GameType = "ranking",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""Rank these values in order of importance to you"",
                    ""items"": [
                        ""Honesty"",
                        ""Adventure"",
                        ""Security"",
                        ""Growth"",
                        ""Deep connections""
                    ]
                }"),
                SourceInstrument = "Schwartz Values Survey",
                Citation = "Schwartz, S. H. (1992). Universals in the content and structure of values. Advances in Experimental Social Psychology, 25, 1-65.",
                OriginalItem = "Portrait Values Questionnaire items",
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Values",
                GameType = "ranking",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""Rank what you value most in a relationship"",
                    ""items"": [
                        ""Trust & loyalty"",
                        ""Shared adventures"",
                        ""Emotional depth"",
                        ""Mutual growth"",
                        ""Fun & laughter""
                    ]
                }"),
                SourceInstrument = "Schwartz Values Survey",
                Citation = "Schwartz, S. H. (1992). Advances in Experimental Social Psychology, 25, 1-65.",
                OriginalItem = "Adapted from PVQ items",
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Values",
                GameType = "swipe",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""How important are these to you?"",
                    ""options"": [
                        {""text"": ""Making a difference in the world""},
                        {""text"": ""Financial independence""},
                        {""text"": ""Close-knit family/friend circle""},
                        {""text"": ""Personal freedom & autonomy""},
                        {""text"": ""Being part of something bigger""}
                    ]
                }"),
                SourceInstrument = "Schwartz Values Survey",
                Citation = "Schwartz, S. H. (1992). Advances in Experimental Social Psychology, 25, 1-65.",
                OriginalItem = "Self-direction, benevolence, universalism items",
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Values",
                GameType = "slider",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""In relationships, you prioritize..."",
                    ""leftLabel"": ""Independence & space"",
                    ""rightLabel"": ""Togetherness & unity""
                }"),
                SourceInstrument = "Custom",
                Citation = "Relationship values research",
                OriginalItem = null,
                IsActive = true,
                Weight = 100
            },

            // ========== AFFECTION - 3 questions ==========

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Affection",
                GameType = "selection",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""How do you show someone you care?"",
                    ""subtitle"": ""Pick 2"",
                    ""maxSelect"": 2,
                    ""options"": [
                        {""label"": ""Sweet texts & compliments"", ""value"": ""words""},
                        {""label"": ""Quality time together"", ""value"": ""time""},
                        {""label"": ""Thoughtful gifts"", ""value"": ""gifts""},
                        {""label"": ""Acts of service (helping out)"", ""value"": ""acts""},
                        {""label"": ""Physical touch & closeness"", ""value"": ""touch""}
                    ]
                }"),
                SourceInstrument = "Love Languages",
                Citation = "Chapman, G. (1992). The Five Love Languages",
                OriginalItem = "Love Languages framework",
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Affection",
                GameType = "selection",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""What makes you feel most loved?"",
                    ""subtitle"": ""Pick 2"",
                    ""maxSelect"": 2,
                    ""options"": [
                        {""label"": ""Hearing 'I love you' often"", ""value"": ""words""},
                        {""label"": ""Undivided attention"", ""value"": ""time""},
                        {""label"": ""Surprise gifts or gestures"", ""value"": ""gifts""},
                        {""label"": ""Someone helping with tasks"", ""value"": ""acts""},
                        {""label"": ""Hugs, cuddles, hand-holding"", ""value"": ""touch""}
                    ]
                }"),
                SourceInstrument = "Love Languages",
                Citation = "Chapman, G. (1992). The Five Love Languages",
                OriginalItem = "Love Languages framework",
                IsActive = true,
                Weight = 100
            },

            new() {
                Id = Guid.NewGuid(),
                Pillar = "Affection",
                GameType = "slider",
                QuestionData = JsonDocument.Parse(@"{
                    ""question"": ""When it comes to affection..."",
                    ""leftLabel"": ""Need my personal space"",
                    ""rightLabel"": ""Love being close often""
                }"),
                SourceInstrument = "Custom",
                Citation = "Attachment theory research",
                OriginalItem = null,
                IsActive = true,
                Weight = 100
            }
        };

        await db.FoundationalQuestions.AddRangeAsync(questions);
        await db.SaveChangesAsync();
    }
}
