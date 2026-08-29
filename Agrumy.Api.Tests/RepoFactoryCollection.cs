namespace Agrumy.Api.Tests;

/// <summary>
/// Tests that swap a mock in through <c>RepoFactory.OverrideForTests</c> share one process-wide
/// static, so they must not run in parallel with each other. Every such test class carries
/// <c>[Collection("RepoFactory")]</c>.
/// </summary>
[CollectionDefinition("RepoFactory")]
public class RepoFactoryCollection { }
