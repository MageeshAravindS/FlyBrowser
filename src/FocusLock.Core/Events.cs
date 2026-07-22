using System;

namespace FocusLock.Core;

public record FocusLostEvent(DateTime Timestamp, string LostWindowTitle, int CurrentCount);

public record FocusRestoredEvent(DateTime Timestamp);

public record NavigationEvent(DateTime Timestamp, string Url, bool IsAllowed, string? Reason = null);

public record DownloadBlockedEvent(DateTime Timestamp, string Url, string SuggestedFilename);

public record PopupBlockedEvent(DateTime Timestamp, string TargetUrl);

public record StateChangedEvent(SessionState OldState, SessionState NewState, string Reason, DateTime Timestamp);

public record ConfigErrorEvent(DateTime Timestamp, string ErrorDetails);
