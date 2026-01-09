using Xunit;

namespace TokenRelay.Tests.Integration.OAuth1;

/// <summary>
/// Collection definition for OAuth1 integration tests.
/// All tests in this collection share the same Docker container instance.
///
/// This ensures containers are started once before all tests and stopped after all tests.
/// </summary>
[CollectionDefinition("OAuth1 Integration Tests")]
public class OAuth1IntegrationTestCollection : ICollectionFixture<OAuth1IntegrationTestFixture>
{
    // This class has no code, and is never created.
    // Its purpose is simply to be the place to apply [CollectionDefinition]
    // and all the ICollectionFixture<> interfaces.
}
