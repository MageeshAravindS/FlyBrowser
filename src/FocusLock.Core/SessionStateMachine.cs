using System;

namespace FocusLock.Core;

public class SessionStateMachine
{
    private readonly object _lock = new();

    public SessionState CurrentState { get; private set; } = SessionState.Idle;
    public int FocusLossCount { get; private set; } = 0;
    public int WarningThreshold { get; private set; } = 1;
    public int TerminationThreshold { get; private set; } = 3;
    public int FocusLossDebounceMs { get; private set; } = 250;
    public string SessionId { get; private set; } = Guid.NewGuid().ToString("N");

    public event EventHandler<StateChangedEvent>? StateChanged;
    public event EventHandler<FocusLostEvent>? FocusLost;
    public event EventHandler<FocusRestoredEvent>? FocusRestored;

    public SessionStateMachine(int warningThreshold = 1, int terminationThreshold = 3, int focusLossDebounceMs = 250)
    {
        WarningThreshold = Math.Max(1, warningThreshold);
        TerminationThreshold = Math.Max(WarningThreshold, terminationThreshold);
        FocusLossDebounceMs = Math.Max(0, focusLossDebounceMs);
    }

    public bool TransitionTo(SessionState newState, string reason)
    {
        lock (_lock)
        {
            if (!IsValidTransition(CurrentState, newState))
            {
                return false;
            }

            var oldState = CurrentState;
            CurrentState = newState;

            StateChanged?.Invoke(this, new StateChangedEvent(oldState, newState, reason, DateTime.UtcNow));
            return true;
        }
    }

    public void RegisterFocusLoss(string lostWindowTitle = "Unknown Window")
    {
        lock (_lock)
        {
            if (CurrentState != SessionState.Active && CurrentState != SessionState.Warning)
            {
                return;
            }

            FocusLossCount++;
            var focusEvent = new FocusLostEvent(DateTime.UtcNow, lostWindowTitle, FocusLossCount);
            FocusLost?.Invoke(this, focusEvent);

            if (FocusLossCount >= TerminationThreshold)
            {
                TransitionTo(SessionState.Terminated, $"Focus loss threshold reached ({FocusLossCount}/{TerminationThreshold}). Window that lost focus: '{lostWindowTitle}'");
            }
            else if (FocusLossCount >= WarningThreshold)
            {
                if (CurrentState == SessionState.Active)
                {
                    TransitionTo(SessionState.Warning, $"Focus loss warning ({FocusLossCount}/{TerminationThreshold}).");
                }
                else
                {
                    StateChanged?.Invoke(this, new StateChangedEvent(CurrentState, SessionState.Warning, $"Focus loss warning ({FocusLossCount}/{TerminationThreshold}).", DateTime.UtcNow));
                }
            }
        }
    }

    public void RegisterFocusRestored()
    {
        lock (_lock)
        {
            if (CurrentState == SessionState.Active || CurrentState == SessionState.Warning)
            {
                FocusRestored?.Invoke(this, new FocusRestoredEvent(DateTime.UtcNow));
            }
        }
    }

    public void ResetSession()
    {
        lock (_lock)
        {
            FocusLossCount = 0;
            CurrentState = SessionState.Idle;
            SessionId = Guid.NewGuid().ToString("N");
        }
    }

    public bool AuthorizeExit(string reason = "Proctor exit authenticated")
    {
        lock (_lock)
        {
            return TransitionTo(SessionState.Exited, reason);
        }
    }

    public bool CompleteExam(string reason = "Exam naturally completed")
    {
        lock (_lock)
        {
            return TransitionTo(SessionState.Completed, reason);
        }
    }

    public bool ReportError(string errorDetails)
    {
        lock (_lock)
        {
            return TransitionTo(SessionState.Error, errorDetails);
        }
    }

    private static bool IsValidTransition(SessionState from, SessionState to)
    {
        if (from == to) return false;

        return (from, to) switch
        {
            (SessionState.Idle, SessionState.Launching) => true,
            (SessionState.Idle, SessionState.Error) => true,
            (SessionState.Launching, SessionState.Active) => true,
            (SessionState.Launching, SessionState.Error) => true,
            (SessionState.Active, SessionState.Warning) => true,
            (SessionState.Active, SessionState.Terminated) => true,
            (SessionState.Active, SessionState.Completed) => true,
            (SessionState.Active, SessionState.Error) => true,
            (SessionState.Warning, SessionState.Active) => true,
            (SessionState.Warning, SessionState.Terminated) => true,
            (SessionState.Warning, SessionState.Completed) => true,
            (SessionState.Warning, SessionState.Error) => true,
            (SessionState.Terminated, SessionState.Idle) => true,
            (SessionState.Terminated, SessionState.Exited) => true,
            (SessionState.Completed, SessionState.Exited) => true,
            (SessionState.Error, SessionState.Exited) => true,
            (SessionState.Active, SessionState.Exited) => true,
            (SessionState.Warning, SessionState.Exited) => true,
            (SessionState.Launching, SessionState.Exited) => true,
            _ => false
        };
    }
}
