# FlyBrowser — Software Architecture Document

**Version:** 1.0 (Pre-Development Draft)
**Author:** Lead Architect
**Status:** For Review — Architectural Decisions Pending Sign-off
**Target Platforms:** Windows 10 (21H2+), Windows 11

---

## 1. Executive Summary

FlyBrowser is a standalone Windows kiosk-mode browser purpose-built for
administering online exams. It renders a single, pre-configured exam URL in a
fullscreen, chrome-less window built on a bundled Chromium engine, and
terminates the exam session if the window loses OS focus more than a
configurable number of times.

It occupies a deliberate middle ground between two existing categories of
product:

- **Safe Exam Browser (SEB)-class tools**: heavyweight, deeply invasive
  lockdown browsers that disable task switching, screenshotting, external
  monitors, virtual machines, etc., typically requiring elevated privileges
  and kernel-level or driver-level hooks.
- **Simple kiosk wrappers**: single-purpose Electron/WebView shells with no
  real integrity guarantees, easily bypassed with Alt+Tab.

FocusLock's design goal is **"honest deterrence, not fortress security."** It
assumes a cooperative-but-tempted test-taker on a standard, unmanaged Windows
machine, not a hostile actor with local admin and time to reverse-engineer the
binary. This framing has to be made explicit to the stakeholders before
development starts, because it changes almost every downstream decision —
threat model, privilege level, engine choice, and QA strategy.

The recommended stack is **CEF (Chromium Embedded Framework) via CefSharp.Wpf,
hosted in a .NET 8 WPF application**, run as a standard (non-admin) user
process, with a JSON-driven configuration and a structured, tamper-evident
session log. This plan also proposes challenging two of your stated
assumptions (see Section 4 and Section 15) — most importantly, that
"no administrator privileges required" and "SEB-like security" are in tension
with each other, and that tension must be resolved explicitly rather than
left implicit.

---

## 2. Functional Requirements

| ID | Requirement |
|----|-------------|
| FR-1 | Application launches directly into a fullscreen browser window displaying a single configured exam URL. |
| FR-2 | No address bar, tab strip, bookmarks bar, menu bar, or status bar is rendered. |
| FR-3 | Right-click context menu is disabled on all pages. |
| FR-4 | Browser DevTools (F12, Ctrl+Shift+I, view-source, etc.) are disabled. |
| FR-5 | File downloads initiated by the page are blocked or silently discarded. |
| FR-6 | New window / new tab / popup requests are intercepted and either blocked or redirected into the same single view, per policy. |
| FR-7 | Keyboard shortcuts that could break lockdown (Alt+Tab cannot be blocked at the OS level without a hook — see Section 15; Alt+F4, Ctrl+W, Ctrl+N, Ctrl+T, F11, Windows key combinations reachable in-process) are intercepted where technically possible. |
| FR-8 | The application monitors OS-level window focus (`WM_ACTIVATE` / `WM_SETFOCUS`/`WM_KILLFOCUS` equivalents) and increments a focus-loss counter each time the window loses foreground focus. |
| FR-9 | When focus-loss count exceeds a configurable threshold (default 3), the exam session is terminated: the page is navigated away or the app displays a termination screen, and the event is logged. |
| FR-10 | A grace/re-focus behavior is configurable: e.g., ignore focus loss under N milliseconds (to tolerate OS toast notifications) — see Section 4 assumption challenge. |
| FR-11 | All configuration (exam URL, threshold, timeouts, allowed domains, branding) is loaded from a JSON file at startup. |
| FR-12 | The application produces a structured session log (JSON Lines or similar) recording: session start/end, focus-loss events with timestamps, navigation events, termination reason, and app version. |
| FR-13 | The application displays a minimal, modern branded UI for pre-exam state (e.g., "Preparing exam environment…"), an in-exam state (just the web content), and a post-termination/completion state. |
| FR-14 | The application refuses to launch if the configured URL is unreachable, malformed, or fails an optional allow-list check, and shows a clear operator-facing error. |
| FR-15 | The application supports a documented, authenticated "exit lockdown" mechanism for proctors/administrators (e.g., a hidden keyboard shortcut + password, or a signed exit token) distinct from student-triggered termination. |
| FR-16 | Optional: single-instance enforcement — a second launch of FocusLock either focuses the existing instance or refuses to start. |

---

## 3. Non-Functional Requirements

| Category | Requirement |
|----------|-------------|
| **Performance** | Cold start to visible exam page in under 3 seconds on typical hardware (SSD, 4+ GB RAM). Memory footprint comparable to a single Chrome tab plus the CEF runtime (~150–300 MB baseline). |
| **Compatibility** | Windows 10 21H2+ and Windows 11, x64 (ARM64 as a stretch goal, see Risks). No dependency on WebView2 runtime or Edge being installed. |
| **Portability of deployment** | Should run from a single install (MSIX or NSIS/Inno installer) and ideally support a "portable" xcopy-deployable mode for lab machines without install rights. |
| **Reliability** | The app must not silently crash-lose the exam session; any crash must be logged and, where possible, recoverable to a defined safe state. |
| **Security** | See Section 15 — explicit threat model with in-scope and out-of-scope threats. |
| **Maintainability** | Codebase must be maintainable by a small team (1–3 engineers) over a 5-year horizon; must tolerate Chromium/CEF version upgrades without full rewrites. |
| **Observability** | Session logs must be sufficient for a support engineer to reconstruct "what happened" during a disputed exam session without needing screen recordings. |
| **Accessibility** | Minimal chrome UI should still respect Windows high-contrast/DPI scaling settings; exam content accessibility is the responsibility of the exam site itself. |
| **Localization** | UI strings (not exam content) should be externalized for future localization, even if only English ships in v1. |
| **Privilege level** | Must run as a standard user by default; any feature requiring elevation must be optional and clearly justified (see Section 15). |

---

## 4. Assumptions and Constraints — and Where I'm Pushing Back

**Stated assumptions accepted as-is:**

- Single exam URL per session; no multi-site navigation is a supported use case.
- Windows-only; no macOS/Linux requirement in v1.
- JSON is an acceptable configuration format (vs. YAML/TOML/registry).
- "No admin rights" is a hard product requirement, not just a nice-to-have.

**Assumptions I want to challenge before code is written:**

1. **"Fully secure lockdown browser like SEB" and "no admin privileges" are
   partially contradictory.** SEB's actual security value comes largely from
   OS-level enforcement it cannot get without elevated privileges or system
   policy: blocking Alt+Tab system-wide, blocking the Windows key, disabling
   other running processes, controlling multi-monitor setups, and in the
   Windows version, using low-level keyboard hooks and sometimes a kiosk
   Assigned Access profile (which itself requires admin/Group Policy to
   configure). **A non-admin, single-process Win32/WPF app can intercept
   keyboard input only while it holds foreground focus**, and cannot prevent
   the user from Alt+Tabbing away, opening Task Manager, or running a second
   monitor with cheat material, because Windows will always route Alt+Tab and
   Ctrl+Alt+Del to the shell, not to an unprivileged application. **My
   recommendation: reframe the security goal explicitly as "focus-loss
   detection and honest deterrence," not "prevention of a determined
   cheater."** This should be a documented, signed-off product decision, not
   a marketing gap discovered after launch. Section 15 formalizes this as the
   security model.

2. **"No admin privileges unless absolutely necessary" should be read as "no
   admin privileges, full stop, in v1."** I recommend committing to that as a
   hard constraint (not "unless necessary") so the team isn't tempted to add
   a low-level keyboard hook (which legitimately does need care but not
   necessarily admin — global hooks via `SetWindowsHookEx(WH_KEYBOARD_LL)`
   **do** work without elevation) or a kernel driver (which would break the
   promise entirely) mid-project. If deeper lockdown is ever required, it
   should ship as a clearly separate, optional "Managed Mode" product tier
   that explicitly requires admin/Group Policy, rather than silently
   escalating v1.

3. **"Configurable focus-loss threshold... exam session is terminated."**
   Terminating outright on the 4th focus loss is a blunt instrument that
   will generate support tickets for false positives (Windows security
   notifications, antivirus popups, a phone call overlay on a 2-in-1 device,
   OS focus-stealing bugs are common). I recommend the config support: (a) a
   minimum focus-loss duration before it counts ("ignore blur/focus flicker
   under 300ms"), and (b) a distinction between "warn" and "terminate"
   thresholds (e.g., warn at 2, terminate at 3) so the student gets one
   visible warning. This is a UX/fairness improvement, not scope creep, and
   should be in v1's config schema even if the warning UI is simple.

4. **"No dependency on WebView2"** — I want to confirm this is really a
   product requirement and not solvable by WebView2 plus a locked-down
   policy set. WebView2 is Chromium-based, ships with Windows 11 by default,
   and can achieve nearly all of FR-1 through FR-7 via its documented
   `CoreWebView2Settings` API (disable dev tools, context menu, downloads,
   status bar, etc.) with dramatically less packaging and maintenance
   burden. The stated constraint rules it out anyway (likely due to runtime
   version drift across student machines, corporate WebView2 removal
   policies, or wanting single-binary determinism), and I accept that as a
   fixed constraint — but it is the single most consequential decision in
   this document and should be explicitly re-confirmed by the stakeholder
   before Milestone 1 starts, because it roughly triples engineering and
   packaging effort versus a WebView2-based build (see Section 5).

I proceed through the rest of this document treating "bundled Chromium
engine, not WebView2, no admin rights, deterrence-not-fortress security" as
the confirmed constraints.

---

## 5. Technology Stack Comparison

| Engine | Basis | Language/Binding | Bundle Size | Kiosk Lockdown Support | Maintenance Burden | Verdict |
|---|---|---|---|---|---|---|
| **CEF (Chromium Embedded Framework)** | Full Chromium | C++ native; C# via CefSharp; also Java (JCEF), Python (cefpython, less maintained) | ~130–180 MB | Excellent — granular control over context menu, downloads, devtools, popups, key events via C++/C# handlers | Moderate — CefSharp lags upstream CEF by weeks to a couple of months; active community; large user base (many commercial kiosk/POS apps use it) | **Recommended** |
| **Qt WebEngine** | Chromium (via Qt's fork) | C++/QML, Python (PySide/PyQt) | ~150–200 MB + Qt runtime | Good — similar API surface to CEF for disabling features | Higher — full Qt dependency adds licensing complexity (LGPL vs commercial Qt), larger toolchain, less idiomatic on Windows-only targets, team needs Qt expertise | Viable alternative, not recommended here |
| **Ultralight** | WebKit-derived, custom lightweight renderer | C++/C#/Rust bindings | ~10–20 MB (its main selling point) | Limited — not full Chromium; JS engine (JavaScriptCore variant) and web-platform coverage lag significantly; WebRTC, modern CSS, WASM support inconsistent | Low binary size but high compatibility risk | **Rejected** — exam platforms increasingly use webcam/mic proctoring (WebRTC), modern JS frameworks, and WASM-based content; incompatibility here directly breaks the product's core purpose. |
| **Electron** | Full Chromium + Node.js runtime | JavaScript/TypeScript | ~150–220 MB | Good for UI-level restrictions (`BrowserWindow` kiosk flags, `webContents` event interception) | Low dev friction, huge ecosystem, but **Node.js integration is an added attack surface**: any XSS/JS-injection on the exam page has a much larger blast radius if `nodeIntegration`/`contextIsolation` are ever misconfigured, and the runtime is heavier and slower to cold-start than a native CEF/WPF host | Rejected as primary, viable as low-risk fallback (see below) |
| **WebView2 (Edge/Chromium)** | System Chromium runtime | C#/.NET, C++, WinRT | ~1–5 MB (relies on installed runtime) | Excellent, very low engineering cost | Lowest maintenance, but **violates the "no WebView2 dependency" constraint** and depends on a runtime the institution doesn't fully control | Excluded per stated constraint (see Section 4.4) |
| **Firefox/Gecko embedding** | Gecko | No mature embedding API remains (XULRunner deprecated) | N/A | Effectively unmaintained for embedding use cases | N/A | Rejected — not a realistic option in 2026 |

### Why not Electron, in more depth

Electron is the fastest path to an MVP and has excellent kiosk-mode
primitives (`kiosk: true`, `fullscreen: true`, disabling `webContents`
features). It is rejected as the *primary* recommendation for three reasons
specific to this product:

1. It ships a full Node.js runtime and IPC bridge by default; every
   `contextBridge`/preload script is a place a determined student could
   probe for an escape hatch (e.g., to open a real Explorer window or spawn
   a process) if the app is ever misconfigured, which is a materially larger
   attack surface than a CEF host with no Node exposed to the page at all.
2. Cold-start and idle memory are higher than a native WPF+CEF host, which
   matters on the lower-spec lab/school hardware this product is likely to
   run on.
3. Long-term maintenance of an Electron app pulls in the whole npm
   dependency graph (supply-chain risk), whereas a CefSharp/.NET app has a
   much smaller and more auditable dependency surface.

If the team's skill set is overwhelmingly JavaScript/TypeScript and .NET/C++
expertise is not available, Electron becomes a legitimate pragmatic
fallback — flag this explicitly as a staffing-driven decision point, not a
technical one.

---

## 6. Recommended Stack and Reasoning

**Recommendation: CefSharp.Wpf (CEF bound into .NET) on .NET 8, hosted in a
WPF application, targeting x64 Windows 10/11.**

Reasoning:

- **CEF gives full Chromium web-platform compatibility** (WebRTC, WASM,
  modern JS/CSS), which is a hard requirement for exam and proctoring sites,
  without the "not really Chromium" compatibility risk of Ultralight or the
  attack-surface cost of Electron.
- **CefSharp.Wpf specifically** (over raw C++ CEF) lets the team build in
  C#/.NET, which materially lowers the 5-year maintenance risk: hiring,
  onboarding, tooling, and debugging are all easier in .NET than in raw C++
  CEF client code, at the cost of trailing upstream CEF releases by a few
  weeks to months — an acceptable tradeoff for a lockdown browser that
  should *not* chase bleeding-edge Chromium anyway (stability > freshness
  here).
- **WPF** (over WinForms or raw Win32) gives a straightforward path to a
  "modern minimal UI" for the chrome-less states (loading screen, warning
  overlay, termination screen) using XAML, while CefSharp.Wpf's
  `ChromiumWebBrowser` control drops in as the main content host.
- **No admin privileges are required** to run a WPF+CefSharp app, to
  register a low-level keyboard hook via `SetWindowsHookEx`, or to go
  fullscreen — all of this is achievable as a standard user, which satisfies
  the stated constraint without compromise.
- **.NET 8** (LTS) gives a 3-year support window per release and a
  self-contained/single-file publish option, so the app can be deployed
  without requiring the .NET runtime to be pre-installed on lab machines —
  important since we've already ruled out depending on a pre-installed
  browser runtime (WebView2); we shouldn't reintroduce the same class of
  dependency via .NET.

**Rejected-but-documented alternative:** raw C++ CEF client (`cefclient`
architecture) — more control over low-level Chromium behaviors and a smaller
runtime footprint, but a significantly higher development and maintenance
cost for a small team, and no meaningful security benefit for this specific
threat model (see Section 15) to justify the cost. Revisit only if the
5-year roadmap adds requirements that CefSharp genuinely cannot expose
(unlikely for this feature set).

---

## 7. High-Level System Architecture

```
┌───────────────────────────────────────────────────────────────────┐
│                        FocusLock Browser.exe                        │
│                     (.NET 8 / WPF Host Process)                     │
│                                                                       │
│  ┌───────────────┐   ┌────────────────────┐   ┌──────────────────┐ │
│  │  App Shell /  │   │   Focus Monitor     │   │  Config Loader /  │ │
│  │  Window Mgr   │◄─►│   Service           │   │  Validator        │ │
│  │  (WPF, XAML)  │   │  (Win32 focus hooks)│   │  (JSON schema)     │ │
│  └───────┬───────┘   └─────────┬──────────┘   └────────┬───────────┘ │
│          │                     │                        │             │
│          ▼                     ▼                        ▼             │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │                     Session State Machine                       │ │
│  │  (Idle → Launching → Active → Warning → Terminated → Exited)    │ │
│  └───────┬───────────────────────────────────┬──────────────────────┘ │
│          │                                   │                        │
│          ▼                                   ▼                        │
│  ┌───────────────────┐               ┌────────────────────┐         │
│  │  Browser Host      │               │   Logging Service   │         │
│  │  (CefSharp.Wpf     │──events──────►│  (structured JSONL,  │         │
│  │   ChromiumWebBrowser│              │   rolling files)     │         │
│  │   + policy handlers)│              └────────────────────┘         │
│  └─────────┬──────────┘                                              │
│            │                                                          │
│            ▼                                                          │
│  ┌────────────────────┐                                              │
│  │ CEF / Chromium       │  (bundled runtime, out-of-proc renderer,     │
│  │ subprocess(es)       │   sandboxed per Chromium's own model)        │
│  └────────────────────┘                                              │
└───────────────────────────────────────────────────────────────────┘
```

---

## 8. Component Diagram (text form)

```
[Program.cs / App.xaml.cs]
        │ starts
        ▼
[ConfigService] --loads--> [config.json] --validates against--> [ConfigSchema]
        │
        ▼
[MainWindow (WPF)]
        │ hosts
        ▼
[ChromiumWebBrowser (CefSharp.Wpf)]
        │ wraps
        ▼
[BrowserPolicyHandlers]
    ├── IContextMenuHandler   → suppress context menu
    ├── IKeyboardHandler      → intercept devtools shortcuts, block combos
    ├── IDownloadHandler      → block/log download attempts
    ├── ILifeSpanHandler      → block popups/new windows
    ├── IRequestHandler       → optional domain allow-list enforcement
    └── IDisplayHandler       → suppress status bar / address changes

[FocusMonitorService]
    ├── subscribes to WM_ACTIVATE / Application.Deactivated
    ├── applies debounce window (config: focusLossDebounceMs)
    └── raises FocusLostEvent(count, timestamp)

[SessionStateMachine]
    ├── consumes FocusLostEvent, NavigationEvent, ConfigError, CrashEvent
    └── drives UI state + triggers termination

[LoggingService]
    ├── writes structured JSONL to %ProgramData%/FocusLock/logs or config path
    └── optional log signing/hash-chaining (see Section 12)

[ExitAuthorizationService]
    └── validates proctor exit sequence (password/token) → transitions to Exited
```

---

## 9. Module Breakdown and Responsibilities

| Module | Responsibility | Notes |
|---|---|---|
| **App Bootstrap** | Parse CLI args (e.g., `--config path`), initialize CEF runtime, wire up DI container, show splash/loading state. | Fails fast and visibly if CEF initialization fails. |
| **ConfigService** | Load, validate (JSON Schema), and expose strongly-typed configuration. Supports config reload only at next launch, not hot-reload (deliberate simplicity). | Owns default values and error messages for missing/invalid fields. |
| **MainWindow / Shell** | Own the WPF window: fullscreen, borderless, topmost (configurable), owns the visual states (loading/active/warning/terminated). | No window chrome; explicit `WindowStyle=None`, `WindowState=Maximized`, `Topmost=True`. |
| **BrowserHost** | Wrap `ChromiumWebBrowser`; wire all CefSharp handler interfaces to enforce lockdown behaviors (FR-1–FR-7). | Central place where "what the browser is allowed to do" is enforced. |
| **FocusMonitorService** | Detect and debounce OS-level focus transitions; expose events to the state machine. | Uses WPF `Application.Activated`/`Deactivated` plus a Win32 `WM_ACTIVATE` hook as a defense-in-depth double-check (WPF's events can be unreliable across some multi-monitor/RDP scenarios). |
| **SessionStateMachine** | Central orchestrator; the only component allowed to transition session state (see Section 18). | Pure logic, unit-testable in isolation from UI/browser. |
| **LoggingService** | Append-only structured logs; log rotation; optional hash-chaining for tamper evidence. | Should be injectable/mockable for testing. |
| **ExitAuthorizationService** | Gate the proctor-only exit path (FR-15). | Should not be reachable via any student-facing UI element. |
| **UpdateChecker (optional/future)** | Check for new app version; does not auto-update mid-exam. | Explicitly out of scope for v1 (see Section 24). |

---

## 10. Folder / Project Structure

```
FocusLockBrowser/
├── src/
│   ├── FocusLock.App/                  # WPF host application (entry point)
│   │   ├── App.xaml / App.xaml.cs
│   │   ├── MainWindow.xaml / .cs
│   │   ├── Views/
│   │   │   ├── LoadingView.xaml
│   │   │   ├── WarningOverlay.xaml
│   │   │   └── TerminatedView.xaml
│   │   └── Program.cs
│   ├── FocusLock.Browser/              # CefSharp integration layer
│   │   ├── BrowserHostControl.cs
│   │   ├── Handlers/
│   │   │   ├── ContextMenuHandler.cs
│   │   │   ├── KeyboardHandler.cs
│   │   │   ├── DownloadHandler.cs
│   │   │   ├── LifeSpanHandler.cs
│   │   │   ├── RequestHandler.cs
│   │   │   └── DisplayHandler.cs
│   ├── FocusLock.Focus/                # Focus monitoring
│   │   ├── FocusMonitorService.cs
│   │   └── Win32FocusInterop.cs
│   ├── FocusLock.Core/                 # State machine, domain models
│   │   ├── SessionStateMachine.cs
│   │   ├── SessionState.cs
│   │   └── Events/
│   ├── FocusLock.Config/               # Config loading & schema validation
│   │   ├── ConfigService.cs
│   │   ├── FocusLockConfig.cs
│   │   └── config.schema.json
│   ├── FocusLock.Logging/              # Structured logging
│   │   ├── LoggingService.cs
│   │   └── LogEvent.cs
│   └── FocusLock.Security/             # Exit authorization
│       └── ExitAuthorizationService.cs
├── tests/
│   ├── FocusLock.Core.Tests/
│   ├── FocusLock.Config.Tests/
│   └── FocusLock.Focus.Tests/
├── installer/
│   ├── FocusLock.wxs                   # WiX/MSIX packaging definitions
│   └── portable/                       # xcopy-deployable build output
├── docs/
│   ├── architecture.md                 # this document
│   ├── config-schema.md
│   └── operator-guide.md
├── config/
│   └── config.sample.json
└── FocusLockBrowser.sln
```

---

## 11. Configuration Design

- Single JSON file, path resolved in this order: `--config <path>` CLI arg →
  `config.json` next to the executable → `%ProgramData%\FocusLock\config.json`.
- Validated against a JSON Schema at startup; on failure, the app shows a
  clear, operator-facing error screen (not a silent crash) and writes the
  validation error to the log, then exits non-zero.
- Config is **read-once at launch**; no hot reload, to avoid mid-exam
  behavior changes — a deliberate simplicity/security choice.
- Sensitive fields (e.g., the proctor exit password) are stored as a hash,
  not plaintext, even though this is a relatively low-stakes secret.

---

## 12. Logging Design

- Format: **JSON Lines (JSONL)** — one JSON object per line, append-only,
  easy to parse with any tool, easy to stream.
- Location: configurable; default `%ProgramData%\FocusLock\logs\session-<timestamp>.jsonl`.
- Every log line includes: `timestamp (UTC, ISO-8601)`, `eventType`,
  `sessionId`, and event-specific payload.
- Event types: `SessionStarted`, `ConfigLoaded`, `NavigationStarted`,
  `NavigationCompleted`, `FocusLost`, `FocusRestored`, `WarningIssued`,
  `SessionTerminated`, `ProctorExit`, `CrashDetected`, `AppClosed`.
- **Tamper-evidence (recommended, optional in v1):** hash-chain each log line
  (`hash(line_n) = SHA256(line_n_content + hash(line_n-1))`) so a modified or
  deleted log entry is detectable during dispute review, without requiring a
  remote logging server. This is cheap to implement and materially increases
  the evidentiary value of the log for academic-integrity disputes.
- Logs are local-only in v1; optional remote log shipping (to an LMS or
  proctoring backend) is a documented future enhancement (Section 24), not
  v1 scope, to avoid taking on a network dependency and its own failure
  modes inside the exam session itself.

---

## 13. Browser Lifecycle

1. **Pre-init**: CEF runtime settings configured (cache path, log path,
   command-line switches to disable devtools at the Chromium level in
   addition to the CefSharp handler layer, defense-in-depth).
2. **Init**: `Cef.Initialize()` called once at app startup, before any
   `ChromiumWebBrowser` is constructed.
3. **Browser creation**: `ChromiumWebBrowser` created bound to the configured
   URL; all policy handlers attached before first navigation.
4. **Navigation guard**: `IRequestHandler.OnBeforeBrowse` enforces the
   optional domain allow-list (exam URL's origin + explicitly configured
   auxiliary origins, e.g., a proctoring SDK's domain) — anything outside
   the allow-list is blocked and logged.
5. **Active session**: browser renders exam content; all lockdown handlers
   remain active for the life of the session.
6. **Termination**: on state-machine transition to `Terminated`, browser is
   navigated to a local `about:blank`/embedded termination page (not left
   showing exam content), and further navigation is blocked.
7. **Shutdown**: `Cef.Shutdown()` called on app exit; must be called exactly
   once and only after all browser instances are disposed, per CefSharp's
   documented lifecycle requirements — a common source of shutdown crashes
   if done incorrectly, called out explicitly for the implementing team.

---

## 14. Focus Monitoring Lifecycle

1. On `MainWindow` load, `FocusMonitorService` subscribes to:
   - WPF `Application.Activated` / `Application.Deactivated` (primary signal)
   - A raw Win32 `WM_ACTIVATE` message hook on the window handle
     (defense-in-depth / cross-check, since WPF-level events have known edge
     cases with RDP sessions, some virtual-machine environments, and certain
     multi-monitor DPI configurations)
2. On deactivation: start a debounce timer (`focusLossDebounceMs`, default
   ~250ms) — if focus returns before the timer elapses, it's not counted
   (handles transient OS toast/notification focus steals).
3. If the debounce elapses without refocus: increment `focusLossCount`, emit
   `FocusLostEvent`, write a `FocusLost` log line with a timestamp and,
   where obtainable, the title of the window that stole focus (useful
   forensic detail for review, obtained via `GetForegroundWindow` +
   `GetWindowText`).
4. `SessionStateMachine` compares `focusLossCount` against
   `warningThreshold` and `terminationThreshold` from config and transitions
   state accordingly (see Section 18).
5. On refocus (`FocusRestored`), the app re-asserts topmost/fullscreen state
   in case the OS altered window placement.

---

## 15. Security Model

### What FocusLock Browser **does** protect against (in scope)

- Casual, opportunistic cheating: switching to a browser tab, chat app, or
  notes application, and forgetting the exam window is watching.
- Accidental or careless loss of exam integrity (e.g., a notification
  pulling focus and the student engaging with it).
- Basic in-page escape attempts: opening DevTools, right-click "view page
  source," downloading exam content, opening new tabs/windows from the page.
- Disputes over "what happened during the exam" — via tamper-evident,
  timestamped session logs.
- Navigation outside the intended exam domain(s) via the optional allow-list.

### What FocusLock Browser **explicitly does not** protect against (out of scope, by design)

- A user with local administrator rights or willingness to run a second,
  fully separate device (phone, second PC) to look up answers — no
  software-only, non-admin, single-process app can prevent this.
- Alt+Tab / Windows-key / Ctrl+Alt+Del at the OS level while the
  FocusLock window doesn't hold a low-level system hook with elevated
  privileges — we deliberately do **not** install such a hook (see
  Section 4, item 2), so a fast task-switch is detectable (it will register
  as a focus loss) but not preventable.
- Screen sharing / remote-viewing tools running as separate processes.
- Virtual machines, secondary monitors, or secondary devices used for
  lookup, unless a proctor is also visually supervising.
- A sufficiently technical user patching or debugging the FocusLock binary
  itself (e.g., attaching a debugger, hex-editing the config, replaying a
  captured log). We do not attempt binary obfuscation/anti-tamper in v1;
  that's a different, much larger engineering investment (code signing +
  integrity self-checks could be a v2 consideration, see Section 24) with
  diminishing returns against a determined attacker.
- Kernel-level or driver-level attacks; FocusLock has no kernel component.

### Explicit statement for the product/legal team

FocusLock Browser should be **marketed and documented as a focus-monitoring
and deterrence tool**, not as an unbypassable lockdown, to avoid setting an
expectation it architecturally cannot meet without violating the "no admin
rights" constraint. This is the single most important sentence in this
document for the non-engineering stakeholders to internalize.

---

## 16. UI/UX Flow

```
[App Launch]
     │
     ▼
[Loading Screen] ── "Preparing your exam environment…" (branding, spinner)
     │  (config validated, CEF initialized)
     ▼
[Active Exam View] ── fullscreen, chrome-less, exam website only
     │
     ├── focus lost (< warningThreshold) ─────► no visible UI change, logged silently
     │
     ├── focus lost (== warningThreshold) ────► [Warning Overlay]
     │        "Your exam session detected a focus loss (2 of 3 allowed).
     │         One more will end your session."  [Dismiss / Continue]
     │        └──► returns to [Active Exam View]
     │
     ├── focus lost (> terminationThreshold) ─► [Terminated Screen]
     │        "Your exam session has ended due to repeated focus loss.
     │         Please contact your proctor. Reference ID: <sessionId>"
     │        (no way back into the exam from here; app effectively locked
     │         until closed by proctor exit sequence)
     │
     └── exam naturally completed (site navigates to a configured
          "done" URL/pattern, if supported) ──► [Completion Screen]
              "Your exam has been submitted. You may close this window."
```

Design principles: no exam content is ever hidden behind FocusLock's own UI
except full-screen overlays at defined transition points; typography and
color should follow a simple, calm, "systems status" visual language
(avoid alarming red/siren styling for the warning state — reserve stronger
visual weight for the terminal state only).

---

## 17. Error Handling Strategy

| Failure | Handling |
|---|---|
| Invalid/missing config | Show a full-screen, operator-facing error before any browser UI renders; log; exit non-zero. Never fall back to silent defaults for the exam URL itself. |
| Exam URL unreachable at launch | Retry with backoff (configurable attempts), then show a clear "could not reach exam server" screen with a retry button; log every attempt. |
| CEF initialization failure | Distinct, specific error screen (likely indicates a corrupted install or unsupported OS/architecture); log full exception; exit non-zero. |
| Renderer process crash (a Chromium subprocess crashes, not the whole app) | CefSharp exposes a `RenderProcessTerminated` event — catch it, log it, and either auto-reload the page (with an on-screen notice) or transition to a "technical issue" state depending on config, rather than leaving a blank white screen. |
| Unexpected app crash | A top-level `AppDomain.UnhandledException` / `DispatcherUnhandledException` handler writes a best-effort crash log entry (including last known session state) before allowing termination, so the log can explain a mid-session disappearance. |
| Disk full / log write failure | Logging service fails soft (keeps an in-memory ring buffer, retries), never crashes the exam session over a logging failure — but surfaces a discreet operator warning if persistent. |
| Focus monitor hook failure (rare, e.g., blocked by security software) | Falls back to WPF-only focus events, logs a degraded-mode warning; app still functions, just with the redundancy removed. |

---

## 18. State Machine for the Application

```
                ┌────────┐
                │  Idle  │
                └───┬────┘
                    │ start()
                    ▼
             ┌─────────────┐
             │  Launching   │──(config/CEF error)──► Error/Exit
             └──────┬──────┘
                    │ browser ready + navigation success
                    ▼
             ┌─────────────┐
      ┌─────►│   Active     │
      │      └──────┬──────┘
      │             │ focusLossCount == warningThreshold
      │             ▼
      │      ┌─────────────┐
      └──────┤   Warning    │  (dismiss / auto-timeout back to Active)
   refocus   └──────┬──────┘
   or dismiss        │ focusLossCount > terminationThreshold
                     ▼
              ┌─────────────┐
              │ Terminated   │──(proctor exit auth success)──► Exited
              └──────┬──────┘
                     │ exam site reports natural completion
                     ▼
              ┌─────────────┐
              │  Completed   │──(user/proctor closes app)──► Exited
              └─────────────┘
```

Notes:
- `Terminated` and `Completed` are both terminal-for-the-student states; only
  `ExitAuthorizationService` can move the app out of `Terminated` (to allow a
  proctor to reset the machine for the next student), and only via an
  explicit authorized action, never automatically.
- The state machine is implemented as a pure, deterministic component
  (input: current state + event → output: new state + side-effect list),
  independently unit-testable from the UI and browser layers.

---

## 19. Configuration Schema

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "FocusLockConfig",
  "type": "object",
  "required": ["examUrl", "focusMonitoring"],
  "properties": {
    "examUrl": {
      "type": "string",
      "format": "uri",
      "description": "The single exam URL to load."
    },
    "allowedDomains": {
      "type": "array",
      "items": { "type": "string" },
      "description": "Optional allow-list of origins the browser may navigate to (e.g., proctoring SDK domains). Defaults to the examUrl's origin only."
    },
    "focusMonitoring": {
      "type": "object",
      "required": ["terminationThreshold"],
      "properties": {
        "warningThreshold": { "type": "integer", "minimum": 1, "default": 2 },
        "terminationThreshold": { "type": "integer", "minimum": 1, "default": 3 },
        "focusLossDebounceMs": { "type": "integer", "minimum": 0, "default": 250 }
      }
    },
    "ui": {
      "type": "object",
      "properties": {
        "branding": {
          "type": "object",
          "properties": {
            "appName": { "type": "string", "default": "FlyLock Browser" },
            "logoPath": { "type": "string" },
            "accentColor": { "type": "string", "default": "#2D6CDF" }
          }
        },
        "topmost": { "type": "boolean", "default": true }
      }
    },
    "logging": {
      "type": "object",
      "properties": {
        "logDirectory": { "type": "string" },
        "hashChain": { "type": "boolean", "default": true }
      }
    },
    "exitAuthorization": {
      "type": "object",
      "properties": {
        "passwordHash": { "type": "string" },
        "keySequence": {
          "type": "string",
          "default": "Ctrl+Alt+Shift+Q"
        }
      }
    },
    "network": {
      "type": "object",
      "properties": {
        "connectTimeoutMs": { "type": "integer", "default": 10000 },
        "retryAttempts": { "type": "integer", "default": 3 }
      }
    }
  }
}
```

Sample instance provided in `config/config.sample.json` in the project
structure.

---

## 20. Data Flow Between Components

```
config.json ─► ConfigService ─► FocusLockConfig (typed) ─┬─► BrowserHost (URL, allow-list)
                                                          ├─► FocusMonitorService (thresholds, debounce)
                                                          ├─► MainWindow (branding, topmost)
                                                          └─► LoggingService (log path, hash-chain flag)

FocusMonitorService ─(FocusLostEvent)─► SessionStateMachine ─(StateChanged)─► MainWindow (render state)
                                                              └─(LogRequest)──► LoggingService

BrowserHost ─(NavigationEvent / DownloadBlocked / PopupBlocked)─► SessionStateMachine
                                                                  └─(LogRequest)──► LoggingService

ExitAuthorizationService ─(AuthSuccess)─► SessionStateMachine ─► Exited
```

All cross-component communication flows through typed events into the
`SessionStateMachine` — no component other than the state machine is allowed
to directly force a UI state transition, which keeps the system's behavior
centrally reasoned-about and testable.

---

## 21. Build and Packaging Strategy

- **Runtime**: .NET 8, published `self-contained`, `win-x64` (and `win-arm64`
  as a stretch goal — see Risks), so no .NET runtime install is required on
  target machines.
- **CEF binaries**: bundled via the CefSharp NuGet packages'
  redistributables, included in the publish output; verify license
  compliance (CEF/Chromium is BSD-licensed, generally permissive, but the
  bundled binary redistribution terms should be reviewed by legal once,
  not per-release).
- **Packaging formats**:
  - **MSIX** for managed lab/institutional deployment (supports
    Intune/Group Policy distribution, clean uninstall, per-user install
    without admin).
  - **Portable ZIP / xcopy build** for BYOD or unmanaged-machine scenarios
    where even MSIX install isn't desired — this directly serves the "no
    admin required" goal and should be a first-class output of CI, not an
    afterthought.
- **Code signing**: the executable and installer should be Authenticode
  signed to avoid SmartScreen friction and to support the log
  tamper-evidence story (a signed binary is a precondition for anyone
  trusting the logs it produces).
- **CI/CD**: build matrix produces both packaging outputs on every tagged
  release; automated smoke test (launch, load a test page, verify no
  console/devtools, verify focus-loss counter increments) runs against the
  packaged artifact, not just in dev.
- **Versioning**: semantic versioning; the app version and the bundled CEF/
  Chromium version are both recorded in every session log for support
  triage.

---

## 22. Testing Strategy

**Unit tests**
- `SessionStateMachine`: every transition, including edge cases (rapid
  focus loss/restore, threshold-boundary values, out-of-order events).
- `ConfigService`: schema validation success/failure paths, default value
  application, malformed JSON handling.
- `LoggingService`: log line formatting, hash-chain correctness, rotation.

**Integration tests**
- `BrowserHost` + real CefSharp instance (headless where possible) verifying
  policy handlers actually suppress context menu / devtools / downloads /
  popups against a local test HTML fixture.
- `FocusMonitorService` against a real WPF window in a test harness,
  simulating focus/blur via `SetForegroundWindow` on a companion test
  window.
- End-to-end config → launch → navigate → terminate flow against a local
  mock exam site.

**Manual / exploratory QA**
- Multi-monitor configurations (focus loss across monitors, DPI scaling
  differences).
- RDP and virtual-machine environments (known to have flaky focus-event
  delivery — call out any degraded-mode behavior).
- Screen readers / accessibility tools' interaction with focus (avoid
  accidentally penalizing assistive-tech users — a real fairness/legal
  concern worth a dedicated test pass and possibly a documented
  accommodation path, e.g., a proctor-set exemption flag in config).
- Antivirus/EDR interaction with the low-level keyboard hook (some security
  suites flag `SetWindowsHookEx(WH_KEYBOARD_LL)` usage; test against at
  least Windows Defender and one common third-party EDR).
- Network interruption mid-exam (Wi-Fi drop, captive portal).
- Log tamper-evidence: manually edit a log file and verify the hash-chain
  detects it.

---

## 23. Risks and Tradeoffs

| Risk | Impact | Mitigation |
|---|---|---|
| Stakeholders/marketing describe this as "unbypassable" despite the documented security model | Reputational/legal exposure if a student demonstrates a bypass | Section 15 language should be reviewed and signed off by product/legal before launch; user-facing docs should match engineering reality |
| CEF/CefSharp version lag introduces a known Chromium CVE window | Security exposure if exam sites or ad-adjacent content are ever loaded | Establish a recurring (e.g., monthly) dependency-update cadence as a maintenance SLA, not ad hoc |
| Focus-event delivery is unreliable in RDP/VM/thin-client environments common in some school computer labs | False positives/negatives in focus-loss counting | Explicit degraded-mode detection + documented known-limitations list per environment; consider an environment-detection warning at launch |
| Low-level keyboard hook flagged by antivirus/EDR as suspicious | Support burden, failed launches in managed environments | Code-sign binary; publish a known-good hash/allow-list guidance doc for IT admins; test against major EDR vendors pre-launch |
| ARM64 Windows devices (increasingly common, e.g., Copilot+ PCs) | App unusable or emulated (Chromium under x64 emulation works but is slower) | Track as a stretch goal (Section 21); do not block v1 launch on it but flag it in the roadmap |
| Accessibility/assistive-tech false-positive focus loss | Fairness/legal concern for students using screen readers or alternative input | Manual QA pass + documented per-student exemption flag in config, reviewed with accessibility stakeholders |
| Single-process architecture means a renderer crash is more visible to the user than in a multi-tab browser (no other tab to fall back to) | Perceived reliability issue | Explicit crash-recovery UX (Section 17) rather than a blank screen |
| No admin rights means the app cannot fully suppress Alt+Tab/Windows key | Sets a security expectation gap versus SEB | Explicit scoping in Section 15, reinforced in all product materials |

---

## 24. Future Enhancements (explicitly out of v1 scope)

- **"Managed Mode" tier**: an optional, clearly-separated add-on that *does*
  require admin/Group Policy (e.g., Windows Assigned Access integration,
  disabling Task Manager, blocking secondary monitors) for institutions that
  accept that tradeoff — kept fully separate from the core product's "no
  admin" promise.
- Remote log shipping to an LMS/proctoring backend, with offline queuing.
- Webcam/microphone proctoring integration hooks (as a page-level
  permission the exam site itself requests through the browser, not a
  FocusLock feature per se, but the `IRequestHandler`/permission-prompt
  policy should be designed with this in mind now).
- Binary integrity self-check / anti-tamper (checksum verification of the
  installed app against a signed manifest at launch).
- Central configuration management console for institutions deploying to
  many machines (push config, view aggregated logs).
- ARM64 native build.
- Localization of UI strings beyond English.
- Auto-update mechanism (deliberately excluded from v1 to avoid any
  mid-semester behavior change risk — updates should be an IT-driven,
  out-of-session action).

---

## 25. Development Roadmap and Milestones

### Milestone 0 — Architectural Sign-off (no code)
- **Goal**: Get explicit stakeholder agreement on the security-model
  reframing (Section 15) and the four challenged assumptions (Section 4)
  before any implementation begins.
- **Deliverables**: this document, reviewed and signed off; a one-page
  "what this product does and does not protect against" doc approved by
  product/legal.
- **Dependencies**: none.
- **Risks**: skipping this step risks a mid-project scope fight when
  someone realizes Alt+Tab can't be fully blocked.
- **Definition of Done**: written sign-off from product owner on Sections 4
  and 15.
- **Estimated effort**: 3–5 days (meetings/review, not engineering).

### Milestone 1 — Core Shell + CEF Integration
- **Goal**: A fullscreen, chrome-less WPF window that loads a hardcoded URL
  via CefSharp with context menu, devtools, downloads, and popups disabled.
- **Deliverables**: `FocusLock.App` + `FocusLock.Browser` projects; manual
  demo of FR-1 through FR-7.
- **Dependencies**: Milestone 0.
- **Risks**: CefSharp/.NET 8 compatibility issues (verify early — CefSharp
  release cadence vs. .NET version support should be checked before
  committing to .NET 8 specifically).
- **Definition of Done**: app launches fullscreen to a test page; all
  FR-1–FR-7 manually verified; automated integration test suite covering
  policy handlers passes in CI.
- **Estimated effort**: 2–3 weeks (1 engineer).

### Milestone 2 — Configuration System
- **Goal**: Replace hardcoded values with the JSON config system (Section
  11/19).
- **Deliverables**: `FocusLock.Config` project, schema, sample config,
  validation error UI.
- **Dependencies**: Milestone 1.
- **Risks**: low.
- **Definition of Done**: app refuses to launch on invalid config with a
  clear error; all documented config fields are respected; unit tests for
  schema validation pass.
- **Estimated effort**: 1 week.

### Milestone 3 — Focus Monitoring + State Machine
- **Goal**: Implement `FocusMonitorService` and `SessionStateMachine`
  end-to-end, including debounce, warning, and termination flows.
- **Deliverables**: `FocusLock.Focus`, `FocusLock.Core` projects; warning
  overlay and terminated-screen UI.
- **Dependencies**: Milestones 1–2.
- **Risks**: focus-event reliability across environments (Section 22); this
  is the highest-uncertainty milestone and should get the most manual QA
  time across multi-monitor/RDP/VM setups.
- **Definition of Done**: focus-loss counting is correct and debounced
  across at least three tested environments (physical multi-monitor,
  single monitor, RDP session); state machine unit tests cover all
  transitions; termination flow verified end-to-end.
- **Estimated effort**: 2–3 weeks.

### Milestone 4 — Logging System
- **Goal**: Structured, tamper-evident session logging.
- **Deliverables**: `FocusLock.Logging` project; hash-chain implementation;
  log rotation.
- **Dependencies**: Milestone 3 (needs real events to log).
- **Risks**: low; main risk is performance if logging is on the hot path of
  frequent events — mitigate with async/buffered writes.
- **Definition of Done**: every event type in Section 12 is logged
  correctly; tamper-evidence verified by a manual "edit a log line, confirm
  detection" test; log writes never block the UI thread.
- **Estimated effort**: 1 week.

### Milestone 5 — Proctor Exit + Session Completion
- **Goal**: Implement `ExitAuthorizationService` and the natural-completion
  detection path.
- **Deliverables**: authenticated exit flow; completion screen; config
  fields for exit credentials.
- **Dependencies**: Milestone 3.
- **Risks**: exit credential storage — even though it's a low-stakes secret,
  don't store it in plaintext (Section 11).
- **Definition of Done**: a proctor can exit a terminated session with the
  correct credential and cannot with an incorrect one; attempt is logged
  either way.
- **Estimated effort**: 3–5 days.

### Milestone 6 — Packaging and Deployment
- **Goal**: Produce both MSIX and portable-ZIP build artifacts via CI.
- **Deliverables**: installer scripts, CI pipeline, code-signing
  integration, operator/IT deployment guide.
- **Dependencies**: Milestones 1–5 functionally complete.
- **Risks**: code-signing certificate procurement can be a lead-time item —
  start this in parallel with Milestone 1, not at Milestone 6.
- **Definition of Done**: a clean Windows 10 and a clean Windows 11 VM can
  install (MSIX) or unzip-and-run (portable) the app with no admin rights
  and no pre-installed dependencies, and successfully complete a full test
  exam session.
- **Estimated effort**: 1–2 weeks.

### Milestone 7 — Hardening, Accessibility, and QA Pass
- **Goal**: Execute the full manual QA matrix (Section 22), fix
  environment-specific issues, and address the accessibility fairness
  question (Section 23).
- **Deliverables**: QA report; accessibility exemption flag implemented if
  required by stakeholder decision; antivirus/EDR compatibility notes.
- **Dependencies**: Milestone 6.
- **Risks**: this milestone commonly uncovers scope that pushes the
  timeline — budget contingency here rather than in earlier milestones.
- **Definition of Done**: QA matrix fully executed with sign-off; no P0/P1
  defects open; documented known-limitations list finalized for release
  notes.
- **Estimated effort**: 2 weeks.

### Milestone 8 — Pilot Release
- **Goal**: Limited real-world pilot with a single institution/course
  before general availability.
- **Deliverables**: pilot deployment, monitored session logs, post-pilot
  retrospective.
- **Dependencies**: Milestone 7.
- **Risks**: real-world environments always surface something the lab
  didn't (network conditions, unusual hardware, actual student behavior).
- **Definition of Done**: pilot completed with no unresolved P0 issues;
  retrospective document produced feeding into the v1.1 backlog.
- **Estimated effort**: 1–2 weeks elapsed (low active engineering effort,
  mostly monitoring/support).

**Total estimated engineering effort (1–2 engineers): roughly 12–16 weeks**
from architectural sign-off to pilot-ready release, excluding the pilot's
elapsed calendar time.

---

## Architectural Decisions to Finalize Before Writing Any Code

1. **Security-model framing** (Section 15) — signed off as "deterrence, not
   fortress," explicitly ruling out kernel hooks, admin elevation, and
   claims of unbypassability.
2. **CEF via CefSharp.Wpf on .NET 8** as the confirmed engine/framework
   choice, with the Electron and Qt WebEngine alternatives formally
   rejected and documented (not left as an open question the team
   re-litigates mid-project).
3. **WebView2 exclusion re-confirmed** by the stakeholder with awareness of
   its ~3x lower engineering cost, given it's the most consequential and
   most reversible-if-wrong decision in this document.
4. **Warning-then-terminate two-threshold model** (vs. a single blunt
   termination threshold) accepted into scope for v1, since it changes the
   config schema, state machine, and UI from day one.
5. **No low-level system-wide input blocking** (Alt+Tab, Windows key) is a
   permanent v1 constraint, not a "figure it out later" item, since it
   defines the boundary of Section 15's security model.
6. **Local-only logging in v1**, no network dependency for core exam
   integrity logging — confirmed so the networking/backend team doesn't
   assume a server component is coming in v1.
7. **Config is load-once at launch**, no hot-reload — confirmed to avoid an
   entire class of mid-session-config-change edge cases.
8. **.NET 8 self-contained deployment** (vs. requiring a pre-installed
   .NET runtime) — confirmed given the app already avoids depending on a
   pre-installed browser runtime; consistency of that principle matters.
9. **Accessibility exemption mechanism** — decide now whether this ships in
   v1 or is explicitly deferred, since it affects the config schema and the
   state machine's event model either way.
10. **Code-signing certificate procurement** started immediately, in
    parallel with Milestone 1, given typical lead times.
