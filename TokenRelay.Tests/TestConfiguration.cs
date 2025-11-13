using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using TokenRelay.Models;

namespace TokenRelay.Tests;

public static class TestConfiguration
{
    private static IConfiguration? _configuration;

    public static IConfiguration Configuration
    {
        get
        {
            if (_configuration == null)
            {
                _configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                    .Build();
            }
            return _configuration;
        }
    }

    public static TargetConfig GetValidOAuthTarget()
    {
        var config = Configuration.GetSection("TestOAuthCredentials:ValidTarget");
        return new TargetConfig
        {
            Endpoint = config["endpoint"] ?? throw new InvalidOperationException("Missing endpoint"),
            AuthType = "oauth",
            AuthData = new Dictionary<string, string>
            {
                ["token_endpoint"] = config["token_endpoint"] ?? throw new InvalidOperationException("Missing token_endpoint"),
                ["grant_type"] = config["grant_type"] ?? throw new InvalidOperationException("Missing grant_type"),
                ["username"] = config["username"] ?? throw new InvalidOperationException("Missing username"),
                ["password"] = config["password"] ?? throw new InvalidOperationException("Missing password"),
                ["client_id"] = config["client_id"] ?? throw new InvalidOperationException("Missing client_id"),
                ["client_secret"] = config["client_secret"] ?? string.Empty,
                ["scope"] = config["scope"] ?? string.Empty
            }
        };
    }

    public static TargetConfig GetDynamicEndpointTarget()
    {
        var config = Configuration.GetSection("TestOAuthCredentials:DynamicEndpointTarget");
        return new TargetConfig
        {
            Endpoint = config["endpoint"] ?? throw new InvalidOperationException("Missing endpoint"),
            AuthType = "oauth",
            AuthData = new Dictionary<string, string>
            {
                ["grant_type"] = config["grant_type"] ?? throw new InvalidOperationException("Missing grant_type"),
                ["username"] = config["username"] ?? throw new InvalidOperationException("Missing username"),
                ["password"] = config["password"] ?? throw new InvalidOperationException("Missing password"),
                ["client_id"] = config["client_id"] ?? throw new InvalidOperationException("Missing client_id")
                // Note: token_endpoint is intentionally missing - should be built dynamically
            }
        };
    }

    public static TargetConfig GetMinimalTarget()
    {
        var config = Configuration.GetSection("TestOAuthCredentials:MinimalTarget");
        return new TargetConfig
        {
            Endpoint = config["endpoint"] ?? throw new InvalidOperationException("Missing endpoint"),
            AuthType = "oauth",
            AuthData = new Dictionary<string, string>
            {
                ["grant_type"] = config["grant_type"] ?? throw new InvalidOperationException("Missing grant_type"),
                ["client_id"] = config["client_id"] ?? throw new InvalidOperationException("Missing client_id"),
                ["client_secret"] = config["client_secret"] ?? string.Empty
            }
        };
    }

    public static string GetMockTokenResponse(string responseName = "ValidTokenResponse")
    {
        var section = Configuration.GetSection($"MockOAuthResponses:{responseName}");
        var accessToken = section["access_token"] ?? "default-token";
        var tokenType = section["token_type"] ?? "Bearer";
        var expiresIn = section["expires_in"] ?? "3600";
        var refreshToken = section["refresh_token"];
        var scope = section["scope"];

        var response = $@"{{
            ""access_token"": ""{accessToken}"",
            ""token_type"": ""{tokenType}"",
            ""expires_in"": {expiresIn}";

        if (!string.IsNullOrEmpty(refreshToken))
        {
            response += $@",
            ""refresh_token"": ""{refreshToken}""";
        }

        if (!string.IsNullOrEmpty(scope))
        {
            response += $@",
            ""scope"": ""{scope}""";
        }

        response += "\n}";
        return response;
    }
}
