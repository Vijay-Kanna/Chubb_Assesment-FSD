namespace Claims.Api.Domain;

public sealed class User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public required string Email { get; init; }
    public string PasswordHash { get; set; } = string.Empty;
    public required string RoleId { get; init; }
}
