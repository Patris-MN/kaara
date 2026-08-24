namespace PTS.Modules.Tenancy;

public sealed class InvitationNotAllowedException : Exception
{
    public InvitationNotAllowedException(string reason)
        : base(reason)
    {
    }
}
