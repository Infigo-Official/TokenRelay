using System.Text.Json;
using TokenRelay.Models;
using Xunit;

namespace TokenRelay.Tests.Models;

public class HealthCheckConfigTests
{
    #region HealthCheckConfig Defaults

    [Fact]
    public void HealthCheckConfig_DefaultsEnabled_ToTrue()
    {
        // Arrange & Act
        var config = new HealthCheckConfig();

        // Assert
        Assert.True(config.Enabled);
    }

    [Fact]
    public void HealthCheckConfig_DefaultsType_ToHttpGet()
    {
        // Arrange & Act
        var config = new HealthCheckConfig();

        // Assert
        Assert.Equal(HealthCheckType.HttpGet, config.Type);
    }

    [Fact]
    public void HealthCheckConfig_DefaultsUrl_ToEmptyString()
    {
        // Arrange & Act
        var config = new HealthCheckConfig();

        // Assert
        Assert.Equal(string.Empty, config.Url);
    }

    #endregion

    #region HealthCheckType Enum

    [Fact]
    public void HealthCheckType_HasHttpGetValue()
    {
        // Assert
        Assert.Equal(0, (int)HealthCheckType.HttpGet);
    }

    [Fact]
    public void HealthCheckType_HasTcpConnectValue()
    {
        // Assert
        Assert.Equal(1, (int)HealthCheckType.TcpConnect);
    }

    #endregion

    #region TargetConfig.EffectiveHealthCheck - Backward Compatibility

    [Fact]
    public void EffectiveHealthCheck_ReturnsNull_WhenBothHealthCheckAndHealthCheckUrlAreNull()
    {
        // Arrange
        var target = new TargetConfig
        {
            Endpoint = "https://api.example.com",
            HealthCheckUrl = null,
            HealthCheck = null
        };

        // Act
        var effective = target.EffectiveHealthCheck;

        // Assert
        Assert.Null(effective);
    }

    [Fact]
    public void EffectiveHealthCheck_ReturnsNull_WhenHealthCheckUrlIsEmpty()
    {
        // Arrange
        var target = new TargetConfig
        {
            Endpoint = "https://api.example.com",
            HealthCheckUrl = "",
            HealthCheck = null
        };

        // Act
        var effective = target.EffectiveHealthCheck;

        // Assert
        Assert.Null(effective);
    }

    [Fact]
    public void EffectiveHealthCheck_ReturnsNull_WhenHealthCheckUrlIsWhitespace()
    {
        // Arrange
        var target = new TargetConfig
        {
            Endpoint = "https://api.example.com",
            HealthCheckUrl = "   ",
            HealthCheck = null
        };

        // Act
        var effective = target.EffectiveHealthCheck;

        // Assert
        Assert.Null(effective);
    }

    [Fact]
    public void EffectiveHealthCheck_CreatesConfigFromLegacyUrl_WhenOnlyHealthCheckUrlIsSet()
    {
        // Arrange
        var target = new TargetConfig
        {
            Endpoint = "https://api.example.com",
            HealthCheckUrl = "https://api.example.com/health",
            HealthCheck = null
        };

        // Act
        var effective = target.EffectiveHealthCheck;

        // Assert
        Assert.NotNull(effective);
        Assert.Equal("https://api.example.com/health", effective.Url);
        Assert.True(effective.Enabled);
        Assert.Equal(HealthCheckType.HttpGet, effective.Type);
    }

    [Fact]
    public void EffectiveHealthCheck_ReturnsHealthCheck_WhenBothAreSet()
    {
        // Arrange - HealthCheck takes precedence over HealthCheckUrl
        var target = new TargetConfig
        {
            Endpoint = "https://api.example.com",
            HealthCheckUrl = "https://api.example.com/old-health",
            HealthCheck = new HealthCheckConfig
            {
                Url = "https://api.example.com/new-health",
                Enabled = true,
                Type = HealthCheckType.TcpConnect
            }
        };

        // Act
        var effective = target.EffectiveHealthCheck;

        // Assert
        Assert.NotNull(effective);
        Assert.Equal("https://api.example.com/new-health", effective.Url);
        Assert.Equal(HealthCheckType.TcpConnect, effective.Type);
    }

    [Fact]
    public void EffectiveHealthCheck_ReturnsHealthCheck_WhenOnlyHealthCheckIsSet()
    {
        // Arrange
        var target = new TargetConfig
        {
            Endpoint = "https://api.example.com",
            HealthCheckUrl = null,
            HealthCheck = new HealthCheckConfig
            {
                Url = "https://api.example.com/health",
                Enabled = false,
                Type = HealthCheckType.TcpConnect
            }
        };

        // Act
        var effective = target.EffectiveHealthCheck;

        // Assert
        Assert.NotNull(effective);
        Assert.Equal("https://api.example.com/health", effective.Url);
        Assert.False(effective.Enabled);
        Assert.Equal(HealthCheckType.TcpConnect, effective.Type);
    }

    #endregion

    #region JSON Serialization

    [Fact]
    public void HealthCheckConfig_SerializesTypeAsString()
    {
        // Arrange
        var config = new HealthCheckConfig
        {
            Url = "https://api.example.com/health",
            Enabled = true,
            Type = HealthCheckType.HttpGet
        };

        // Act
        var json = JsonSerializer.Serialize(config);

        // Assert
        Assert.Contains("\"Type\":\"HttpGet\"", json);
    }

    [Fact]
    public void HealthCheckConfig_SerializesTcpConnectTypeAsString()
    {
        // Arrange
        var config = new HealthCheckConfig
        {
            Url = "https://api.example.com",
            Enabled = true,
            Type = HealthCheckType.TcpConnect
        };

        // Act
        var json = JsonSerializer.Serialize(config);

        // Assert
        Assert.Contains("\"Type\":\"TcpConnect\"", json);
    }

    [Fact]
    public void HealthCheckConfig_DeserializesFromJson_HttpGet()
    {
        // Arrange
        var json = """
        {
            "Url": "https://api.example.com/health",
            "Enabled": true,
            "Type": "HttpGet"
        }
        """;

        // Act
        var config = JsonSerializer.Deserialize<HealthCheckConfig>(json);

        // Assert
        Assert.NotNull(config);
        Assert.Equal("https://api.example.com/health", config.Url);
        Assert.True(config.Enabled);
        Assert.Equal(HealthCheckType.HttpGet, config.Type);
    }

    [Fact]
    public void HealthCheckConfig_DeserializesFromJson_TcpConnect()
    {
        // Arrange
        var json = """
        {
            "Url": "https://api.example.com",
            "Enabled": true,
            "Type": "TcpConnect"
        }
        """;

        // Act
        var config = JsonSerializer.Deserialize<HealthCheckConfig>(json);

        // Assert
        Assert.NotNull(config);
        Assert.Equal("https://api.example.com", config.Url);
        Assert.True(config.Enabled);
        Assert.Equal(HealthCheckType.TcpConnect, config.Type);
    }

    [Fact]
    public void HealthCheckConfig_DeserializesWithDisabled()
    {
        // Arrange
        var json = """
        {
            "Url": "https://api.example.com/health",
            "Enabled": false,
            "Type": "HttpGet"
        }
        """;

        // Act
        var config = JsonSerializer.Deserialize<HealthCheckConfig>(json);

        // Assert
        Assert.NotNull(config);
        Assert.False(config.Enabled);
    }

    [Fact]
    public void TargetConfig_DeserializesWithNewHealthCheckFormat()
    {
        // Arrange
        var json = """
        {
            "Endpoint": "https://api.example.com",
            "HealthCheck": {
                "Url": "https://api.example.com/health",
                "Enabled": true,
                "Type": "HttpGet"
            },
            "Description": "Test API"
        }
        """;

        // Act
        var target = JsonSerializer.Deserialize<TargetConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // Assert
        Assert.NotNull(target);
        Assert.NotNull(target.HealthCheck);
        Assert.Equal("https://api.example.com/health", target.HealthCheck.Url);
        Assert.Equal(HealthCheckType.HttpGet, target.HealthCheck.Type);
    }

    [Fact]
    public void TargetConfig_DeserializesWithLegacyHealthCheckUrlFormat()
    {
        // Arrange
        var json = """
        {
            "Endpoint": "https://api.example.com",
            "HealthCheckUrl": "https://api.example.com/health",
            "Description": "Test API"
        }
        """;

        // Act
        var target = JsonSerializer.Deserialize<TargetConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // Assert
        Assert.NotNull(target);
        Assert.Null(target.HealthCheck);
        Assert.Equal("https://api.example.com/health", target.HealthCheckUrl);

        // EffectiveHealthCheck should still work
        var effective = target.EffectiveHealthCheck;
        Assert.NotNull(effective);
        Assert.Equal("https://api.example.com/health", effective.Url);
        Assert.Equal(HealthCheckType.HttpGet, effective.Type);
    }

    [Fact]
    public void TargetConfig_DeserializesWithBothFormats_HealthCheckTakesPrecedence()
    {
        // Arrange
        var json = """
        {
            "Endpoint": "https://api.example.com",
            "HealthCheckUrl": "https://api.example.com/old-health",
            "HealthCheck": {
                "Url": "https://api.example.com/new-health",
                "Enabled": true,
                "Type": "TcpConnect"
            },
            "Description": "Test API"
        }
        """;

        // Act
        var target = JsonSerializer.Deserialize<TargetConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // Assert
        Assert.NotNull(target);
        var effective = target.EffectiveHealthCheck;
        Assert.NotNull(effective);
        Assert.Equal("https://api.example.com/new-health", effective.Url);
        Assert.Equal(HealthCheckType.TcpConnect, effective.Type);
    }

    #endregion

    #region Case Insensitive Deserialization

    [Fact]
    public void HealthCheckConfig_DeserializesCaseInsensitive_LowerCase()
    {
        // Arrange
        var json = """
        {
            "url": "https://api.example.com/health",
            "enabled": true,
            "type": "HttpGet"
        }
        """;

        // Act
        var config = JsonSerializer.Deserialize<HealthCheckConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // Assert
        Assert.NotNull(config);
        Assert.Equal("https://api.example.com/health", config.Url);
        Assert.True(config.Enabled);
        Assert.Equal(HealthCheckType.HttpGet, config.Type);
    }

    #endregion

    #region Relative URL Support

    [Fact]
    public void HealthCheckConfig_SupportsRelativeUrl()
    {
        // Arrange
        var config = new HealthCheckConfig
        {
            Url = "/health",
            Enabled = true,
            Type = HealthCheckType.HttpGet
        };

        // Assert
        Assert.Equal("/health", config.Url);
    }

    [Fact]
    public void EffectiveHealthCheck_SupportsRelativeUrl_FromLegacy()
    {
        // Arrange
        var target = new TargetConfig
        {
            Endpoint = "https://api.example.com",
            HealthCheckUrl = "/health"
        };

        // Act
        var effective = target.EffectiveHealthCheck;

        // Assert
        Assert.NotNull(effective);
        Assert.Equal("/health", effective.Url);
    }

    #endregion
}
