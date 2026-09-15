namespace ArchitectureTests.Support;

/// <summary>
/// Classifies a discovered .csproj by which ring of CONVENTIONS.md "Project reference graph" it belongs to, purely from
/// its project name — no hand-maintained list of project names per service. That is what lets
/// one set of architecture-test files be copied verbatim into every service repo (the same way
/// Directory.Build.props is, per CONVENTIONS.md "Target framework") and still enforce the right rule against
/// whatever projects that repo happens to contain.
/// </summary>
internal enum ProjectRing
{
    Domain,
    Application,
    Infrastructure,
    Host,
    Contracts,
    BuildingBlocksCore,
    BuildingBlocksInfrastructure,
    // GL-75: BuildingBlocks.Testing (shared test-support helpers, e.g. the RabbitMQ queue-delete
    // helper) is an exact-name match the same way the two rows above it are — it is not a
    // fifth per-service ring, just a third fixed project this one repo happens to contain.
    BuildingBlocksTesting,
    Test,
    Unknown,
}
