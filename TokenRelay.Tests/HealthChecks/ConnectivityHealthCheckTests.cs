using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using TokenRelay.HealthChecks;
using TokenRelay.Models;
using TokenRelay.Services;
using Xunit;

namespace TokenRelay.Tests.HealthChecks;

public class ConnectivityHealthCheckTests
{
    private readonly Mock<IProxyService> _mockProxyService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<ILogger<ConnectivityHealthCheck>> _mockLogger;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;

    public ConnectivityHealthCheckTests()
    {
        _mockProxyService = new Mock<IProxyService>();
        _mockConfigService = new Mock<IConfigurationService>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockLogger = new Mock<ILogger<ConnectivityHealthCheck>>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
    }

    #region HttpGet Health Check Tests

    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_WhenHttpGetSucceeds()
    {
        // Arrange
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["api"] = new TargetConfig
            {
                Endpoint = "https://api.example.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://api.example.com/health",
                    Enabled = true,
                    Type = HealthCheckType.HttpGet
                }
            }
        });

        SetupMocks(config, HttpStatusCode.OK);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("1 checked targets are reachable", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_WhenHttpGetReturns401()
    {
        // Arrange - 401 is considered healthy (service is responding)
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["api"] = new TargetConfig
            {
                Endpoint = "https://api.example.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://api.example.com/health",
                    Enabled = true,
                    Type = HealthCheckType.HttpGet
                }
            }
        });

        SetupMocks(config, HttpStatusCode.Unauthorized);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_WhenHttpGetFails()
    {
        // Arrange
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["api"] = new TargetConfig
            {
                Endpoint = "https://api.example.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://api.example.com/health",
                    Enabled = true,
                    Type = HealthCheckType.HttpGet
                }
            }
        });

        SetupMocks(config, HttpStatusCode.InternalServerError);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsDegraded_WhenSomeTargetsFail()
    {
        // Arrange
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["api1"] = new TargetConfig
            {
                Endpoint = "https://api1.example.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://api1.example.com/health",
                    Enabled = true,
                    Type = HealthCheckType.HttpGet
                }
            },
            ["api2"] = new TargetConfig
            {
                Endpoint = "https://api2.example.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://api2.example.com/health",
                    Enabled = true,
                    Type = HealthCheckType.HttpGet
                }
            }
        });

        // Setup api1 to succeed, api2 to fail
        SetupMocksWithMultipleResponses(config, new Dictionary<string, HttpStatusCode>
        {
            ["https://api1.example.com/health"] = HttpStatusCode.OK,
            ["https://api2.example.com/health"] = HttpStatusCode.InternalServerError
        });
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("Some targets unreachable", result.Description);
    }

    #endregion

    #region Legacy HealthCheckUrl Tests

    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_WithLegacyHealthCheckUrl()
    {
        // Arrange - Using legacy healthCheckUrl string
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["api"] = new TargetConfig
            {
                Endpoint = "https://api.example.com",
                HealthCheckUrl = "https://api.example.com/health", // Legacy format
                HealthCheck = null
            }
        });

        SetupMocks(config, HttpStatusCode.OK);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    #endregion

    #region Disabled Health Check Tests

    [Fact]
    public async Task CheckHealthAsync_SkipsTarget_WhenHealthCheckDisabled()
    {
        // Arrange
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["api"] = new TargetConfig
            {
                Endpoint = "https://api.example.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://api.example.com/health",
                    Enabled = false, // Disabled
                    Type = HealthCheckType.HttpGet
                }
            }
        });

        SetupMocks(config, HttpStatusCode.OK);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("No health checks configured", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_SkipsTarget_WhenNoHealthCheckConfigured()
    {
        // Arrange
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["api"] = new TargetConfig
            {
                Endpoint = "https://api.example.com",
                HealthCheckUrl = null,
                HealthCheck = null
            }
        });

        SetupMocks(config, HttpStatusCode.OK);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("No health checks configured", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_SkipsTarget_WhenHealthCheckUrlEmpty()
    {
        // Arrange
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["api"] = new TargetConfig
            {
                Endpoint = "https://api.example.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "", // Empty URL
                    Enabled = true,
                    Type = HealthCheckType.HttpGet
                }
            }
        });

        SetupMocks(config, HttpStatusCode.OK);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("No health checks configured", result.Description);
    }

    #endregion

    #region Mixed Configuration Tests

    [Fact]
    public async Task CheckHealthAsync_HandlesMultipleTargets_WithMixedConfigurations()
    {
        // Arrange - Mix of new format, legacy format, and disabled
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["api1"] = new TargetConfig
            {
                Endpoint = "https://api1.example.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://api1.example.com/health",
                    Enabled = true,
                    Type = HealthCheckType.HttpGet
                }
            },
            ["api2"] = new TargetConfig
            {
                Endpoint = "https://api2.example.com",
                HealthCheckUrl = "https://api2.example.com/health" // Legacy
            },
            ["api3"] = new TargetConfig
            {
                Endpoint = "https://api3.example.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://api3.example.com/health",
                    Enabled = false // Disabled
                }
            }
        });

        SetupMocksWithMultipleResponses(config, new Dictionary<string, HttpStatusCode>
        {
            ["https://api1.example.com/health"] = HttpStatusCode.OK,
            ["https://api2.example.com/health"] = HttpStatusCode.OK
        });
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("2 checked targets", result.Description);
        Assert.Contains("Skipped", result.Description);
    }

    #endregion

    #region Data Reporting Tests

    [Fact]
    public async Task CheckHealthAsync_ReportsCorrectData()
    {
        // Arrange
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["api1"] = new TargetConfig
            {
                Endpoint = "https://api1.example.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://api1.example.com/health",
                    Enabled = true,
                    Type = HealthCheckType.HttpGet
                }
            },
            ["api2"] = new TargetConfig
            {
                Endpoint = "https://api2.example.com",
                HealthCheck = null // No health check
            }
        });

        SetupMocks(config, HttpStatusCode.OK);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(2, result.Data["total_targets"]);
        Assert.Equal(1, result.Data["checked_targets"]);
        Assert.Equal(1, result.Data["skipped_targets"]);
        Assert.Equal(1, result.Data["healthy_targets"]);
        Assert.Equal(0, result.Data["unhealthy_targets"]);
    }

    #endregion

    #region Connection Error Tests

    [Fact]
    public async Task CheckHealthAsync_HandlesConnectionException()
    {
        // Arrange
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["api"] = new TargetConfig
            {
                Endpoint = "https://api.example.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://api.example.com/health",
                    Enabled = true,
                    Type = HealthCheckType.HttpGet
                }
            }
        });

        SetupMocksWithException(config, new HttpRequestException("Connection refused"));
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("unreachable", result.Description);
    }

    #endregion

    #region TcpConnect Tests

    [Fact]
    public async Task CheckHealthAsync_SkipsTarget_WhenTcpConnectTypeConfigured_ButNoRealConnection()
    {
        // Arrange - TcpConnect type should be executed but we can't easily mock TCP
        // This test verifies the type is recognized and processed
        var config = CreateProxyConfig(new Dictionary<string, TargetConfig>
        {
            ["api"] = new TargetConfig
            {
                Endpoint = "https://api.example.com",
                HealthCheck = new HealthCheckConfig
                {
                    Url = "https://localhost:99999", // Invalid port that won't connect
                    Enabled = true,
                    Type = HealthCheckType.TcpConnect
                }
            }
        });

        SetupMocks(config, HttpStatusCode.OK);
        var healthCheck = CreateHealthCheck();

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert - Should be unhealthy because TCP connection fails
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("unreachable", result.Description);
    }

    #endregion

    #region Helper Methods

    private ConnectivityHealthCheck CreateHealthCheck()
    {
        return new ConnectivityHealthCheck(
            _mockProxyService.Object,
            _mockConfigService.Object,
            _mockHttpClientFactory.Object,
            _mockLogger.Object);
    }

    private ProxyConfig CreateProxyConfig(Dictionary<string, TargetConfig> targets)
    {
        return new ProxyConfig
        {
            Targets = targets
        };
    }

    private void SetupMocks(ProxyConfig config, HttpStatusCode statusCode)
    {
        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode
            });

        var httpClient = new HttpClient(_mockHttpMessageHandler.Object);
        _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
    }

    private void SetupMocksWithMultipleResponses(ProxyConfig config, Dictionary<string, HttpStatusCode> responses)
    {
        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken ct) =>
            {
                var url = request.RequestUri?.ToString() ?? "";
                var statusCode = responses.ContainsKey(url) ? responses[url] : HttpStatusCode.OK;
                return new HttpResponseMessage { StatusCode = statusCode };
            });

        var httpClient = new HttpClient(_mockHttpMessageHandler.Object);
        _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
    }

    private void SetupMocksWithException(ProxyConfig config, Exception exception)
    {
        _mockConfigService.Setup(c => c.GetProxyConfig()).Returns(config);

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(exception);

        var httpClient = new HttpClient(_mockHttpMessageHandler.Object);
        _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
    }

    #endregion
}
