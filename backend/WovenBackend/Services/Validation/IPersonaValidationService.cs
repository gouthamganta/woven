namespace WovenBackend.Services.Validation;

public interface IPersonaValidationService
{
    /// <summary>
    /// Runs the full ECHO persona validation pipeline:
    /// - Scores all 30×29 persona pairs using pillar + fingerprint cosine similarity
    /// - Computes NDCG@5 and NDCG@10 vs the research oracle (initial weights)
    /// - Simulates 100 epochs of logistic regression on oracle-derived observations
    /// - Reports learned weights and final NDCG
    /// </summary>
    ValidationReport RunValidation();
}
