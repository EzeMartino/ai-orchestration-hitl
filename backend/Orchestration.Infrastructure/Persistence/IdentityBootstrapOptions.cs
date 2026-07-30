namespace Orchestration.Infrastructure.Persistence;

public sealed class IdentityBootstrapOptions
{
    public const string SectionName = "IdentityBootstrap";

    public bool Enabled { get; init; }

    public IReadOnlyList<IdentityBootstrapUserOptions> Users { get; init; } = [];
}

public sealed class IdentityBootstrapUserOptions
{
    public Guid? Id { get; init; }

    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public bool EmailConfirmed { get; init; }
}
