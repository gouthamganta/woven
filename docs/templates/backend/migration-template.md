# Database Migration Template

> Guidelines for creating and managing Entity Framework Core migrations.
> Follow these patterns to ensure safe, reversible database changes.

---

## Creating a Migration

### Step 1: Make Model Changes

```csharp
// Models/{Entity}.cs

public class Balloon
{
    public Guid Id { get; set; }
    public Guid UserAId { get; set; }
    public Guid UserBId { get; set; }
    public BalloonStatus Status { get; set; }

    // NEW FIELD - Add with nullable or default value first
    public DateTime? NewField { get; set; }

    // Navigation properties
    public User UserA { get; set; } = null!;
    public User UserB { get; set; } = null!;
}
```

### Step 2: Update DbContext (if needed)

```csharp
// Data/WovenDbContext.cs

public class WovenDbContext : DbContext
{
    public DbSet<Balloon> Balloons => Set<Balloon>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Add any new configurations
        modelBuilder.Entity<Balloon>(entity =>
        {
            entity.HasIndex(e => e.Status);  // New index
            entity.Property(e => e.NewField)
                .HasDefaultValue(null);
        });
    }
}
```

### Step 3: Generate Migration

```bash
# From backend/Woven.Api directory
dotnet ef migrations add {DescriptiveMigrationName}

# Examples:
dotnet ef migrations add AddNewFieldToBalloons
dotnet ef migrations add CreateRatingsTable
dotnet ef migrations add AddIndexOnBalloonStatus
```

### Step 4: Review Generated Migration

**Always review the generated migration before applying!**

```csharp
// Migrations/{Timestamp}_{MigrationName}.cs

public partial class AddNewFieldToBalloons : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Verify this is what you expect
        migrationBuilder.AddColumn<DateTime>(
            name: "NewField",
            table: "Balloons",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Verify rollback is correct
        migrationBuilder.DropColumn(
            name: "NewField",
            table: "Balloons");
    }
}
```

### Step 5: Apply Migration

```bash
# Development
dotnet ef database update

# Production - generate SQL script first
dotnet ef migrations script --idempotent -o migration.sql
```

---

## Migration Patterns

### Adding a New Table

```csharp
// Model
public class Rating
{
    public Guid Id { get; set; }
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    public int Value { get; set; }  // -100 to +100
    public DateTime CreatedAt { get; set; }

    public User FromUser { get; set; } = null!;
    public User ToUser { get; set; } = null!;
}

// DbContext configuration
modelBuilder.Entity<Rating>(entity =>
{
    entity.HasKey(e => e.Id);

    entity.Property(e => e.Value)
        .IsRequired();

    entity.Property(e => e.CreatedAt)
        .HasDefaultValueSql("CURRENT_TIMESTAMP");

    entity.HasIndex(e => new { e.FromUserId, e.ToUserId })
        .IsUnique();

    entity.HasIndex(e => e.ToUserId);  // For queries by recipient

    entity.HasOne(e => e.FromUser)
        .WithMany()
        .HasForeignKey(e => e.FromUserId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasOne(e => e.ToUser)
        .WithMany()
        .HasForeignKey(e => e.ToUserId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

### Adding a Column (Safe)

```csharp
// 1. Add as nullable first
public DateTime? NewField { get; set; }

// 2. Run migration
// 3. Backfill data if needed
// 4. Make non-nullable in separate migration (if required)
```

### Adding a Column with Default Value

```csharp
// Model
public BalloonStatus Status { get; set; } = BalloonStatus.ACTIVE;

// Configuration
entity.Property(e => e.Status)
    .HasDefaultValue(BalloonStatus.ACTIVE);
```

### Adding an Index

```csharp
// Single column index
entity.HasIndex(e => e.Status);

// Composite index
entity.HasIndex(e => new { e.UserId, e.CreatedAt });

// Unique index
entity.HasIndex(e => e.Email).IsUnique();

// Filtered index (PostgreSQL)
entity.HasIndex(e => e.Status)
    .HasFilter("\"Status\" = 'ACTIVE'");
```

### Renaming a Column

```csharp
// In migration Up()
migrationBuilder.RenameColumn(
    name: "OldName",
    table: "TableName",
    newName: "NewName");

// In migration Down()
migrationBuilder.RenameColumn(
    name: "NewName",
    table: "TableName",
    newName: "OldName");
```

### Adding a Foreign Key

```csharp
// Model
public Guid CategoryId { get; set; }
public Category Category { get; set; } = null!;

// Configuration
entity.HasOne(e => e.Category)
    .WithMany(c => c.Items)
    .HasForeignKey(e => e.CategoryId)
    .OnDelete(DeleteBehavior.Restrict);  // or Cascade, SetNull
```

---

## Safe Migration Practices

### DO ✅

```csharp
// Add columns as nullable first
public DateTime? NewField { get; set; }

// Add with default values
entity.Property(e => e.Status).HasDefaultValue(Status.Active);

// Add indexes for frequently queried columns
entity.HasIndex(e => e.UserId);

// Use transactions for data migrations
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(@"
        UPDATE ""Balloons""
        SET ""Status"" = 'ACTIVE'
        WHERE ""Status"" IS NULL;
    ");
}
```

### DON'T ❌

```csharp
// Don't add non-nullable columns without defaults to existing tables
public string RequiredField { get; set; } = null!;  // Will fail on existing rows

// Don't drop columns without backing up data
migrationBuilder.DropColumn(...);  // Data lost!

// Don't rename tables/columns without updating all references
migrationBuilder.RenameTable(...);  // May break running code
```

---

## Data Migrations

For complex data migrations, create a separate service:

```csharp
// Services/DataMigrationService.cs

public class DataMigrationService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<DataMigrationService> _logger;

    public async Task MigrateRatings()
    {
        var batchSize = 1000;
        var processed = 0;

        while (true)
        {
            var batch = await _db.OldRatings
                .Where(r => !r.Migrated)
                .Take(batchSize)
                .ToListAsync();

            if (batch.Count == 0)
                break;

            foreach (var old in batch)
            {
                _db.Ratings.Add(new Rating
                {
                    Id = Guid.NewGuid(),
                    FromUserId = old.RaterId,
                    ToUserId = old.RatedId,
                    Value = ConvertScore(old.Score),
                    CreatedAt = old.Date
                });

                old.Migrated = true;
            }

            await _db.SaveChangesAsync();
            processed += batch.Count;

            _logger.LogInformation("Migrated {Count} ratings", processed);
        }
    }
}
```

---

## Rolling Back Migrations

```bash
# Rollback to specific migration
dotnet ef database update {MigrationName}

# Rollback last migration
dotnet ef database update {PreviousMigrationName}

# Remove last migration (if not applied)
dotnet ef migrations remove
```

---

## Migration Naming Conventions

```
# Format: {Verb}{Entity}{Description}

# Adding
Add{Column}To{Table}         → AddStatusToBalloons
Add{Table}                   → AddRatingsTable
AddIndexOn{Table}{Columns}   → AddIndexOnBalloonsStatus

# Modifying
Modify{Column}In{Table}      → ModifyStatusInBalloons
Rename{Old}To{New}In{Table}  → RenameScoreToValueInRatings

# Removing
Remove{Column}From{Table}    → RemoveOldFieldFromBalloons
Drop{Table}                  → DropLegacyRatingsTable

# Complex
Refactor{Feature}            → RefactorMatchingSystem
Initial{Feature}Schema       → InitialChatSchema
```

---

## Checklist

Before running a migration:

- [ ] Model changes are complete
- [ ] DbContext configuration is updated
- [ ] Migration is generated and reviewed
- [ ] Down() method correctly reverses Up()
- [ ] New columns are nullable or have defaults
- [ ] Indexes added for frequently queried columns
- [ ] Foreign keys have appropriate delete behavior
- [ ] Data migrations tested on copy of production data
- [ ] Rollback tested in development
