using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using EnvDTE;
using Newtonsoft.Json.Linq;
using Vespertan.VisualStudio.AgentProxy.Rpc;
using Vespertan.VisualStudio.AgentProxy.Services;
using Xunit;

namespace Vespertan.VisualStudio.AgentProxy.Tests;

public sealed class BreakpointReplacementTests
{
    [Fact]
    public void FailedCreationPreservesOriginal()
    {
        var original = new TestBreakpoint();
        var error = Assert.Throws<ProxyException>(() => BreakpointReplacement.Replace(original.Instance,
            () => throw new InvalidOperationException("Add rejected."), () => "new"));

        Assert.False(original.Deleted);
        Assert.False(error.Details.Value<bool>("originalDeleteAttempted"));
    }

    [Fact]
    public void EmptyCreationPreservesOriginal()
    {
        var original = new TestBreakpoint();
        Assert.Throws<ProxyException>(() => Replace(original));
        Assert.False(original.Deleted);
    }

    [Fact]
    public void ConfigurationFailureCleansAllReplacementsAndPreservesOriginal()
    {
        var original = new TestBreakpoint();
        var first = new TestBreakpoint();
        var second = new TestBreakpoint { FailConfiguration = true };
        var third = new TestBreakpoint();

        Assert.Throws<ProxyException>(() => Replace(original, first, second, third));

        Assert.False(original.Deleted);
        Assert.All(new[] { first, second, third }, item => Assert.True(item.Deleted));
    }

    [Fact]
    public void SuccessfulReplacementPreservesIdentityAndEnabledStateBeforeDeletingOriginal()
    {
        var original = new TestBreakpoint { Tag = "original-id", Enabled = false };
        var first = new TestBreakpoint();
        var second = new TestBreakpoint();
        original.BeforeDelete = () =>
        {
            Assert.Equal("original-id", first.Tag);
            Assert.Equal("new-id", second.Tag);
            Assert.False(first.Enabled);
            Assert.False(second.Enabled);
        };

        var result = Replace(original, first, second);

        Assert.Equal(2, result.Count);
        Assert.True(original.Deleted);
        Assert.False(first.Deleted);
        Assert.False(second.Deleted);
    }

    [Fact]
    public void FailedOriginalDeletionCleansPreparedReplacements()
    {
        var original = new TestBreakpoint { FailDelete = true };
        var replacement = new TestBreakpoint();
        var error = Assert.Throws<ProxyException>(() => Replace(original, replacement));

        Assert.False(original.Deleted);
        Assert.True(replacement.Deleted);
        Assert.True(error.Details.Value<bool>("originalDeleteAttempted"));
    }

    [Fact]
    public void CleanupFailureIsReportedWithoutSkippingOtherReplacements()
    {
        var original = new TestBreakpoint();
        var first = new TestBreakpoint { FailDelete = true };
        var second = new TestBreakpoint { FailConfiguration = true };
        var error = Assert.Throws<ProxyException>(() => Replace(original, first, second));

        Assert.False(original.Deleted);
        Assert.True(second.Deleted);
        Assert.Single((JArray)error.Details["cleanupErrors"]!);
    }

    private static IReadOnlyList<Breakpoint> Replace(TestBreakpoint original, params TestBreakpoint[] replacements)
    {
        var collection = new TestBreakpoints(replacements.Select(item => item.Instance).ToArray());
        return BreakpointReplacement.Replace(original.Instance, () => collection.Instance, () => "new-id");
    }

    private sealed class TestBreakpoints : RealProxy
    {
        private readonly Breakpoint[] _items;
        public Breakpoints Instance => (Breakpoints)GetTransparentProxy();

        public TestBreakpoints(Breakpoint[] items) : base(typeof(Breakpoints))
        {
            _items = items;
        }

        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            return call.MethodName == "GetEnumerator"
                ? new ReturnMessage(((IEnumerable)_items).GetEnumerator(), null, 0, call.LogicalCallContext, call)
                : new ReturnMessage(new NotSupportedException(call.MethodName), call);
        }
    }

    private sealed class TestBreakpoint : RealProxy
    {
        public string Tag { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public bool Deleted { get; private set; }
        public bool FailConfiguration { get; set; }
        public bool FailDelete { get; set; }
        public Action? BeforeDelete { get; set; }
        public Breakpoint Instance => (Breakpoint)GetTransparentProxy();

        public TestBreakpoint() : base(typeof(Breakpoint)) { }

        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            try
            {
                object? result = null;
                switch (call.MethodName)
                {
                    case "get_Tag":
                        result = Tag;
                        break;
                    case "get_Enabled":
                        result = Enabled;
                        break;
                    case "set_Tag":
                        Tag = (string)call.Args[0];
                        break;
                    case "set_Enabled":
                        if (FailConfiguration)
                        {
                            throw new InvalidOperationException("Configuration rejected.");
                        }

                        Enabled = (bool)call.Args[0];
                        break;
                    case "Delete":
                        BeforeDelete?.Invoke();
                        if (FailDelete)
                        {
                            throw new InvalidOperationException("Delete rejected.");
                        }

                        Deleted = true;
                        break;
                    default:
                        throw new NotSupportedException(call.MethodName);
                }

                return new ReturnMessage(result, null, 0, call.LogicalCallContext, call);
            }
            catch (Exception exception)
            {
                return new ReturnMessage(exception, call);
            }
        }
    }
}
