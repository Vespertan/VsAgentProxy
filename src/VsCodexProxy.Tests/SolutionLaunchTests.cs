using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;

namespace VsCodexProxy.Tests;

public sealed class SolutionLaunchTests
{
    [Fact]
    public void SharedAndUserProfilesWithTheSameNameRequireScope()
    {
        var profiles = JArray.Parse("[{'name':'Demo','scope':'shared'},{'name':'Demo','scope':'user'}]");
        Assert.Equal("ambiguousProfile", Assert.Throws<ProxyException>(() => SolutionLaunchService.SelectIndex(profiles, "Demo", null)).Code);
        Assert.Equal(1, SolutionLaunchService.SelectIndex(profiles, "Demo", "user"));
        Assert.Equal("profileNotFound", Assert.Throws<ProxyException>(() => SolutionLaunchService.SelectIndex(profiles, "missing", null)).Code);
        Assert.Throws<ArgumentException>(() => SolutionLaunchService.SelectIndex(profiles, "Demo", "typo"));
    }

    [Theory]
    [InlineData("None", 0)]
    [InlineData("StartWithoutDebugging", 1)]
    [InlineData("Start", 2)]
    public void LaunchActionsPreserveDebuggerSemantics(string action, int mode) => Assert.Equal(mode, SolutionLaunchService.ActionMode(action));

    [Fact]
    public void UnknownActionsAreNotSilentlyStarted() => Assert.Throws<ArgumentException>(() => SolutionLaunchService.ActionMode("Unexpected"));

    [Theory]
    [InlineData("mode", "1")]
    [InlineData("order", "1")]
    [InlineData("debugTarget", "'Edge'")]
    public void MatchingProfileNameDoesNotHideIncorrectProjectSettings(string property, string value)
    {
        var expected = JArray.Parse("[{'id':'a','mode':2,'order':0,'debugTarget':'Chrome'}]");
        var state = JObject.Parse("{'mode':1,'projects':[{'id':'a','mode':2,'order':0,'debugTarget':'Chrome'}]}");
        Assert.True(SolutionLaunchService.Matches(expected, state));
        state["projects"]![0]![property] = JToken.Parse(value);
        Assert.False(SolutionLaunchService.Matches(expected, state));
    }

    [Fact]
    public void UnexpectedStartedProjectOrSingleProjectModeInvalidatesSelection()
    {
        var expected = JArray.Parse("[{'id':'a','mode':2,'order':0}]");
        var state = JObject.Parse("{'mode':1,'projects':[{'id':'a','mode':2,'order':0},{'id':'b','mode':1,'order':1}]}");
        Assert.False(SolutionLaunchService.Matches(expected, state));
        state["projects"]![1]!["mode"] = 0;
        Assert.True(SolutionLaunchService.Matches(expected, state));
        state["mode"] = 0;
        Assert.False(SolutionLaunchService.Matches(expected, state));
    }

    [Fact]
    public void DiskFallbackIncludesPrivateProfilesButNeverClaimsTheyAreSelected()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VsCodexProfiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Test.slnLaunch"), "[{'Name':'Shared','Projects':[]}]");
            File.WriteAllText(Path.Combine(directory, "Test.slnLaunch.user"), "[{'Name':'Private','Projects':[]}]");
            var result = SolutionLaunchService.DiskProfiles(Path.Combine(directory, "Test.slnx"));
            Assert.Equal(2, ((JArray)result["profiles"]!).Count);
            Assert.Equal("user", (string?)result["profiles"]![1]!["scope"]);
            Assert.Equal(JTokenType.Null, result["activeProfile"]!.Type);
            Assert.Equal("unsupported", (string?)result["selectionAvailability"]);
        }
        finally
        {
            File.Delete(Path.Combine(directory, "Test.slnLaunch"));
            File.Delete(Path.Combine(directory, "Test.slnLaunch.user"));
            Directory.Delete(directory);
        }
    }
}
