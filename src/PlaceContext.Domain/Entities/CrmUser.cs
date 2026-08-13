using System.Security.Cryptography;
using PlaceContext.Domain.Common;

namespace PlaceContext.Domain.Entities;

public sealed class CrmUser
{
    private CrmUser(
        Guid id,
        Guid projectId,
        string? name,
        string email,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string? joinCode,
        DateTimeOffset? joinCodeExpiresAt,
        Guid? authUserId,
        DateTimeOffset? onboardedAt)
    {
        Id = id;
        ProjectId = projectId;
        Name = name;
        Email = email;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        JoinCode = joinCode;
        JoinCodeExpiresAt = joinCodeExpiresAt;
        AuthUserId = authUserId;
        OnboardedAt = onboardedAt;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public string? Name { get; }
    public string Email { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? JoinCode { get; private set; }
    public DateTimeOffset? JoinCodeExpiresAt { get; private set; }
    public Guid? AuthUserId { get; private set; }
    public DateTimeOffset? OnboardedAt { get; private set; }

    public static CrmUser Create(
        Guid projectId,
        string email,
        string? name,
        string joinCode,
        DateTimeOffset joinCodeExpiresAt,
        DateTimeOffset now)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));

        var cleanEmail = Normalize(email);
        if (cleanEmail is null || !cleanEmail.Contains('@', StringComparison.Ordinal))
            throw new ArgumentException("Email must be a valid address.", nameof(email));

        return new CrmUser(
            Guid.NewGuid(),
            projectId,
            Normalize(name),
            cleanEmail,
            now,
            now,
            joinCode,
            joinCodeExpiresAt,
            authUserId: null,
            onboardedAt: null);
    }

    public static CrmUser Rehydrate(
        Guid id,
        Guid projectId,
        string? name,
        string email,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string? joinCode,
        DateTimeOffset? joinCodeExpiresAt,
        Guid? authUserId,
        DateTimeOffset? onboardedAt)
        => new(
            id,
            projectId,
            Normalize(name),
            Normalize(email) ?? "",
            createdAt,
            updatedAt,
            joinCode,
            joinCodeExpiresAt,
            authUserId,
            onboardedAt);

    public bool IsJoinCodeValid(DateTimeOffset now)
        => !string.IsNullOrWhiteSpace(JoinCode)
           && OnboardedAt is null
           && JoinCodeExpiresAt is not null
           && JoinCodeExpiresAt.Value > now;

    public static bool IsJoinCodeFormatValid(string? joinCode)
    {
        if (string.IsNullOrWhiteSpace(joinCode) || joinCode.Length != 12)
            return false;

        for (var i = 0; i < joinCode.Length; i++)
        {
            var c = joinCode[i];
            var isDigit = (uint)(c - '0') <= 9u;
            var isLowerHex = (uint)(c - 'a') <= 5u;
            if (!isDigit && !isLowerHex)
                return false;
        }

        return true;
    }

    public static string NormalizeJoinCode(string code) => code.Trim().ToLowerInvariant();

    public void MarkOnboarded(Guid authUserId, DateTimeOffset now)
    {
        if (!IsJoinCodeValid(now))
            throw new InvalidOperationException("This onboarding code is invalid, expired, or already used.");

        AuthUserId = authUserId;
        OnboardedAt = now;
        JoinCode = null;
        JoinCodeExpiresAt = null;
        UpdatedAt = now;
    }

    public bool IsOnboarded => OnboardedAt is not null;

    public static string GenerateJoinCode()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
