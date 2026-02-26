using System;
using System.Collections.Generic;
using TokenRelay.Utilities;
using Xunit;

namespace TokenRelay.Tests.Utilities;

public class VariablesHelperTests
{
    private static readonly Dictionary<string, string> ConfiguredVars = new()
    {
        ["script"] = "customscript_test",
        ["deploy"] = "customdeploy_test"
    };

    #region Standalone Placeholder Tests

    [Fact]
    public void ResolveVariablePlaceholders_StandalonePlaceholder_ExpandsToKeyValue()
    {
        var (result, error) = VariablesHelper.ResolveVariablePlaceholders(
            "https://api.example.com/resource",
            ConfiguredVars,
            "?{script}");

        Assert.Null(error);
        Assert.Contains("script=customscript_test", result);
    }

    [Fact]
    public void ResolveVariablePlaceholders_MultiplePlaceholders_ExpandsAll()
    {
        var (result, error) = VariablesHelper.ResolveVariablePlaceholders(
            "https://api.example.com/resource",
            ConfiguredVars,
            "?{script}&{deploy}");

        Assert.Null(error);
        Assert.Contains("script=customscript_test", result);
        Assert.Contains("deploy=customdeploy_test", result);
    }

    #endregion

    #region Value Placeholder Tests

    [Fact]
    public void ResolveVariablePlaceholders_ValuePlaceholder_ReplacesValue()
    {
        var (result, error) = VariablesHelper.ResolveVariablePlaceholders(
            "https://api.example.com/resource",
            ConfiguredVars,
            "?key={script}");

        Assert.Null(error);
        Assert.Contains("key=customscript_test", result);
    }

    [Fact]
    public void ResolveVariablePlaceholders_MultipleValuePlaceholders_ReplacesAll()
    {
        var variables = new Dictionary<string, string>
        {
            ["a"] = "val1",
            ["b"] = "val2"
        };

        var (result, error) = VariablesHelper.ResolveVariablePlaceholders(
            "https://api.example.com/resource",
            variables,
            "?q={a}+{b}");

        Assert.Null(error);
        Assert.Contains("q=val1+val2", result);
    }

    #endregion

    #region Mixed Tests

    [Fact]
    public void ResolveVariablePlaceholders_MixedPlaceholdersAndLiterals()
    {
        var (result, error) = VariablesHelper.ResolveVariablePlaceholders(
            "https://api.example.com/resource",
            ConfiguredVars,
            "?{script}&name=foo&key={deploy}");

        Assert.Null(error);
        Assert.Contains("script=customscript_test", result);
        Assert.Contains("name=foo", result);
        Assert.Contains("key=customdeploy_test", result);
    }

    #endregion

    #region Passthrough Tests

    [Fact]
    public void ResolveVariablePlaceholders_NoPlaceholders_PassedThroughUnchanged()
    {
        var (result, error) = VariablesHelper.ResolveVariablePlaceholders(
            "https://api.example.com/resource",
            ConfiguredVars,
            "?name=foo&action=test");

        Assert.Null(error);
        Assert.Contains("name=foo", result);
        Assert.Contains("action=test", result);
        // Configured vars should NOT be auto-added
        Assert.DoesNotContain("script=", result);
        Assert.DoesNotContain("deploy=", result);
    }

    [Fact]
    public void ResolveVariablePlaceholders_NoQueryString_ReturnsBaseUrlUnchanged()
    {
        var baseUrl = "https://api.example.com/resource";

        var (result, error) = VariablesHelper.ResolveVariablePlaceholders(
            baseUrl, ConfiguredVars, null);

        Assert.Null(error);
        Assert.Equal(baseUrl, result);
    }

    [Fact]
    public void ResolveVariablePlaceholders_EmptyQueryString_ReturnsBaseUrlUnchanged()
    {
        var baseUrl = "https://api.example.com/resource";

        var (result, error) = VariablesHelper.ResolveVariablePlaceholders(
            baseUrl, ConfiguredVars, "");

        Assert.Null(error);
        Assert.Equal(baseUrl, result);
    }

    [Fact]
    public void ResolveVariablePlaceholders_VariablesNotAutoAdded()
    {
        // Key behavioral change: configured vars are NOT auto-added when there's no placeholder
        var baseUrl = "https://api.example.com/resource";

        var (result, error) = VariablesHelper.ResolveVariablePlaceholders(
            baseUrl, ConfiguredVars, null);

        Assert.Null(error);
        Assert.Equal(baseUrl, result);
        Assert.DoesNotContain("script=", result);
        Assert.DoesNotContain("deploy=", result);
    }

    #endregion

    #region Error Tests

    [Fact]
    public void ResolveVariablePlaceholders_UnknownStandalonePlaceholder_ReturnsError()
    {
        var (_, error) = VariablesHelper.ResolveVariablePlaceholders(
            "https://api.example.com/resource",
            ConfiguredVars,
            "?{unknown}");

        Assert.NotNull(error);
        Assert.Contains("Unknown query parameter placeholder: unknown", error);
    }

    [Fact]
    public void ResolveVariablePlaceholders_UnknownValuePlaceholder_ReturnsError()
    {
        var (_, error) = VariablesHelper.ResolveVariablePlaceholders(
            "https://api.example.com/resource",
            ConfiguredVars,
            "?key={unknown}");

        Assert.NotNull(error);
        Assert.Contains("Unknown query parameter placeholder: unknown", error);
    }

    [Fact]
    public void ResolveVariablePlaceholders_NullConfigWithPlaceholder_ReturnsError()
    {
        var (_, error) = VariablesHelper.ResolveVariablePlaceholders(
            "https://api.example.com/resource",
            null,
            "?{script}");

        Assert.NotNull(error);
        Assert.Contains("Unknown query parameter placeholder: script", error);
    }

    [Fact]
    public void ResolveVariablePlaceholders_EmptyConfigWithPlaceholder_ReturnsError()
    {
        var (_, error) = VariablesHelper.ResolveVariablePlaceholders(
            "https://api.example.com/resource",
            new Dictionary<string, string>(),
            "?{script}");

        Assert.NotNull(error);
        Assert.Contains("Unknown query parameter placeholder: script", error);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ResolveVariablePlaceholders_BaseUrlWithExistingQueryString()
    {
        var (result, error) = VariablesHelper.ResolveVariablePlaceholders(
            "https://api.example.com/resource?existing=param",
            ConfiguredVars,
            "?{script}");

        Assert.Null(error);
        Assert.Contains("existing=param", result);
        Assert.Contains("script=customscript_test", result);
    }

    [Fact]
    public void ResolveVariablePlaceholders_QueryStringWithoutQuestionMark()
    {
        var (result, error) = VariablesHelper.ResolveVariablePlaceholders(
            "https://api.example.com/resource",
            ConfiguredVars,
            "{script}");

        Assert.Null(error);
        Assert.Contains("script=customscript_test", result);
    }

    [Fact]
    public void ResolveVariablePlaceholders_EmptyBaseUrl()
    {
        var (result, error) = VariablesHelper.ResolveVariablePlaceholders(
            "", ConfiguredVars, "?{script}");

        Assert.Null(error);
        Assert.Equal("", result);
    }

    [Fact]
    public void ResolveVariablePlaceholders_UrlEncodedPlaceholders_DecodesAndResolves()
    {
        var variables = new Dictionary<string, string>
        {
            ["wpwhreceivable_group"] = "group1",
            ["wpwhreceivable_name"] = "name1",
            ["wpwhreceivable"] = "recv1"
        };

        // %7B = { and %7D = } — this is what browsers send
        var (result, error) = VariablesHelper.ResolveVariablePlaceholders(
            "https://api.example.com/resource",
            variables,
            "?wpwhreceivable_group=%7Bwpwhreceivable_group%7D&wpwhreceivable_name=%7Bwpwhreceivable_name%7D&wpwhreceivable=%7Bwpwhreceivable%7D");

        Assert.Null(error);
        Assert.Contains("wpwhreceivable_group=group1", result);
        Assert.Contains("wpwhreceivable_name=name1", result);
        Assert.Contains("wpwhreceivable=recv1", result);
    }

    #endregion

    #region Body Placeholder Tests

    [Fact]
    public void ResolveBodyPlaceholders_KnownPlaceholders_Replaced()
    {
        var variables = new Dictionary<string, string>
        {
            ["name"] = "John",
            ["age"] = "30"
        };

        var body = """{"user": "{{name}}", "age": "{{age}}"}""";
        var result = VariablesHelper.ResolveBodyPlaceholders(body, variables);

        Assert.Equal("""{"user": "John", "age": "30"}""", result);
    }

    [Fact]
    public void ResolveBodyPlaceholders_UnknownPlaceholders_LeftAsIs()
    {
        var variables = new Dictionary<string, string>
        {
            ["name"] = "John"
        };

        var body = """{"user": "{{name}}", "id": "{{unknown}}"}""";
        var result = VariablesHelper.ResolveBodyPlaceholders(body, variables);

        Assert.Equal("""{"user": "John", "id": "{{unknown}}"}""", result);
    }

    [Fact]
    public void ResolveBodyPlaceholders_SingleBraces_NotMatched()
    {
        var variables = new Dictionary<string, string>
        {
            ["name"] = "John"
        };

        var body = """{"user": "{name}"}""";
        var result = VariablesHelper.ResolveBodyPlaceholders(body, variables);

        Assert.Equal("""{"user": "{name}"}""", result);
    }

    [Fact]
    public void ResolveBodyPlaceholders_NullBody_ReturnsEmpty()
    {
        var variables = new Dictionary<string, string> { ["x"] = "y" };
        var result = VariablesHelper.ResolveBodyPlaceholders(null, variables);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ResolveBodyPlaceholders_EmptyBody_ReturnsEmpty()
    {
        var variables = new Dictionary<string, string> { ["x"] = "y" };
        var result = VariablesHelper.ResolveBodyPlaceholders("", variables);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ResolveBodyPlaceholders_NullVariables_ReturnsBodyUnchanged()
    {
        var body = """{"user": "{{name}}"}""";
        var result = VariablesHelper.ResolveBodyPlaceholders(body, null);
        Assert.Equal(body, result);
    }

    [Fact]
    public void ResolveBodyPlaceholders_EmptyVariables_ReturnsBodyUnchanged()
    {
        var body = """{"user": "{{name}}"}""";
        var result = VariablesHelper.ResolveBodyPlaceholders(body, new Dictionary<string, string>());
        Assert.Equal(body, result);
    }

    [Fact]
    public void ResolveBodyPlaceholders_NoPlaceholders_ReturnsBodyUnchanged()
    {
        var variables = new Dictionary<string, string> { ["name"] = "John" };
        var body = """{"user": "Jane"}""";
        var result = VariablesHelper.ResolveBodyPlaceholders(body, variables);
        Assert.Equal(body, result);
    }

    #endregion
}
