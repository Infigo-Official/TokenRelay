using Xunit;

namespace TokenRelay.Tests.Integration.Chain;

/// <summary>
/// xUnit Collection definition for Chain Mode integration tests.
/// Ensures all tests in this collection share a single fixture instance,
/// preventing multiple container startups/shutdowns.
/// </summary>
[CollectionDefinition("Chain Mode Integration Tests")]
public class ChainModeIntegrationTestCollection : ICollectionFixture<ChainModeIntegrationTestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
