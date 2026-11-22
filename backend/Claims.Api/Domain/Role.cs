namespace Claims.Api.Domain;

public sealed class Role
{
    public const string Claimant = "Claimant";
    public const string ClaimsStaff = "ClaimsStaff";

    public required string Id { get; init; }
}
