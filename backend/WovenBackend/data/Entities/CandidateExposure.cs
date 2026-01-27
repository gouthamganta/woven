using System;

namespace WovenBackend.Data.Entities;

public class CandidateExposure
{
    public int Id { get; set; }

    // Viewer (the person seeing the profile)
    public int ViewerUserId { get; set; }

    // Shown user (the profile being shown)
    public int ShownUserId { get; set; }

    // Surface where exposure happened
    // DECK / MOMENTS / PENDING
    public string Surface { get; set; } = "DECK";

    // Bucket at time of exposure (CORE_FIT, LIFESTYLE_FIT, etc.)
    public string? Bucket { get; set; }

    // Snapshot of total score when shown
    public double? ScoreSnapshot { get; set; }

    // Used for "shown today" and throttling logic
    public DateOnly DateUtc { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
