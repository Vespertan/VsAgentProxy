using System;
using Newtonsoft.Json.Linq;
using Vespertan.VisualStudio.AgentProxy.Rpc;
using Xunit;

namespace Vespertan.VisualStudio.AgentProxy.Tests;

public sealed class BreakpointSelectorTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{'id':'second-breakpoint','index':0}")]
    [InlineData("{'id':''}")]
    [InlineData("{'id':'   ','index':0}")]
    [InlineData("{'index':-1}")]
    [InlineData("{'index':0.5}")]
    public void InvalidSelectorsCannotReachBreakpointLookup(string json)
    {
        Assert.Throws<ArgumentException>(() => ProtocolSupport.BreakpointSelector(JObject.Parse(json)));
    }

    [Fact]
    public void StableIdDoesNotSupplyAnIndexFallback()
    {
        var selector = ProtocolSupport.BreakpointSelector(new JObject { ["id"] = "second-breakpoint" });
        Assert.Equal("second-breakpoint", selector.Id);
        Assert.Null(selector.Index);
    }

    [Fact]
    public void FirstManualBreakpointCanBeSelectedByIndex()
    {
        var selector = ProtocolSupport.BreakpointSelector(new JObject { ["index"] = 0 });
        Assert.Null(selector.Id);
        Assert.Equal(0, selector.Index);
    }
}
