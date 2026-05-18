namespace WovenBackend.Endpoints;

public static class LegalEndpoints
{
    public static void MapLegalEndpoints(this WebApplication app)
    {
        // GET /legal/privacy
        app.MapGet("/legal/privacy", () => Results.Ok(new
        {
            version = "1.0",
            lastUpdated = "2026-01-01",
            url = "https://wooven.me/privacy"
        })).WithName("LegalPrivacy");

        // GET /legal/terms
        app.MapGet("/legal/terms", () => Results.Ok(new
        {
            version = "1.0",
            lastUpdated = "2026-01-01",
            url = "https://wooven.me/terms"
        })).WithName("LegalTerms");

        // GET /legal/data-practices
        app.MapGet("/legal/data-practices", () => Results.Ok(new
        {
            dataCollected = new[]
            {
                "profile information",
                "behavioral patterns (hashed, not raw)",
                "match preferences learned from interactions",
                "conversation metadata (not message content)",
                "device fingerprint hash (not raw device ID)"
            },
            dataSentExternally = new object[]
            {
                new { service = "OpenAI", purpose = "match explanations, insights, game questions", piiStripped = true, retentionByVendor = "per OpenAI privacy policy" },
                new { service = "Replicate", purpose = "photo similarity matching (anonymous tokens only)", piiStripped = true, retentionByVendor = "per Replicate privacy policy" },
                new { service = "Azure Content Moderator", purpose = "content safety screening", piiStripped = true, retentionByVendor = "per Microsoft privacy policy" },
                new { service = "Azure Speech", purpose = "voice note transcription for moderation", piiStripped = true, retentionByVendor = "per Microsoft privacy policy" },
                new { service = "Google Places", purpose = "venue suggestions (city-level only)", piiStripped = true, retentionByVendor = "per Google privacy policy" }
            },
            retentionPeriods = new
            {
                profileData = "retained until account deletion",
                analyticsEvents = "12 months then anonymized",
                securityAuditLog = "90 days",
                chatMessages = "retained until account deletion",
                tileContent = "48 hours then deleted (unless highlighted)",
                verificationPhotos = "deleted immediately after verification"
            },
            userRights = new[]
            {
                "Export your data: GET /me/data-export",
                "Delete your account: DELETE /me/account",
                "Reset visual preferences: POST /me/visual-preference/reset",
                "Reset voice preferences: POST /me/voice-preference/reset",
                "Update accessibility: PUT /me/accessibility"
            }
        })).WithName("LegalDataPractices");
    }
}
