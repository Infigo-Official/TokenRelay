using Xunit;

namespace TokenRelay.Tests.Integration.Downloader;

/// <summary>
/// xUnit Collection definition for Downloader integration tests.
/// Ensures all tests in this collection share a single fixture instance,
/// preventing multiple container startups/shutdowns.
/// </summary>
[CollectionDefinition("Downloader Integration Tests")]
public class DownloaderIntegrationTestCollection : ICollectionFixture<DownloaderIntegrationTestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
