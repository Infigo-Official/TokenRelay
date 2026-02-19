using System;
using System.Collections.Generic;
using TokenRelay.Utilities;
using Xunit;

namespace TokenRelay.Tests.Utilities;

public class QueryParamsHelperTests
{
    private static readonly Dictionary<string, string> ConfiguredParams = new()
    {
        ["script"] = "customscript_test",
        ["deploy"] = "customdeploy_test"
    };

    #region Standalone Placeholder Tests

    [Fact]
    public void ResolveQueryParamPlaceholders_StandalonePlaceholder_ExpandsToKeyValue()
    {
        var (result, error) = QueryParamsHelper.ResolveQueryParamPlaceholders(
            "https://api.example.com/resource",
            ConfiguredParams,
            "?{script}");

        Assert.Null(error);
        Assert.Contains("script=customscript_test", result);
    }

    [Fact]
    public void ResolveQueryParamPlaceholders_MultiplePlaceholders_ExpandsAll()
    {
        var (result, error) = QueryParamsHelper.ResolveQueryParamPlaceholders(
            "https://api.example.com/resource",
            ConfiguredParams,
            "?{script}&{deploy}");

        Assert.Null(error);
        Assert.Contains("script=customscript_test", result);
        Assert.Contains("deploy=customdeploy_test", result);
    }

    #endregion

    #region Value Placeholder Tests

    [Fact]
    public void ResolveQueryParamPlaceholders_ValuePlaceholder_ReplacesValue()
    {
        var (result, error) = QueryParamsHelper.ResolveQueryParamPlaceholders(
            "https://api.example.com/resource",
            ConfiguredParams,
            "?key={script}");

        Assert.Null(error);
        Assert.Contains("key=customscript_test", result);
    }

    [Fact]
    public void ResolveQueryParamPlaceholders_MultipleValuePlaceholders_ReplacesAll()
    {
        var configuredParams = new Dictionary<string, string>
        {
            ["a"] = "val1",
            ["b"] = "val2"
        };

        var (result, error) = QueryParamsHelper.ResolveQueryParamPlaceholders(
            "https://api.example.com/resource",
            configuredParams,
            "?q={a}+{b}");

        Assert.Null(error);
        Assert.Contains("q=val1+val2", result);
    }

    #endregion

    #region Mixed Tests

    [Fact]
    public void ResolveQueryParamPlaceholders_MixedPlaceholdersAndLiterals()
    {
        var (result, error) = QueryParamsHelper.ResolveQueryParamPlaceholders(
            "https://api.example.com/resource",
            ConfiguredParams,
            "?{script}&name=foo&key={deploy}");

        Assert.Null(error);
        Assert.Contains("script=customscript_test", result);
        Assert.Contains("name=foo", result);
        Assert.Contains("key=customdeploy_test", result);
    }

    #endregion

    #region Passthrough Tests

    [Fact]
    public void ResolveQueryParamPlaceholders_NoPlaceholders_PassedThroughUnchanged()
    {
        var (result, error) = QueryParamsHelper.ResolveQueryParamPlaceholders(
            "https://api.example.com/resource",
            ConfiguredParams,
            "?name=foo&action=test");

        Assert.Null(error);
        Assert.Contains("name=foo", result);
        Assert.Contains("action=test", result);
        // Configured params should NOT be auto-added
        Assert.DoesNotContain("script=", result);
        Assert.DoesNotContain("deploy=", result);
    }

    [Fact]
    public void ResolveQueryParamPlaceholders_NoQueryString_ReturnsBaseUrlUnchanged()
    {
        var baseUrl = "https://api.example.com/resource";

        var (result, error) = QueryParamsHelper.ResolveQueryParamPlaceholders(
            baseUrl, ConfiguredParams, null);

        Assert.Null(error);
        Assert.Equal(baseUrl, result);
    }

    [Fact]
    public void ResolveQueryParamPlaceholders_EmptyQueryString_ReturnsBaseUrlUnchanged()
    {
        var baseUrl = "https://api.example.com/resource";

        var (result, error) = QueryParamsHelper.ResolveQueryParamPlaceholders(
            baseUrl, ConfiguredParams, "");

        Assert.Null(error);
        Assert.Equal(baseUrl, result);
    }

    [Fact]
    public void ResolveQueryParamPlaceholders_ConfiguredParamsNotAutoAdded()
    {
        // Key behavioral change: configured params are NOT auto-added when there's no placeholder
        var baseUrl = "https://api.example.com/resource";

        var (result, error) = QueryParamsHelper.ResolveQueryParamPlaceholders(
            baseUrl, ConfiguredParams, null);

        Assert.Null(error);
        Assert.Equal(baseUrl, result);
        Assert.DoesNotContain("script=", result);
        Assert.DoesNotContain("deploy=", result);
    }

    #endregion

    #region Error Tests

    [Fact]
    public void ResolveQueryParamPlaceholders_UnknownStandalonePlaceholder_ReturnsError()
    {
        var (_, error) = QueryParamsHelper.ResolveQueryParamPlaceholders(
            "https://api.example.com/resource",
            ConfiguredParams,
            "?{unknown}");

        Assert.NotNull(error);
        Assert.Contains("Unknown query parameter placeholder: unknown", error);
    }

    [Fact]
    public void ResolveQueryParamPlaceholders_UnknownValuePlaceholder_ReturnsError()
    {
        var (_, error) = QueryParamsHelper.ResolveQueryParamPlaceholders(
            "https://api.example.com/resource",
            ConfiguredParams,
            "?key={unknown}");

        Assert.NotNull(error);
        Assert.Contains("Unknown query parameter placeholder: unknown", error);
    }

    [Fact]
    public void ResolveQueryParamPlaceholders_NullConfigWithPlaceholder_ReturnsError()
    {
        var (_, error) = QueryParamsHelper.ResolveQueryParamPlaceholders(
            "https://api.example.com/resource",
            null,
            "?{script}");

        Assert.NotNull(error);
        Assert.Contains("Unknown query parameter placeholder: script", error);
    }

    [Fact]
    public void ResolveQueryParamPlaceholders_EmptyConfigWithPlaceholder_ReturnsError()
    {
        var (_, error) = QueryParamsHelper.ResolveQueryParamPlaceholders(
            "https://api.example.com/resource",
            new Dictionary<string, string>(),
            "?{script}");

        Assert.NotNull(error);
        Assert.Contains("Unknown query parameter placeholder: script", error);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ResolveQueryParamPlaceholders_BaseUrlWithExistingQueryString()
    {
        var (result, error) = QueryParamsHelper.ResolveQueryParamPlaceholders(
            "https://api.example.com/resource?existing=param",
            ConfiguredParams,
            "?{script}");

        Assert.Null(error);
        Assert.Contains("existing=param", result);
        Assert.Contains("script=customscript_test", result);
    }

    [Fact]
    public void ResolveQueryParamPlaceholders_QueryStringWithoutQuestionMark()
    {
        var (result, error) = QueryParamsHelper.ResolveQueryParamPlaceholders(
            "https://api.example.com/resource",
            ConfiguredParams,
            "{script}");

        Assert.Null(error);
        Assert.Contains("script=customscript_test", result);
    }

    [Fact]
    public void ResolveQueryParamPlaceholders_EmptyBaseUrl()
    {
        var (result, error) = QueryParamsHelper.ResolveQueryParamPlaceholders(
            "", ConfiguredParams, "?{script}");

        Assert.Null(error);
        Assert.Equal("", result);
    }

    [Fact]
    public void ResolveQueryParamPlaceholders_UrlEncodedPlaceholders_DecodesAndResolves()
    {
        var configuredParams = new Dictionary<string, string>
        {
            ["wpwhreceivable_group"] = "group1",
            ["wpwhreceivable_name"] = "name1",
            ["wpwhreceivable"] = "recv1"
        };

        // %7B = { and %7D = } — this is what browsers send
        var (result, error) = QueryParamsHelper.ResolveQueryParamPlaceholders(
            "https://api.example.com/resource",
            configuredParams,
            "?wpwhreceivable_group=%7Bwpwhreceivable_group%7D&wpwhreceivable_name=%7Bwpwhreceivable_name%7D&wpwhreceivable=%7Bwpwhreceivable%7D");

        Assert.Null(error);
        Assert.Contains("wpwhreceivable_group=group1", result);
        Assert.Contains("wpwhreceivable_name=name1", result);
        Assert.Contains("wpwhreceivable=recv1", result);
    }

    #endregion
}
