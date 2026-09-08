namespace PTS.Modules.Tenancy;

public sealed class InvitationExpiredException : Exception;

public sealed class InvitationRevokedException : Exception;

public sealed class InvitationAlreadyUsedException : Exception;

public sealed class InvitationEmailMismatchException : Exception;

public sealed class AlreadyTenantMemberException : Exception;

public sealed class MemberManagementForbiddenException : Exception;

public sealed class OwnerProtectionException : Exception;

public sealed class InvitationDeliveryResult
{
    public required Guid InvitationId { get; init; }

    public required string InvitedEmail { get; init; }

    public required string RawToken { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }

    /// <summary>Development-only invitation URL when email delivery is deferred.</summary>
    public string? DevelopmentInvitationUrl { get; init; }
}

public sealed class InvitationPreview
{
    public required string OrganizationName { get; init; }

    public required string InvitedEmail { get; init; }

    public required MembershipRole Role { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }

    public string? InviterDisplayName { get; init; }

    public required bool RequiresRegistration { get; init; }

    public IReadOnlyList<InvitationWorkspaceGrantPreview> WorkspaceGrants { get; init; } =
        Array.Empty<InvitationWorkspaceGrantPreview>();
}

public sealed record InvitationWorkspaceGrantPreview(string WorkspaceName, string AccessLevel);

public sealed class PendingTenantInvitation
{
    public required Guid Id { get; init; }

    public required string InvitedEmail { get; init; }

    public required MembershipRole Role { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public Guid? MembershipId { get; init; }
}
