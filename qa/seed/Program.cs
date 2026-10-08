using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Pgvector.Npgsql;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services.Security;
using WovenBackend.Services.Moments;

if (args.Length == 1 && args[0] == "--verify-rules")
{
    if (MomentsRules.BalloonLifetime != TimeSpan.FromHours(72)) throw new Exception("Founder-approved balloon duration must be 72h");
    foreach (var created in new[] { DateTimeOffset.Parse("2026-10-08T00:00:00Z"), DateTimeOffset.Parse("2026-12-31T23:59:59Z"), DateTimeOffset.Parse("2028-02-28T23:59:59Z") })
        if (MomentsRules.ComputeExpiresAt(created) - created != TimeSpan.FromHours(72)) throw new Exception("UTC expiry boundary mismatch");
    if (MomentsRules.NormalizePair(9, 1) != (1, 9)) throw new Exception("Match pair normalization failed");
    Console.WriteLine("{\"result\":\"passed\",\"balloonHours\":72,\"utcBoundaries\":3,\"scope\":\"canonical rule only; persisted match flows need separate verification\"}");
    return;
}

// This is a standalone QA importer, not an application endpoint. No hosted
// workers, AI clients, storage clients or notification services are constructed.
var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
var connection = config.GetConnectionString("DefaultConnection") ?? throw new Exception("QA connection required");
var parsed = new NpgsqlConnectionStringBuilder(connection);
if (parsed.Database is not ("woven_qa" or "woven_qa_model") || parsed.Host != "postgres")
    throw new Exception("Refusing import outside the isolated postgres/woven_qa target");
var dsBuilder = new NpgsqlDataSourceBuilder(connection);
dsBuilder.UseVector();
await using var ds = dsBuilder.Build();
var options = new DbContextOptionsBuilder<WovenDbContext>().UseNpgsql(ds, o => o.UseVector()).Options;
await using var db = new WovenDbContext(options, new EncryptionService(config));
if (args.Length == 1 && args[0] == "--prepare-model-schema")
{
    if (parsed.Database != "woven_qa_model") throw new Exception("Model-schema preparation is restricted to woven_qa_model");
    var created = await db.Database.EnsureCreatedAsync();
    Console.WriteLine(JsonSerializer.Serialize(new { database = parsed.Database, created, modelTables = db.Model.GetRelationalModel().Tables.Count(), warning = "Model-created QA schema; migrations and upgrades are NOT verified" }));
    return;
}
if (await db.Users.AnyAsync(u => !u.Email.EndsWith("@woven.invalid")))
    throw new Exception("Non-fixture users exist; refusing to mix data");
using var document = JsonDocument.Parse(await File.ReadAllTextAsync(args[0]));
var personas = document.RootElement.GetProperty("personas").EnumerateArray().ToArray();
if (personas.Length != 100 || personas.Any(p => !p.GetProperty("synthetic").GetBoolean() || p.GetProperty("age").GetInt32() < 18))
    throw new Exception("Expected exactly 100 synthetic adult personas");
var pillarNames = new[] { "Lifestyle", "Energy", "Values", "Communication", "Ambition", "Stability", "Curiosity", "Affection" };
var questionIds = new[] { "QA_VALUES", "QA_LIFESTYLE", "QA_COMMUNICATION", "QA_CURIOSITY", "QA_AFFECTION" };
var pillarType = db.Model.FindEntityType(typeof(UserVector))!.FindProperty(nameof(UserVector.PillarEmbedding))!.GetColumnType()!;
var pillarDimension = int.Parse(pillarType[(pillarType.IndexOf('(') + 1)..pillarType.IndexOf(')')]);
var inserted = 0;
var accounts = new List<object>();
await using var tx = await db.Database.BeginTransactionAsync();
for (var i = 0; i < personas.Length; i++)
{
    var p = personas[i]; var email = p.GetProperty("email").GetString()!;
    var existing = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
    if (existing != null) { accounts.Add(new { personaId = p.GetProperty("id").GetString(), userId = existing.Id, email }); continue; }
    var target = p.GetProperty("targetState").GetString();
    var user = new User { Email = email, FullName = p.GetProperty("displayName").GetString(), ProfileStatus = target is "new" or "incomplete" ? ProfileStatus.INCOMPLETE : ProfileStatus.COMPLETE, TrustScore = target == "safety-review" ? 0.1f : 0.5f, LastActiveAt = DateTimeOffset.UtcNow.AddDays(target == "inactive" ? -100 : 0) };
    db.Users.Add(user); await db.SaveChangesAsync();
    var location = p.GetProperty("location"); var preference = p.GetProperty("preferences");
    db.UserProfiles.Add(new UserProfile { UserId = user.Id, Age = p.GetProperty("age").GetInt32(), Gender = p.GetProperty("gender").GetString()!, City = location.GetProperty("city").GetString()!, State = "QA", Lat = location.GetProperty("lat").GetDouble(), Lng = location.GetProperty("lng").GetDouble() });
    db.UserPreferences.Add(new UserPreference { UserId = user.Id, AgeMin = preference.GetProperty("ageMin").GetInt32(), AgeMax = preference.GetProperty("ageMax").GetInt32(), DistanceMiles = preference.GetProperty("distanceMiles").GetInt32(), InterestedInJson = p.GetProperty("interestedIn").GetRawText(), RelationshipStructure = RelationshipStructure.OPEN });
    db.UserIntents.Add(new UserIntent { UserId = user.Id, PrimaryIntent = p.GetProperty("intent").GetString()!, ReflectionSentence = "Synthetic QA fixture: consistency, care and communication.", OpennessJson = "[]" });
    for (var photo = 0; photo < 3; photo++) db.UserPhotos.Add(new UserPhoto { UserId = user.Id, Url = $"/qa-avatar.svg?persona={i + 1}&slot={photo}", Caption = "Synthetic QA avatar", SortOrder = photo });
    var answer = p.GetProperty("foundationalAnswers").GetProperty("values").GetString();
    db.UserFoundationalQuestionSets.Add(new UserFoundationalQuestionSet { UserId = user.Id, Version = 1, QuestionsJson = JsonSerializer.Serialize(questionIds.Select(id => new { id, text = "Synthetic QA foundational question" })), AnswersJson = JsonSerializer.Serialize(questionIds.Select(id => new { id, a = answer })), QuestionsSource = "base", GenerationMetaJson = "{\"source\":\"synthetic_qa_fixture\"}", AnsweredAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(15) });
    var scores = pillarNames.Select((_, index) => 0.2f + ((i * 17 + index * 11) % 70) / 100f).ToArray();
    var pillars = pillarNames.Select((name, index) => new { name, value = scores[index] }).ToDictionary(x => x.name, x => x.value);
    var mockEmbedding = Enumerable.Range(0, pillarDimension).Select(index => scores[index % scores.Length]).ToArray();
    db.UserVectors.Add(new UserVector { UserId = user.Id, Version = 1, PillarScoresJson = JsonSerializer.Serialize(pillars), PillarEmbedding = new Vector(mockEmbedding), VectorJson = JsonSerializer.Serialize(new { intent = new { seriousness = 0.7, flexibility = 0.5, commitmentReadiness = 0.6, tags = new[] { "qa_mock" } }, foundational = new { pillars, tags = new Dictionary<string, string[]>() }, lifestyle = new { }, pulse = new { battery = 0.5, tone = 0.5, role = 0.5 } }) });
    // First two accounts exercise actual lazy wallet creation. Others carry
    // deterministic balances for zero-funds/cap/refund scenarios.
    if (i >= 2) db.SparkWallets.Add(new SparkWallet { UserId = user.Id, BalanceTenths = p.GetProperty("sparkBalance").GetInt32() * 10, LastEarnedDate = DateOnly.FromDateTime(DateTime.UtcNow) });
    await db.SaveChangesAsync(); inserted++;
    accounts.Add(new { personaId = p.GetProperty("id").GetString(), userId = user.Id, email });
}
await tx.CommitAsync();
Console.WriteLine(JsonSerializer.Serialize(new { database = parsed.Database, inserted, accounts, note = "Synthetic fixture vectors, not model-generated semantic evidence. Import bypasses onboarding UI; test that flow separately." }));
