using System;
using System.Text;
using System.Threading;
using FluentAssertions;
using NatsWebSocket.Protocol;
using NatsWebSocket.Subscriptions;
using Xunit;

namespace NatsWebSocket.Tests.Subscriptions;

public class SubscriptionManagerTests
{
    [Fact]
    public void NextSid_ReturnsIncrementingValues()
    {
        var manager = new SubscriptionManager();

        var sid1 = manager.NextSid();
        var sid2 = manager.NextSid();
        var sid3 = manager.NextSid();

        sid1.Should().Be("1");
        sid2.Should().Be("2");
        sid3.Should().Be("3");
    }

    [Fact]
    public void Add_CreatesSubscriptionState()
    {
        var manager = new SubscriptionManager();
        var called = false;

        var state = manager.Add("test.subject", null, msg => called = true);

        state.Should().NotBeNull();
        state.Subject.Should().Be("test.subject");
        state.QueueGroup.Should().BeNull();
        state.IsActive.Should().BeTrue();
        manager.Count.Should().Be(1);
    }

    [Fact]
    public void Add_WithQueueGroup_PreservesQueueGroup()
    {
        var manager = new SubscriptionManager();
        var state = manager.Add("test.subject", "workers", msg => { });

        state.QueueGroup.Should().Be("workers");
    }

    [Fact]
    public void Remove_DeactivatesSubscription()
    {
        var manager = new SubscriptionManager();
        var state = manager.Add("test.subject", null, msg => { });

        manager.Remove(state.Sid);

        state.IsActive.Should().BeFalse();
        manager.Count.Should().Be(0);
    }

    [Fact]
    public void Remove_NonexistentSid_DoesNotThrow()
    {
        var manager = new SubscriptionManager();
        Action act = () => manager.Remove("999");
        act.Should().NotThrow();
    }

    [Fact]
    public void TryGet_ExistingSubscription_ReturnsTrue()
    {
        var manager = new SubscriptionManager();
        var state = manager.Add("test.subject", null, msg => { });

        manager.TryGet(state.Sid, out var found).Should().BeTrue();
        found.Should().BeSameAs(state);
    }

    [Fact]
    public void TryGet_NonexistentSid_ReturnsFalse()
    {
        var manager = new SubscriptionManager();
        manager.TryGet("999", out _).Should().BeFalse();
    }

    [Fact]
    public void Dispatch_MatchingSid_InvokesHandler()
    {
        var manager = new SubscriptionManager();
        var received = new ManualResetEventSlim(false);
        NatsMsg receivedMsg = null;

        var state = manager.Add("test.subject", null, msg =>
        {
            receivedMsg = msg;
            received.Set();
        });

        var parsed = new ParsedMsg
        {
            Command = "MSG",
            Subject = "test.subject",
            Sid = state.Sid,
            Payload = Encoding.UTF8.GetBytes("hello")
        };

        manager.Dispatch(parsed);

        received.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        receivedMsg.Should().NotBeNull();
        receivedMsg.Subject.Should().Be("test.subject");
        receivedMsg.GetString().Should().Be("hello");
    }

    [Fact]
    public void Dispatch_WithHeaders_ParsesHeaders()
    {
        var manager = new SubscriptionManager();
        var received = new ManualResetEventSlim(false);
        NatsMsg receivedMsg = null;

        var state = manager.Add("test.subject", null, msg =>
        {
            receivedMsg = msg;
            received.Set();
        });

        var hdrStr = "NATS/1.0\r\nX-Custom: value\r\n\r\n";
        var parsed = new ParsedMsg
        {
            Command = "HMSG",
            Subject = "test.subject",
            Sid = state.Sid,
            HeaderBytes = Encoding.UTF8.GetBytes(hdrStr),
            Payload = Encoding.UTF8.GetBytes("{}")
        };

        manager.Dispatch(parsed);

        received.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        receivedMsg.Headers.Should().NotBeNull();
        receivedMsg.Headers.GetFirst("X-Custom").Should().Be("value");
    }

    [Fact]
    public void Dispatch_NonMatchingSid_DoesNotInvokeHandler()
    {
        var manager = new SubscriptionManager();
        var called = false;

        manager.Add("test.subject", null, msg => called = true);

        var parsed = new ParsedMsg
        {
            Command = "MSG",
            Subject = "test.subject",
            Sid = "999", // non-matching SID
            Payload = Array.Empty<byte>()
        };

        manager.Dispatch(parsed);
        Thread.Sleep(100);

        called.Should().BeFalse();
    }

    [Fact]
    public void GetResubscribeCommands_ReturnsCommandsForActiveSubscriptions()
    {
        var manager = new SubscriptionManager();
        manager.Add("sub1", null, msg => { });
        var state2 = manager.Add("sub2", "q1", msg => { });
        manager.Add("sub3", null, msg => { });

        // Remove sub3
        manager.Remove("3");

        var commands = manager.GetResubscribeCommands();

        commands.Should().HaveCount(2);
        var cmdStrings = commands.ConvertAll(c => Encoding.UTF8.GetString(c));

        cmdStrings.Should().Contain(s => s.Contains("SUB sub1"));
        cmdStrings.Should().Contain(s => s.Contains("SUB sub2 q1"));
    }

    [Fact]
    public void Dispatch_HandlerThrows_InvokesErrorCallback()
    {
        var errorReceived = new ManualResetEventSlim(false);
        Exception receivedException = null;

        var manager = new SubscriptionManager(ex =>
        {
            receivedException = ex;
            errorReceived.Set();
        });

        var state = manager.Add("test.subject", null, msg =>
        {
            throw new InvalidOperationException("handler error");
        });

        var parsed = new ParsedMsg
        {
            Command = "MSG",
            Subject = "test.subject",
            Sid = state.Sid,
            Payload = Encoding.UTF8.GetBytes("hello")
        };

        manager.Dispatch(parsed);

        errorReceived.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        receivedException.Should().BeOfType<InvalidOperationException>();
        receivedException.Message.Should().Be("handler error");
    }
}
