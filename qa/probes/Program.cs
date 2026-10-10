using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Services.Security;

var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
var connection = config.GetConnectionString("DefaultConnection") ?? throw new Exception("Local QA connection required");
var target = new NpgsqlConnectionStringBuilder(connection);
if (target.Host != "postgres" || target.Database != "woven_qa_model") throw new Exception("Refusing query outside isolated model-created QA database");
var options = new DbContextOptionsBuilder<WovenDbContext>().UseNpgsql(connection, postgres => postgres.UseVector()).Options;
await using var db = new WovenDbContext(options, new EncryptionService(config));
var existing = await db.Users.AsNoTracking().SingleAsync(user => user.Id == 100);
if (!existing.Email.EndsWith("@woven.invalid", StringComparison.Ordinal)) throw new Exception("Refusing non-synthetic fixture");
var email = existing.Email;
var match = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Email == email);
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
    scope = "Read-only real PostgreSQL equality lookup through production encryption converter; no provider login tested",
    expectedUserId = existing.Id, actualUserId = match?.Id,
    result = match?.Id == existing.Id ? "passed" : "failed",
    fixtureEmailVerifiedSynthetic = true,
    note = "No plaintext email, key, token or ciphertext printed; no rows changed"
}));
