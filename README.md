# 🛡️ FlyLock Browser & Creator Studio

> **Enterprise-Grade Secure Assessment Kiosk System with Google OAuth 2.0 Domain Enforcement, Live Oversight & Re-Attempt Management**

FlyLock Browser is a full-stack secure assessment platform designed for educational institutions. It combines a **Windows WPF Native Kiosk Application** (`CefSharp Chromium` + `.NET 8`) with a **Python Server Backend** and a feature-rich **Creator Studio & Admin Portal**.

---

## 🌟 Key Features

- 🔒 **Google OAuth 2.0 Domain Enforcement**: Mandatory identity verification enforcing `@bitsathy.ac.in` institutional emails.
- ⚡ **System Browser OAuth Integration**: Google login opens in default system browser (Chrome/Edge/Firefox) avoiding webview 400 errors.
- 💾 **Persistent Student Account Caching**: Log in once via Google; the app securely saves the session locally (`student_session.json`) for all future assessments.
- 📝 **Dual MCQ Builder**:
  - **Manual MCQ Builder**: Rich UI for building multiple-choice questions with answer keys and rationale explanations.
  - **Bulk CSV Import**: Import entire question banks via CSV files with strict schema validation.
- 📊 **Student Responses & Question-by-Question Audit**: Detailed breakdown of student scores, total marks, percentages, and selected choices versus correct answer keys.
- 🔄 **Grant Re-attempt / Emergency Session Reset**: One-click re-attempt granting for creators and admins to clear stuck or crashed student sessions during power outages or network disruptions.
- 👁️ **Live Assessment Oversight**: Real-time counters showing total attempts, active `IN_PROGRESS` students, `SUBMITTED` exams, and `TERMINATED` sessions.
- 🛡️ **Kiosk Security Lockdown**: Low-level Win32 keyboard hooks, touchpad gesture suppression, window focus monitoring, and proctor exit password authorization.

---

## 🚀 Quick Start Guide

### Prerequisites
1. **Python 3.10+**
2. **.NET 8.0 SDK** (Windows x64)

### 1. Start the Server Backend
```bash
# Navigate to the repository directory
cd FlyBrowser

# Run the Python backend server
python server/server.py
```
*The server will start listening at `http://localhost:8080`.*

### 2. Build & Launch FlyLock Browser Kiosk
```bash
# Build the WPF Native App
dotnet build src/FocusLock.App/FocusLock.App.csproj

# Run the FlyLock Browser App
dotnet run --project src/FocusLock.App/FocusLock.App.csproj
```

---

## 📖 Creator & Instructor Walkthrough

### 1. Accessing Creator Studio
Open your web browser and navigate to:
```
http://localhost:8080/portal.html
```

### 2. Creating an Assessment
#### Method A: Manual MCQ Builder
1. Click the **Manual MCQ Builder** tab under **Create Assessment**.
2. Set the **Assessment Title**, **Duration (Minutes)**, and unique 5-digit **Access PIN** (or click *Generate Random PIN*).
3. Add questions, enter option choices, mark correct answer radio buttons, and provide optional explanation rationales.
4. Click **Save & Publish Assessment**.

#### Method B: Bulk CSV Import
1. Click the **Bulk CSV Import** tab.
2. Select an existing assessment or type details for a new assessment.
3. Prepare a CSV file formatted as:
   ```csv
   Question Text,Option A,Option B,Option C,Option D,Correct Option Index (0-3),Explanation
   "What is 2+2?","3","4","5","6",1,"Basic arithmetic"
   ```
4. Click **Upload & Publish Questions**.

---

### 3. Monitoring Student Responses & Live Sessions
1. Click the **Student Responses** tab in Creator Studio.
2. Filter responses by selecting a specific **Assessment PIN** or typing a student email.
3. View real-time status:
   - 🟡 **Live In-Progress**: Active students taking the exam.
   - 🟢 **Submitted**: Completed exams.
   - 🔴 **Terminated / Blocked**: Interrupted sessions.
4. Click **View Answers** next to any student row to review their exact question-by-question answer choices and score breakdown.

---

### 4. Granting Re-attempts (Emergency Session Reset)
If a student experiences a power cut, browser crash, or network disconnect during an assessment:
1. Navigate to the **Student Responses** tab in Creator Studio.
2. Locate the student's attempt row.
3. Click the red **Grant Re-attempt** button.
4. Confirm the prompt. The server will clear the blocked session record, allowing the student to immediately open FlyLock Browser, enter the 5-digit PIN, and retake the assessment.

---

### 5. Administrator Control Center
Click **Admin Control** in the top navigation bar to access:
- **Creator Email Allowlist**: Add or revoke creator privileges for instructor emails (`@bitsathy.ac.in`).
- **Student Attempt Monitor**: Global view of all exam attempts.
- **Security & Session Audit Log Stream**: Real-time security audit log of token issuances, redemptions, login events, and terminations.

---

## 👨‍🎓 Student Exam Experience

1. **First-Time Google Authentication**:
   - Open FlyLock Browser. Click **SIGN IN WITH GOOGLE IN BROWSER**.
   - Your system browser opens `http://localhost:8080/student-login.html`.
   - Sign in with your verified `@bitsathy.ac.in` Google account.
   - Upon successful sign-in, the browser displays a confirmation page and auto-closes.
2. **Persistent Account Memory**:
   - FlyLock Browser saves your student email in `%LOCALAPPDATA%\FlyLock\student_session.json`.
   - On all future app launches, FlyLock Browser skips login and opens directly to the PIN Entry screen.
3. **Attending Exam**:
   - Enter the 5-digit **Assessment Access Code** provided by your instructor.
   - Click **START ASSESSMENT**. FlyLock Browser upgrades to full lockdown kiosk mode.
   - Answer questions, review progress, and click **Final Submit**.

---

## 🛠️ Repository Architecture

```
FlyBrowser/
├── README.md                      # Complete System Documentation & Guide
├── FocusLockBrowser.sln           # Visual Studio .NET Solution File
├── server/                        # Python Backend Server & Portal Assets
│   ├── server.py                  # HTTP API server, SQLite database, auth logic
│   └── public/                    # Frontend HTML/JS/CSS assets
│       ├── portal.html            # Creator Studio & Admin Portal UI
│       ├── portal.js              # Creator Studio frontend logic
│       ├── index.html             # Student assessment kiosk view
│       ├── app.js                 # Student kiosk engine logic
│       └── student-login.html     # Google OAuth 2.0 student login page
└── src/                           # Native C# WPF Kiosk Application
    ├── FocusLock.App/             # Main WPF UI Window & Views
    │   ├── MainWindow.xaml.cs     # Fullscreen lockdown & session state management
    │   └── Views/                 # HomeView, LoginPromptView, LoadingView, etc.
    ├── FocusLock.Browser/         # CefSharp Chromium Browser Host & Handlers
    ├── FocusLock.Config/          # Configuration models & StudentSessionStorage
    ├── FocusLock.Core/            # Session State Machine
    ├── FocusLock.Focus/           # Win32 Focus Monitor & Key Hooks
    └── FocusLock.Security/        # Proctor Exit Authorization & Cryptography
```

---

## 📜 License & Accreditation

Developed for **BIT Sathy (Bannari Amman Institute of Technology)**. Cryptographically verified via Google OAuth 2.0.
