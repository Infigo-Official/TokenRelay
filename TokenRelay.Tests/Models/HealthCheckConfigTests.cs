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

    [Fact]
    public void HealthCheckConfig_DefaultsBody_ToNull()
    {
        // Arrange & Act
        var config = new HealthCheckConfig();

        // Assert
        Assert.Null(config.Body);
    }

    [Fact]
    public void HealthCheckConfig_DefaultsExpectedStatusCodes_ToNull()
    {
        // Arrange & Act
        var config = new HealthCheckConfig();

        // Assert
        Assert.Null(config.ExpectedStatusCodes);
    }

    [Fact]
    public void HealthCheckConfig_EffectiveExpectedStatusCodes_DefaultsTo200()
    {
        // Arrange & Act
        var config = new HealthCheckConfig();

        // Assert
        Assert.Single(config.EffectiveExpectedStatusCodes);
        Assert.Equal(200, config.EffectiveExpectedStatusCodes[0]);
    }

    [Fact]
    public void HealthCheckConfig_EffectiveExpectedStatusCodes_ReturnsConfiguredCodes()
    {
        // Arrange
        var config = new HealthCheckConfig
        {
            ExpectedStatusCodes = [200, 201, 202]
        };

        // Assert
        Assert.Equal(3, config.EffectiveExpectedStatusCodes.Count);
        Assert.Contains(200, config.EffectiveExpectedStatusCodes);
        Assert.Contains(201, config.EffectiveExpectedStatusCodes);
        Assert.Contains(202, config.EffectiveExpectedStatusCodes);
    }

    [Fact]
    public void HealthCheckConfig_EffectiveExpectedStatusCodes_DefaultsWhenEmptyList()
    {
        // Arrange
        var config = new HealthCheckConfig
        {
            ExpectedStatusCodes = []
        };

        // Assert - Empty list should default to [200]
        Assert.Single(config.EffectiveExpectedStatusCodes);
        Assert.Equal(200, config.EffectiveExpectedStatusCodes[0]);
    }

    [Fact]
    public void HealthCheckConfig_DefaultsContentType_ToNull()
    {
        // Arrange & Act
        var config = new HealthCheckConfig();

        // Assert
        Assert.Null(config.ContentType);
    }

    [Fact]
    public void HealthCheckConfig_EffectiveContentType_DefaultsToApplicationJson()
    {
        // Arrange & Act
        var config = new HealthCheckConfig();

        // Assert
        Assert.Equal("application/json", config.EffectiveContentType);
    }

    [Fact]
    public void HealthCheckConfig_EffectiveContentType_ReturnsConfiguredValue()
    {
        // Arrange
        var config = new HealthCheckConfig
        {
            ContentType = "application/xml"
        };

        // Assert
        Assert.Equal("application/xml", config.EffectiveContentType);
    }

    [Fact]
    public void HealthCheckConfig_EffectiveContentType_DefaultsWhenWhitespace()
    {
        // Arrange
        var config = new HealthCheckConfig
        {
            ContentType = "   "
        };

        // Assert
        Assert.Equal("application/json", config.EffectiveContentType);
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
        // Assert - TcpConnect is at index 1 for backward compatibility
        Assert.Equal(1, (int)HealthCheckType.TcpConnect);
    }

    [Fact]
    public void HealthCheckType_HasHttpPostValue()
    {
        // Assert - HttpPost is at index 2 (added after TcpConnect)
        Assert.Equal(2, (int)HealthCheckType.HttpPost);
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
    public void HealthCheckConfig_SerializesHttpPostTypeAsString()
    {
        // Arrange
        var config = new HealthCheckConfig
        {
            Url = "https://api.example.com/health",
            Enabled = true,
            Type = HealthCheckType.HttpPost,
            Body = "{\"check\": \"health\"}"
        };

        // Act
        var json = JsonSerializer.Serialize(config);

        // Assert
        Assert.Contains("\"Type\":\"HttpPost\"", json);
        Assert.Contains("\"Body\":", json);
        Assert.Contains("check", json);
    }

    [Fact]
    public void HealthCheckConfig_SerializesExpectedStatusCodes()
    {
        // Arrange
        var config = new HealthCheckConfig
        {
            Url = "https://api.example.com/health",
            Enabled = true,
            Type = HealthCheckType.HttpGet,
            ExpectedStatusCodes = [200, 201, 202]
        };

        // Act
        var json = JsonSerializer.Serialize(config);

        // Assert
        Assert.Contains("\"ExpectedStatusCodes\":[200,201,202]", json);
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
    public void HealthCheckConfig_DeserializesFromJson_HttpPost()
    {
        // Arrange
        var json = """
        {
            "Url": "https://api.example.com/health",
            "Enabled": true,
            "Type": "HttpPost",
            "Body": "{\"check\": \"health\"}"
        }
        """;

        // Act
        var config = JsonSerializer.Deserialize<HealthCheckConfig>(json);

        // Assert
        Assert.NotNull(config);
        Assert.Equal("https://api.example.com/health", config.Url);
        Assert.True(config.Enabled);
        Assert.Equal(HealthCheckType.HttpPost, config.Type);
        Assert.Equal("{\"check\": \"health\"}", config.Body);
    }

    [Fact]
    public void HealthCheckConfig_DeserializesFromJson_WithExpectedStatusCodes()
    {
        // Arrange
        var json = """
        {
            "Url": "https://api.example.com/health",
            "Enabled": true,
            "Type": "HttpGet",
            "ExpectedStatusCodes": [200, 201, 202, 204]
        }
        """;

        // Act
        var config = JsonSerializer.Deserialize<HealthCheckConfig>(json);

        // Assert
        Assert.NotNull(config);
        Assert.NotNull(config.ExpectedStatusCodes);
        Assert.Equal(4, config.ExpectedStatusCodes.Count);
        Assert.Contains(200, config.ExpectedStatusCodes);
        Assert.Contains(201, config.ExpectedStatusCodes);
        Assert.Contains(202, config.ExpectedStatusCodes);
        Assert.Contains(204, config.ExpectedStatusCodes);
    }

    [Fact]
    public void HealthCheckConfig_DeserializesFromJson_HttpPostWithAllProperties()
    {
        // Arrange
        var json = """
        {
            "Url": "https://api.example.com/health",
            "Enabled": true,
            "Type": "HttpPost",
            "Body": "{\"status\": \"check\"}",
            "ExpectedStatusCodes": [200, 201]
        }
        """;

        // Act
        var config = JsonSerializer.Deserialize<HealthCheckConfig>(json);

        // Assert
        Assert.NotNull(config);
        Assert.Equal("https://api.example.com/health", config.Url);
        Assert.True(config.Enabled);
        Assert.Equal(HealthCheckType.HttpPost, config.Type);
        Assert.Equal("{\"status\": \"check\"}", config.Body);
        Assert.NotNull(config.ExpectedStatusCodes);
        Assert.Equal(2, config.ExpectedStatusCodes.Count);
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
