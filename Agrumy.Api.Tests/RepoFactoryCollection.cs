namespace Agrumy.Api.Tests;

/// <summary>
/// Tests that set the process-wide <c>EfRepository.ProviderOverride</c> /
/// <c>ConnectionStringOverride</c> seams (the integration tests) must not run in parallel with
/// each other, so they carry <c>[Collection("RepoFactory")]</c>.
/// </summary>
[CollectionDefinition("RepoFactory")]
public class RepoFactoryCollection { }
