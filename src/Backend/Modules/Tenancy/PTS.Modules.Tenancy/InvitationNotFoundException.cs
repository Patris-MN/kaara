namespace PTS.Modules.Tenancy;

public sealed class InvitationNotFoundException : Exception
{
    public InvitationNotFoundException()
        : base("No pending invitation exists for this user and tenant.")
    {
    }
}
