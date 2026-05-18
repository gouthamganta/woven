namespace WovenBackend.Services.Verification;

public record VerificationResult(bool Success, bool? Verified, string? Error);

public record VerificationStatusResult(
    bool IsVerified,
    DateTimeOffset? VerifiedAt,
    string? VerificationType,
    LatestAttemptDto? LatestAttempt);

public record LatestAttemptDto(
    Guid Id,
    string Type,
    string Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? VerifiedAt,
    string? FailureReason);

public interface IVerificationService
{
    Task<VerificationResult> SubmitSelfieAsync(int userId, string blobPath, CancellationToken ct = default);
    Task<VerificationStatusResult> GetVerificationStatusAsync(int userId, CancellationToken ct = default);
}
