using Microsoft.AspNetCore.Mvc;
using WovenBackend.Services.Validation;

namespace WovenBackend.Endpoints;

public static class AdminValidationEndpoints
{
    public static void MapAdminValidationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/validation").RequireAuthorization("Admin");

        // GET /admin/validation/run
        // Full ECHO persona validation: NDCG scores + weight convergence curve.
        // Response is pure JSON — pipe to a charting tool or the investor demo page.
        group.MapGet("/run", ([FromServices] IPersonaValidationService svc) =>
        {
            var report = svc.RunValidation();
            return Results.Ok(report);
        })
        .WithName("RunPersonaValidation")
        .WithSummary("Run ECHO synthetic persona validation (NDCG + weight convergence)");

        // GET /admin/validation/personas
        // Returns the 30 persona definitions for inspection.
        group.MapGet("/personas", () =>
        {
            var personas = SyntheticPersonas.All.Select(p => new
            {
                p.Id,
                p.Name,
                Cluster = p.Cluster.ToString(),
                PillarScores = p.Pillar.Select(v => Math.Round(v, 1)).ToList(),
                FingerprintScores = p.Fingerprint.Select(v => Math.Round(v, 3)).ToList()
            });
            return Results.Ok(personas);
        })
        .WithName("GetSyntheticPersonas")
        .WithSummary("Return the 30 synthetic persona definitions");

        // GET /admin/validation/oracle
        // Returns the full 30×30 pairwise oracle compatibility matrix.
        group.MapGet("/oracle", () =>
        {
            var personas = SyntheticPersonas.All;
            var matrix = personas.Select(viewer => new
            {
                ViewerId = viewer.Id,
                ViewerName = viewer.Name,
                ViewerCluster = viewer.Cluster.ToString(),
                Scores = personas
                    .Where(p => p.Id != viewer.Id)
                    .OrderByDescending(p => PersonaOracle.Compatibility(viewer, p))
                    .Select(p => new
                    {
                        CandidateId = p.Id,
                        CandidateName = p.Name,
                        CandidateCluster = p.Cluster.ToString(),
                        OracleScore = Math.Round(PersonaOracle.Compatibility(viewer, p), 3)
                    })
                    .ToList()
            });
            return Results.Ok(matrix);
        })
        .WithName("GetPersonaOracleMatrix")
        .WithSummary("Return the 30×30 oracle compatibility matrix");
    }
}
