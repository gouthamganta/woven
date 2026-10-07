# Database Migrations

Guide to creating and applying EF Core migrations in Woven.

---

## Overview

Woven uses **Entity Framework Core 10** for database schema management. All schema changes go through EF migrations.

---

## Prerequisites

**Install EF Core tools:**
```bash
dotnet tool install --global dotnet-ef
```

**Verify:**
```bash
dotnet ef --version
# Should show 10.x.x
```

---

## Creating a New Migration

### 1. Make Entity Changes

Edit entity classes in `backend/WovenBackend/Data/Entities/`:

**Example:**
```csharp
// Data/Entities/Match.cs
public class Match
{
    public Guid Id { get; set; }
    public int UserAId { get; set; }
    public int UserBId { get; set; }
    
    // New property
    public string? MeetupLocation { get; set; }  // ← Added
}
```

---

### 2. Update DbContext (if needed)

If adding relationships or configurations:

```csharp
// Data/WovenDbContext.cs
protected override void OnModelCreating(ModelBuilder builder)
{
    builder.Entity<Match>()
        .Property(m => m.MeetupLocation)
        .HasColumnName("meetup_location")
        .HasMaxLength(500);
}
```

**Column naming convention:** snake_case via `.HasColumnName()`

---

### 3. Generate Migration

```bash
cd backend/WovenBackend
dotnet ef migrations add AddMeetupLocationToMatch
```

**File naming pattern:** `yyyyMMddHHmmss_DescriptiveName.cs`

**Generated files:**
- `Migrations/20260525123456_AddMeetupLocationToMatch.cs` — Migration
- `Migrations/WovenDbContextModelSnapshot.cs` — Updated snapshot

---

### 4. Review Migration

**Check generated SQL:**
```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>(
        name: "meetup_location",
        table: "matches",
        type: "character varying(500)",
        maxLength: 500,
        nullable: true);
}
```

**Verify:**
- Column names are snake_case
- Types match entity properties
- Nullable/required matches entity
- Constraints are correct

---

## Applying Migrations

### Local Development

```bash
cd backend/WovenBackend
dotnet ef database update
```

**What this does:**
1. Connects to Postgres (localhost:5433)
2. Checks `__EFMigrationsHistory` table
3. Applies all pending migrations
4. Records applied migrations in history table

---

### Production

Migrations are applied **automatically during deployment** via Container App startup command.

**See [deployment.md](deployment.md) for details.**

---

## pgvector Columns

**pgvector extension is only available in Docker Postgres.**

EF Core cannot generate pgvector columns automatically. Manual steps required:

### 1. Generate Migration

```bash
dotnet ef migrations add AddEmbeddingVectors
```

---

### 2. Edit Migration File

Add raw SQL for vector column:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // EF Core cannot handle pgvector, so we use raw SQL
    migrationBuilder.Sql(@"
        ALTER TABLE user_profiles
        ADD COLUMN embedding_vector vector(1536);
        
        CREATE INDEX idx_profile_embedding
        ON user_profiles
        USING ivfflat (embedding_vector vector_cosine_ops)
        WITH (lists = 100);
    ");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(@"
        DROP INDEX IF EXISTS idx_profile_embedding;
        ALTER TABLE user_profiles DROP COLUMN IF EXISTS embedding_vector;
    ");
}
```

---

### 3. Apply Manually (Local)

If running native Postgres (not Docker), apply SQL manually:

```bash
docker exec -it woven-postgres-1 psql -U woven -d woven_db
```

```sql
ALTER TABLE user_profiles
ADD COLUMN embedding_vector vector(1536);
```

**Then update migration history:**
```sql
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260525123456_AddEmbeddingVectors', '10.0.0');
```

---

## Rollback Migrations

### Rollback to Previous Migration

```bash
dotnet ef database update PreviousMigrationName
```

**Example:**
```bash
dotnet ef database update AddBridgeQuestion
```

---

### Rollback All Migrations

```bash
dotnet ef database update 0
```

**WARNING:** Drops all tables. Use only in development.

---

## Common Tasks

### List All Migrations

```bash
dotnet ef migrations list
```

**Output:**
```
20260525004334_InitialCreate
20260603000002_AddBridgeQuestion (Pending)
20260604000001_AddCoachingSummaries (Pending)
```

---

### Remove Last Migration (Before Applying)

If you made a mistake:

```bash
dotnet ef migrations remove
```

**Only works if migration has not been applied.**

---

### Generate SQL Script

To see SQL without applying:

```bash
dotnet ef migrations script
```

**For specific range:**
```bash
dotnet ef migrations script InitialCreate AddBridgeQuestion
```

---

## Migration File Structure

**Example migration:**
```csharp
public partial class AddMeetupLocation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // SQL to apply changes
        migrationBuilder.AddColumn<string>(
            name: "meetup_location",
            table: "matches",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // SQL to rollback changes
        migrationBuilder.DropColumn(
            name: "meetup_location",
            table: "matches");
    }
}
```

---

## Best Practices

1. **One logical change per migration** — Don't bundle unrelated schema changes
2. **Descriptive names** — `AddVoiceNoteTracking` not `UpdateTables`
3. **Test both Up and Down** — Ensure rollback works
4. **Review generated SQL** — EF doesn't always get it right
5. **Commit migration files** — Migrations are source-controlled
6. **Never edit applied migrations** — Create new migration to fix issues

---

## Troubleshooting

### "Cannot connect to database"

**Solution:** Start Postgres container:
```bash
docker compose up postgres -d
```

---

### "Migration already applied"

**Problem:** Trying to apply a migration that's already in database

**Solution:** Check migration status:
```bash
dotnet ef migrations list
```

If migration shows no "(Pending)", it's already applied.

---

### "Snapshot out of sync"

**Problem:** `WovenDbContextModelSnapshot.cs` doesn't match entities

**Solution:** Regenerate migration:
```bash
dotnet ef migrations remove  # Remove last migration
dotnet ef migrations add YourMigrationName  # Recreate
```

---

**Last Updated:** 2026-10-07
