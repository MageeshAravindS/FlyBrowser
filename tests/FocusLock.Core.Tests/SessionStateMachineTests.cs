using System.Collections.Generic;
using FocusLock.Core;
using Xunit;

namespace FocusLock.Core.Tests;

public class SessionStateMachineTests
{
    [Fact]
    public void InitialState_IsIdle()
    {
        var sm = new SessionStateMachine();
        Assert.Equal(SessionState.Idle, sm.CurrentState);
        Assert.Equal(0, sm.FocusLossCount);
    }

    [Fact]
    public void ValidTransitions_ChangeStateAndRaiseEvent()
    {
        var sm = new SessionStateMachine();
        var raisedEvents = new List<StateChangedEvent>();
        sm.StateChanged += (s, e) => raisedEvents.Add(e);

        Assert.True(sm.TransitionTo(SessionState.Launching, "Start launch"));
        Assert.Equal(SessionState.Launching, sm.CurrentState);

        Assert.True(sm.TransitionTo(SessionState.Active, "Loaded URL"));
        Assert.Equal(SessionState.Active, sm.CurrentState);

        Assert.Equal(2, raisedEvents.Count);
        Assert.Equal(SessionState.Idle, raisedEvents[0].OldState);
        Assert.Equal(SessionState.Launching, raisedEvents[0].NewState);
    }

    [Fact]
    public void InvalidTransition_ReturnsFalse()
    {
        var sm = new SessionStateMachine();
        Assert.False(sm.TransitionTo(SessionState.Terminated, "Direct terminated without active state"));
        Assert.Equal(SessionState.Idle, sm.CurrentState);
    }

    [Fact]
    public void RegisterFocusLoss_TriggersWarningThenTermination()
    {
        var sm = new SessionStateMachine(warningThreshold: 2, terminationThreshold: 3);
        sm.TransitionTo(SessionState.Launching, "Init");
        sm.TransitionTo(SessionState.Active, "Ready");

        sm.RegisterFocusLoss("Window1");
        Assert.Equal(1, sm.FocusLossCount);
        Assert.Equal(SessionState.Active, sm.CurrentState);

        sm.RegisterFocusLoss("Window2");
        Assert.Equal(2, sm.FocusLossCount);
        Assert.Equal(SessionState.Warning, sm.CurrentState);

        sm.RegisterFocusLoss("Window3");
        Assert.Equal(3, sm.FocusLossCount);
        Assert.Equal(SessionState.Terminated, sm.CurrentState);
    }

    [Fact]
    public void AuthorizeExit_FromTerminatedState_TransitionsToExited()
    {
        var sm = new SessionStateMachine(warningThreshold: 1, terminationThreshold: 1);
        sm.TransitionTo(SessionState.Launching, "Init");
        sm.TransitionTo(SessionState.Active, "Ready");
        sm.RegisterFocusLoss("Cheat App");

        Assert.Equal(SessionState.Terminated, sm.CurrentState);
        Assert.True(sm.AuthorizeExit("Proctor entered valid password"));
        Assert.Equal(SessionState.Exited, sm.CurrentState);
    }
}
