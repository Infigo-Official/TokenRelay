using System;
using System.Collections.Generic;
using TokenRelay.Utilities;
using Xunit;

namespace TokenRelay.Tests.Utilities;

public class QueryParamsHelperTests
{
    #region MergeQueryParams Tests

    [Fact]
    public void MergeQueryParams_AppendsConfiguredParams()
    {
        // Arrange
        var baseUrl = "https://api.example.com/resource";
        var configuredParams = new Dictionary<string, string>
        {
            ["script"] = "customscript_test",
            ["deploy"] = "customdeploy_test"
        };

        // Act
        var result = QueryParamsHelper.MergeQueryParams(baseUrl, configuredParams, null);

        // Assert
        Assert.Contains("script=customscript_test", result);
        Assert.Contains("deploy=customdeploy_test", result);
    }

    [Fact]
    public void MergeQueryParams_MergesRequestParams()
    {
        // Arrange
        var baseUrl = "https://api.example.com/resource";
        var requestQueryString = "?action=create&format=json";

        // Act
        var result = QueryParamsHelper.MergeQueryParams(baseUrl, null, requestQueryString);

        // Assert
        Assert.Contains("action=create", result);
        Assert.Contains("format=json", result);
    }

    [Fact]
    public void MergeQueryParams_RequestParamsOverrideConfigured()
    {
        // Arrange
        var baseUrl = "https://api.example.com/resource";
        var configuredParams = new Dictionary<string, string>
        {
            ["script"] = "config_script",
            ["deploy"] = "config_deploy"
        };
        var requestQueryString = "?script=override_script";

        // Act
        var result = QueryParamsHelper.MergeQueryParams(baseUrl, configuredParams, requestQueryString);

        // Assert
        Assert.Contains("script=override_script", result);
        Assert.Contains("deploy=config_deploy", result);
        // Should only have one script param (overridden)
        Assert.DoesNotContain("config_script", result);
    }

    [Fact]
    public void MergeQueryParams_ReturnsOriginalUrl_WhenNoParams()
    {
        // Arrange
        var baseUrl = "https://api.example.com/resource";

        // Act
        var result = QueryParamsHelper.MergeQueryParams(baseUrl, null, null);

        // Assert
        Assert.Equal(baseUrl, result);
    }

    [Fact]
    public void MergeQueryParams_ReturnsOriginalUrl_WhenEmptyParams()
    {
        // Arrange
        var baseUrl = "https://api.example.com/resource";
        var emptyParams = new Dictionary<string, string>();

        // Act
        var result = QueryParamsHelper.MergeQueryParams(baseUrl, emptyParams, "");

        // Assert
        Assert.Equal(baseUrl, result);
    }

    [Fact]
    public void MergeQueryParams_HandlesEmptyConfiguredParams()
    {
        // Arrange
        var baseUrl = "https://api.example.com/resource";
        var emptyParams = new Dictionary<string, string>();
        var requestQueryString = "?action=test";

        // Act
        var result = QueryParamsHelper.MergeQueryParams(baseUrl, emptyParams, requestQueryString);

        // Assert
        Assert.Contains("action=test", result);
    }

    [Fact]
    public void MergeQueryParams_HandlesEmptyRequestQueryString()
    {
        // Arrange
        var baseUrl = "https://api.example.com/resource";
        var configuredParams = new Dictionary<string, string>
        {
            ["param1"] = "value1"
        };

        // Act
        var result = QueryParamsHelper.MergeQueryParams(baseUrl, configuredParams, "");

        // Assert
        Assert.Contains("param1=value1", result);
    }

    [Fact]
    public void MergeQueryParams_PreservesExistingUrlQueryParams()
    {
        // Arrange
        var baseUrl = "https://api.example.com/resource?existing=param";
        var configuredParams = new Dictionary<string, string>
        {
            ["new"] = "value"
        };

        // Act
        var result = QueryParamsHelper.MergeQueryParams(baseUrl, configuredParams, null);

        // Assert
        Assert.Contains("existing=param", result);
        Assert.Contains("new=value", result);
    }

    [Fact]
    public void MergeQueryParams_HandlesNetSuiteScriptDeployParams()
    {
        // Arrange - NetSuite specific use case
        var baseUrl = "https://1234567.restlets.api.netsuite.com/app/site/hosting/restlet.nl";
        var configuredParams = new Dictionary<string, string>
        {
            ["script"] = "customscript_jpcw_customerportal_rl",
            ["deploy"] = "customdeploy_jpcw_customerportal_rl"
        };

        // Act
        var result = QueryParamsHelper.MergeQueryParams(baseUrl, configuredParams, null);

        // Assert
        Assert.Contains("script=customscript_jpcw_customerportal_rl", result);
        Assert.Contains("deploy=customdeploy_jpcw_customerportal_rl", result);
        Assert.StartsWith("https://1234567.restlets.api.netsuite.com", result);
    }

    [Fact]
    public void MergeQueryParams_HandlesSpecialCharactersInValues()
    {
        // Arrange
        var baseUrl = "https://api.example.com/resource";
        var configuredParams = new Dictionary<string, string>
        {
            ["filter"] = "name=test&status=active"
        };

        // Act
        var result = QueryParamsHelper.MergeQueryParams(baseUrl, configuredParams, null);

        // Assert
        Assert.Contains("filter=", result);
        // The value should be present (URL encoding may vary by implementation)
        // Just verify the filter parameter exists with a value containing the key parts
        Assert.True(result.Contains("name") && result.Contains("test"));
    }

    [Fact]
    public void MergeQueryParams_SkipsEmptyValueParams()
    {
        // Arrange
        var baseUrl = "https://api.example.com/resource";
        var configuredParams = new Dictionary<string, string>
        {
            ["valid"] = "value",
            ["empty"] = "",
            ["null"] = null!
        };

        // Act
        var result = QueryParamsHelper.MergeQueryParams(baseUrl, configuredParams, null);

        // Assert
        Assert.Contains("valid=value", result);
        Assert.DoesNotContain("empty=", result);
    }

    [Fact]
    public void MergeQueryParams_HandlesBothConfiguredAndRequestParams()
    {
        // Arrange
        var baseUrl = "https://api.example.com/resource";
        var configuredParams = new Dictionary<string, string>
        {
            ["configured1"] = "config_value1",
            ["configured2"] = "config_value2"
        };
        var requestQueryString = "?request1=req_value1&request2=req_value2";

        // Act
        var result = QueryParamsHelper.MergeQueryParams(baseUrl, configuredParams, requestQueryString);

        // Assert
        Assert.Contains("configured1=config_value1", result);
        Assert.Contains("configured2=config_value2", result);
        Assert.Contains("request1=req_value1", result);
        Assert.Contains("request2=req_value2", result);
    }

    [Fact]
    public void MergeQueryParams_HandlesRequestQueryStringWithoutQuestionMark()
    {
        // Arrange
        var baseUrl = "https://api.example.com/resource";
        // Note: Some systems might pass query string without leading ?
        var requestQueryString = "action=test";

        // Act
        var result = QueryParamsHelper.MergeQueryParams(baseUrl, null, requestQueryString);

        // Assert - Should still work
        Assert.Contains("action=test", result);
    }

    #endregion

    #region HasQueryParams Tests

    [Fact]
    public void HasQueryParams_ReturnsTrue_WhenParamsExist()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["key"] = "value"
        };

        // Act
        var result = QueryParamsHelper.HasQueryParams(queryParams);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasQueryParams_ReturnsFalse_WhenNull()
    {
        // Act
        var result = QueryParamsHelper.HasQueryParams(null);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasQueryParams_ReturnsFalse_WhenEmpty()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>();

        // Act
        var result = QueryParamsHelper.HasQueryParams(queryParams);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasQueryParams_ReturnsFalse_WhenOnlyEmptyValues()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["key1"] = "",
            ["key2"] = null!
        };

        // Act
        var result = QueryParamsHelper.HasQueryParams(queryParams);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasQueryParams_ReturnsTrue_WhenAtLeastOneNonEmptyValue()
    {
        // Arrange
        var queryParams = new Dictionary<string, string>
        {
            ["empty"] = "",
            ["valid"] = "value"
        };

        // Act
        var result = QueryParamsHelper.HasQueryParams(queryParams);

        // Assert
        Assert.True(result);
    }

    #endregion
}
