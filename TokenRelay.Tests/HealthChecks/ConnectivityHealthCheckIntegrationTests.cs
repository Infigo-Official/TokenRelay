using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using TokenRelay.HealthChecks;
using TokenRelay.Models;
using TokenRelay.Services;
using Xunit;

namespace TokenRelay.Tests.HealthChecks;

/// <summary>
/// Integration tests for ConnectivityHealthCheck using real network calls.
/// These tests require internet connectivity to run.
/// Uses TCP connect tests for reliability (HTTP tests can be flaky due to rate limits, User-Agent requirements, etc.)
/// </summary>
[Trait("Category", "Integration")]
public class ConnectivityHealthCheckIntegrationTests : IDisposable
{
    private readonly Mock<IProxyService> _mockProxyService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Mock<ILogger<ConnectivityHealthCheck>> _mockLogger;
    private readonly HttpClient _httpClient;

    public ConnectivityHealthCheckIntegrationTests()
    {
        _mockProxyService = new Mock<IProxyService>();
        _mockConfigService = new Mock<IConfigurationService>();
        _mockLogger = new Mock<ILogger<ConnectivityHealthCheck>>();

        // Use real HttpClient for integration tests
        _httpClient = new HttpClient();
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);
        _httpClientFactory = mockFactory.Object;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    #region TCP Connect Tests - Most Reliable

    [Fact]
    public async Task TcpConnect_ReturnsHealthy_WhenGoogleIsReachable()
    {
        // Arrange - Google is always reachable on port 443
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["google"] = new TargetConfig
            {
                Endpoint = "https://www.google.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://www.google.com",
                    Enabled = true,
                    Type = HealthCheckType.TcpConnect
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, result.Data["healthy_targets"]);
    }

    [Fact]
    public async Task TcpConnect_ReturnsHealthy_WhenMicrosoftIsReachable()
    {
        // Arrange - Microsoft is always reachable on port 443
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["microsoft"] = new TargetConfig
            {
                Endpoint = "https://www.microsoft.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://www.microsoft.com",
                    Enabled = true,
                    Type = HealthCheckType.TcpConnect
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task TcpConnect_ReturnsUnhealthy_WhenPortIsUnreachable()
    {
        // Arrange - Invalid port that won't be open
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["invalid"] = new TargetConfig
            {
                Endpoint = "https://localhost:59999",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://localhost:59999",
                    Enabled = true,
                    Type = HealthCheckType.TcpConnect
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(1, result.Data["unhealthy_targets"]);
    }

    [Fact]
    public async Task TcpConnect_ReturnsUnhealthy_WhenHostDoesNotExist()
    {
        // Arrange - Non-existent host
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["nonexistent"] = new TargetConfig
            {
                Endpoint = "https://this-host-does-not-exist-12345.example.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://this-host-does-not-exist-12345.example.com",
                    Enabled = true,
                    Type = HealthCheckType.TcpConnect
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task TcpConnect_ReturnsDegraded_WhenSomeTargetsFail()
    {
        // Arrange - One succeeds, one fails
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["google-ok"] = new TargetConfig
            {
                Endpoint = "https://www.google.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://www.google.com",
                    Enabled = true,
                    Type = HealthCheckType.TcpConnect
                }
            },
            ["invalid-fail"] = new TargetConfig
            {
                Endpoint = "https://localhost:59999",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://localhost:59999",
                    Enabled = true,
                    Type = HealthCheckType.TcpConnect
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(1, result.Data["healthy_targets"]);
        Assert.Equal(1, result.Data["unhealthy_targets"]);
    }

    [Fact]
    public async Task TcpConnect_ReturnsHealthy_WhenMultipleTargetsSucceed()
    {
        // Arrange - Multiple reliable endpoints
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["google"] = new TargetConfig
            {
                Endpoint = "https://www.google.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://www.google.com",
                    Enabled = true,
                    Type = HealthCheckType.TcpConnect
                }
            },
            ["microsoft"] = new TargetConfig
            {
                Endpoint = "https://www.microsoft.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://www.microsoft.com",
                    Enabled = true,
                    Type = HealthCheckType.TcpConnect
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(2, result.Data["healthy_targets"]);
        Assert.Equal(0, result.Data["unhealthy_targets"]);
    }

    #endregion

    #region HTTP POST Tests

    [Fact]
    public async Task HttpPost_ReturnsHealthy_WhenEndpointAcceptsPost()
    {
        // Arrange - postman-echo.com/post accepts POST requests and returns 200
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["httpbin"] = new TargetConfig
            {
                Endpoint = "https://postman-echo.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://postman-echo.com/post",
                    Enabled = true,
                    Type = HealthCheckType.HttpPost
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, result.Data["healthy_targets"]);
    }

    [Fact]
    public async Task HttpPost_ReturnsHealthy_WithJsonBody()
    {
        // Arrange - POST with JSON body
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["httpbin"] = new TargetConfig
            {
                Endpoint = "https://postman-echo.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://postman-echo.com/post",
                    Enabled = true,
                    Type = HealthCheckType.HttpPost,
                    Body = "{\"test\": \"value\"}",
                    ContentType = "application/json"
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, result.Data["healthy_targets"]);
    }

    [Fact]
    public async Task HttpPost_ReturnsHealthy_WithXmlContentType()
    {
        // Arrange - POST with XML content type
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["httpbin"] = new TargetConfig
            {
                Endpoint = "https://postman-echo.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://postman-echo.com/post",
                    Enabled = true,
                    Type = HealthCheckType.HttpPost,
                    Body = "<root><test>value</test></root>",
                    ContentType = "application/xml"
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, result.Data["healthy_targets"]);
    }

    [Fact]
    public async Task HttpPost_ReturnsHealthy_WithFormUrlEncodedContentType()
    {
        // Arrange - POST with form-urlencoded content type
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["httpbin"] = new TargetConfig
            {
                Endpoint = "https://postman-echo.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://postman-echo.com/post",
                    Enabled = true,
                    Type = HealthCheckType.HttpPost,
                    Body = "key1=value1&key2=value2",
                    ContentType = "application/x-www-form-urlencoded"
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, result.Data["healthy_targets"]);
    }

    [Fact]
    public async Task HttpPost_ReturnsUnhealthy_WhenEndpointOnlyAcceptsGet()
    {
        // Arrange - postman-echo.com/get returns 405 Method Not Allowed for POST
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["httpbin"] = new TargetConfig
            {
                Endpoint = "https://postman-echo.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://postman-echo.com/get",
                    Enabled = true,
                    Type = HealthCheckType.HttpPost,
                    ExpectedStatusCodes = new List<int> { 200 }
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(1, result.Data["unhealthy_targets"]);
    }

    [Fact]
    public async Task HttpPost_ReturnsHealthy_WithMatchingExpectedStatusCode()
    {
        // Arrange - postman-echo.com/post returns 200, and we expect 200
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["postman-echo"] = new TargetConfig
            {
                Endpoint = "https://postman-echo.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://postman-echo.com/post",
                    Enabled = true,
                    Type = HealthCheckType.HttpPost,
                    ExpectedStatusCodes = new List<int> { 200 }
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, result.Data["healthy_targets"]);
    }

    [Fact]
    public async Task HttpPost_ReturnsUnhealthy_WithNonMatchingExpectedStatusCode()
    {
        // Arrange - postman-echo.com/post returns 200, but we expect 201
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["postman-echo"] = new TargetConfig
            {
                Endpoint = "https://postman-echo.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://postman-echo.com/post",
                    Enabled = true,
                    Type = HealthCheckType.HttpPost,
                    ExpectedStatusCodes = new List<int> { 201 }
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(1, result.Data["unhealthy_targets"]);
    }

    [Fact]
    public async Task HttpPost_ReturnsHealthy_WithMultipleExpectedStatusCodes()
    {
        // Arrange - postman-echo.com/post returns 200, and 200 is in our expected list
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["postman-echo"] = new TargetConfig
            {
                Endpoint = "https://postman-echo.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://postman-echo.com/post",
                    Enabled = true,
                    Type = HealthCheckType.HttpPost,
                    ExpectedStatusCodes = new List<int> { 200, 201, 202 }
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, result.Data["healthy_targets"]);
    }

    #endregion

    #region HTTP GET Status Code Tests

    [Fact]
    public async Task HttpGet_ReturnsHealthy_WithCustomExpectedStatusCode()
    {
        // Arrange - postman-echo.com/status/201 returns 201
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["postman-echo"] = new TargetConfig
            {
                Endpoint = "https://postman-echo.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://postman-echo.com/status/201",
                    Enabled = true,
                    Type = HealthCheckType.HttpGet,
                    ExpectedStatusCodes = new List<int> { 201 }
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, result.Data["healthy_targets"]);
    }

    [Fact]
    public async Task HttpGet_ReturnsHealthy_WithMultipleExpectedStatusCodes()
    {
        // Arrange - Accepting 200, 201, 202 (postman-echo.com/status/202 returns 202)
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["postman-echo"] = new TargetConfig
            {
                Endpoint = "https://postman-echo.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://postman-echo.com/status/202",
                    Enabled = true,
                    Type = HealthCheckType.HttpGet,
                    ExpectedStatusCodes = new List<int> { 200, 201, 202 }
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, result.Data["healthy_targets"]);
    }

    [Fact]
    public async Task HttpGet_ReturnsHealthy_When401Received()
    {
        // Arrange - 401 is always considered healthy (service is responding, just requires auth)
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["postman-echo"] = new TargetConfig
            {
                Endpoint = "https://postman-echo.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://postman-echo.com/status/401",
                    Enabled = true,
                    Type = HealthCheckType.HttpGet,
                    ExpectedStatusCodes = new List<int> { 200 } // Even though 401 is not in expected codes
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, result.Data["healthy_targets"]);
    }

    #endregion

    #region Disabled Health Check Tests

    [Fact]
    public async Task DisabledHealthCheck_SkipsTarget()
    {
        // Arrange - Health check is disabled
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["api"] = new TargetConfig
            {
                Endpoint = "https://localhost:59999",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://localhost:59999", // Would fail if enabled
                    Enabled = false, // But it's disabled
                    Type = HealthCheckType.TcpConnect
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, result.Data["skipped_targets"]);
        Assert.Equal(0, result.Data["checked_targets"]);
    }

    [Fact]
    public async Task NoHealthCheck_SkipsTarget()
    {
        // Arrange - No health check configured
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["api"] = new TargetConfig
            {
                Endpoint = "https://example.com",
                HealthCheck = null,
                HealthCheckUrl = null
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, result.Data["skipped_targets"]);
    }

    #endregion

    #region Real-World Scenario Tests

    [Fact]
    public async Task RealWorldScenario_MultipleTargets_MixedConfiguration()
    {
        // Arrange - Simulates a real-world configuration with multiple targets
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["api-primary"] = new TargetConfig
            {
                Endpoint = "https://www.google.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://www.google.com",
                    Enabled = true,
                    Type = HealthCheckType.TcpConnect
                },
                Description = "Primary API with TCP health check"
            },
            ["api-secondary"] = new TargetConfig
            {
                Endpoint = "https://www.microsoft.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://www.microsoft.com",
                    Enabled = true,
                    Type = HealthCheckType.TcpConnect
                },
                Description = "Secondary API with TCP health check"
            },
            ["api-disabled"] = new TargetConfig
            {
                Endpoint = "https://example.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://localhost:59999", // Would fail if enabled
                    Enabled = false,
                    Type = HealthCheckType.TcpConnect
                },
                Description = "API with disabled health check"
            },
            ["api-no-healthcheck"] = new TargetConfig
            {
                Endpoint = "https://internal.example.com",
                Description = "Internal API without health check"
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(4, result.Data["total_targets"]);
        Assert.Equal(2, result.Data["checked_targets"]); // primary, secondary
        Assert.Equal(2, result.Data["skipped_targets"]); // disabled, no-healthcheck
        Assert.Equal(2, result.Data["healthy_targets"]);
        Assert.Equal(0, result.Data["unhealthy_targets"]);
    }

    [Fact]
    public async Task RealWorldScenario_AllTargetsUnreachable()
    {
        // Arrange - All targets fail
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["api1"] = new TargetConfig
            {
                Endpoint = "https://localhost:59998",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://localhost:59998",
                    Enabled = true,
                    Type = HealthCheckType.TcpConnect
                }
            },
            ["api2"] = new TargetConfig
            {
                Endpoint = "https://localhost:59999",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://localhost:59999",
                    Enabled = true,
                    Type = HealthCheckType.TcpConnect
                }
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(0, result.Data["healthy_targets"]);
        Assert.Equal(2, result.Data["unhealthy_targets"]);
    }

    #endregion

    #region Data Reporting Tests

    [Fact]
    public async Task HealthCheck_ReportsCorrectStatistics()
    {
        // Arrange
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["healthy1"] = new TargetConfig
            {
                Endpoint = "https://www.google.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://www.google.com",
                    Enabled = true,
                    Type = HealthCheckType.TcpConnect
                }
            },
            ["unhealthy1"] = new TargetConfig
            {
                Endpoint = "https://localhost:59999",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://localhost:59999",
                    Enabled = true,
                    Type = HealthCheckType.TcpConnect
                }
            },
            ["disabled1"] = new TargetConfig
            {
                Endpoint = "https://example.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://example.com",
                    Enabled = false,
                    Type = HealthCheckType.TcpConnect
                }
            },
            ["noconfigured1"] = new TargetConfig
            {
                Endpoint = "https://example.com"
            }
        });

        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(4, result.Data["total_targets"]);
        Assert.Equal(2, result.Data["checked_targets"]);
        Assert.Equal(2, result.Data["skipped_targets"]);
        Assert.Equal(1, result.Data["healthy_targets"]);
        Assert.Equal(1, result.Data["unhealthy_targets"]);
    }

    #endregion

    #region Helper Methods

    private ConnectivityHealthCheck CreateHealthCheck()
    {
        return new ConnectivityHealthCheck(
            _mockProxyService.Object,
            _mockConfigService.Object,
            _httpClientFactory,
            _mockLogger.Object);
    }

    private ProxyConfig CreateProxyConfig(Dictionary<string, TargetConfig> targets)
    {
        return new ProxyConfig
        {
            Targets = targets
        };
    }

    #endregion
}
