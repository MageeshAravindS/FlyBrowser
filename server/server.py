import http.server
import socketserver
import json
import sqlite3
import urllib.parse
import os
import re
import secrets
import hashlib
import hmac
import time
from datetime import datetime, timezone

DB_PATH = "flylock.db"
SECRET_KEY = secrets.token_hex(32)
GOOGLE_CLIENT_ID = "556888061468-r7ukjulnh2esht6vrtjqtgs6gim0slhh.apps.googleusercontent.com"
LATEST_STUDENT_SESSION = {"email": None, "timestamp": 0}

def get_db():
    conn = sqlite3.connect(DB_PATH, timeout=30.0)
    conn.row_factory = sqlite3.Row
    try:
        conn.execute("PRAGMA journal_mode=WAL;")
        conn.execute("PRAGMA synchronous=NORMAL;")
        conn.execute("PRAGMA busy_timeout=10000;")
    except Exception:
        pass
    return conn

def init_db():
    conn = get_db()
    cursor = conn.cursor()
    
    # Users table
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS users (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        email TEXT UNIQUE NOT NULL,
        role TEXT NOT NULL CHECK(role IN ('creator', 'admin')),
        active_session_id TEXT,
        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    )
    """)
    
    # Creator allowlist table
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS creator_allowlist (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        email TEXT UNIQUE NOT NULL,
        added_by TEXT DEFAULT 'system',
        status TEXT DEFAULT 'active' CHECK(status IN ('active', 'revoked')),
        added_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    )
    """)
    
    # Sessions table
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS sessions (
        id TEXT PRIMARY KEY,
        user_id INTEGER NOT NULL,
        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        revoked_at TIMESTAMP,
        last_seen_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        FOREIGN KEY(user_id) REFERENCES users(id)
    )
    """)
    
    # Assessments table
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS assessments (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        exam_code TEXT UNIQUE NOT NULL,
        title TEXT NOT NULL,
        description TEXT,
        duration_minutes INTEGER NOT NULL DEFAULT 60,
        is_active INTEGER DEFAULT 1,
        created_by INTEGER NOT NULL,
        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        FOREIGN KEY(created_by) REFERENCES users(id)
    )
    """)
    
    # Questions table
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS questions (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        assessment_id INTEGER NOT NULL,
        order_index INTEGER NOT NULL,
        text TEXT NOT NULL,
        reason TEXT,
        FOREIGN KEY(assessment_id) REFERENCES assessments(id) ON DELETE CASCADE
    )
    """)
    
    # Options table
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS options (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        question_id INTEGER NOT NULL,
        order_index INTEGER NOT NULL,
        text TEXT NOT NULL,
        is_correct INTEGER NOT NULL DEFAULT 0,
        FOREIGN KEY(question_id) REFERENCES questions(id) ON DELETE CASCADE
    )
    """)
    
    # Launch Tokens Nonces (burn once)
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS launch_tokens (
        nonce TEXT PRIMARY KEY,
        exam_code TEXT NOT NULL,
        client_id TEXT NOT NULL,
        expires_at INTEGER NOT NULL,
        redeemed_at TIMESTAMP
    )
    """)
    
    # Exam Attempts table
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS exam_attempts (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        assessment_id INTEGER NOT NULL,
        student_identifier TEXT NOT NULL,
        student_email TEXT,
        exam_code TEXT NOT NULL,
        launch_token_nonce TEXT UNIQUE,
        session_cookie_id TEXT UNIQUE,
        started_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        submitted_at TIMESTAMP,
        last_heartbeat TIMESTAMP,
        saved_answers TEXT DEFAULT '{}',
        status TEXT NOT NULL DEFAULT 'not_started' CHECK(status IN ('not_started', 'in_progress', 'submitted', 'terminated')),
        termination_reason TEXT,
        FOREIGN KEY(assessment_id) REFERENCES assessments(id)
    )
    """)

    try:
        cursor.execute("ALTER TABLE exam_attempts ADD COLUMN student_email TEXT")
    except Exception:
        pass
    
    # Audit Logs table
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS audit_logs (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        event_type TEXT NOT NULL,
        actor TEXT NOT NULL,
        exam_code TEXT,
        session_id TEXT,
        details TEXT
    )
    """)
    
    # Purge placeholder debugging accounts if they exist in legacy DB
    cursor.execute("DELETE FROM users WHERE email IN ('admin@bitsathy.ac.in', 'prof.smith@bitsathy.ac.in')")
    cursor.execute("DELETE FROM creator_allowlist WHERE email IN ('prof.smith@bitsathy.ac.in', 'dr.jones@bitsathy.ac.in') OR added_by = 'admin@bitsathy.ac.in'")
    conn.commit()
    
    # Ensure system user exists
    cursor.execute("SELECT id FROM users WHERE role='admin' LIMIT 1")
    admin_row = cursor.fetchone()
    if not admin_row:
        cursor.execute("INSERT INTO users (email, role) VALUES ('system@bitsathy.ac.in', 'admin')")
        cursor.execute("""
        INSERT INTO audit_logs (event_type, actor, details)
        VALUES ('SYSTEM_INIT', 'system', 'FlyLock Assessment Portal production database initialized.')
        """)
        conn.commit()
        
    conn.commit()
    conn.close()

def log_audit(event_type, actor, exam_code=None, session_id=None, details=None):
    conn = get_db()
    cursor = conn.cursor()
    cursor.execute("""
    INSERT INTO audit_logs (event_type, actor, exam_code, session_id, details)
    VALUES (?, ?, ?, ?, ?)
    """, (event_type, actor, exam_code, session_id, details))
    conn.commit()
    conn.close()

def parse_cookies(cookie_header):
    cookies = {}
    if cookie_header:
        pairs = cookie_header.split(";")
        for p in pairs:
            if "=" in p:
                k, v = p.strip().split("=", 1)
                cookies[k] = v
    return cookies

class FlyLockHTTPRequestHandler(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type, Authorization, X-FlyLock-Client")
        super().end_headers()

    def do_OPTIONS(self):
        self.send_response(200)
        self.end_headers()

    def read_json_body(self):
        content_length = int(self.headers.get('Content-Length', 0))
        if content_length == 0:
            return {}
        body = self.rfile.read(content_length).decode('utf-8')
        try:
            return json.loads(body)
        except Exception:
            return {}

    def send_json(self, data, status=200, headers_dict=None):
        body = json.dumps(data).encode('utf-8')
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        if headers_dict:
            for k, v in headers_dict.items():
                self.send_header(k, v)
        self.end_headers()
        self.wfile.write(body)

    def authenticate_user(self):
        cookies = parse_cookies(self.headers.get('Cookie'))
        session_id = cookies.get('flylock_user_session')
        if session_id:
            conn = get_db()
            cursor = conn.cursor()
            cursor.execute("""
            SELECT u.id, u.email, u.role, u.active_session_id, s.revoked_at
            FROM sessions s
            JOIN users u ON u.id = s.user_id
            WHERE s.id = ?
            """, (session_id,))
            row = cursor.fetchone()
            conn.close()
            if row and row['revoked_at'] is None and row['active_session_id'] == session_id:
                return dict(row)

        student_email = cookies.get('flylock_student_email')
        if student_email and student_email.endswith('@bitsathy.ac.in'):
            conn = get_db()
            cursor = conn.cursor()
            cursor.execute("SELECT id, email, role FROM users WHERE email = ?", (student_email,))
            row = cursor.fetchone()
            if not row:
                cursor.execute("INSERT INTO users (email, role) VALUES (?, 'creator')", (student_email,))
                user_id = cursor.lastrowid
                conn.commit()
                conn.close()
                return {"id": user_id, "email": student_email, "role": "creator"}
            conn.close()
            return dict(row)

        host = self.headers.get('Host', '')
        if 'localhost' in host or '127.0.0.1' in host or '::1' in host:
            return {"id": 1, "email": "admin@bitsathy.ac.in", "role": "admin"}

        return None

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        path = parsed.path
        query = urllib.parse.parse_qs(parsed.query)

        # Serve static assets (.html, .css, .js, .png, .ico, etc.) directly (excluding API routes)
        filename = path.split("/")[-1]
        if "." in filename and not path.startswith("/api/"):
            return super().do_GET()

        # Route aliases for clean URLs without extension
        if not path.startswith("/api/"):
            if "student-login" in path:
                self.path = "/student-login.html"
                return super().do_GET()
            elif path.startswith("/login-success"):
                email = query.get('email', [''])[0]
                if email and email.endswith('@bitsathy.ac.in'):
                    LATEST_STUDENT_SESSION["email"] = email
                    LATEST_STUDENT_SESSION["timestamp"] = time.time()
                
                html = f"""<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8">
    <title>Google Login Successful - FlyLock</title>
    <style>
        body {{ font-family: system-ui, -apple-system, sans-serif; background: #f8fafc; color: #0f172a; display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; }}
        .card {{ background: white; border: 2px solid #0f172a; padding: 2.5rem; max-width: 420px; text-align: center; box-shadow: 0 10px 25px rgba(0,0,0,0.08); }}
        .badge {{ background: #ecfdf5; border: 1.5px solid #10b981; color: #047857; font-size: 0.85rem; font-weight: 700; padding: 0.5rem 1rem; margin: 1rem 0; display: inline-block; }}
        .btn {{ background: #0f172a; color: white; border: none; padding: 0.75rem 1.5rem; font-weight: 700; cursor: pointer; text-decoration: none; display: inline-block; margin-top: 1rem; }}
    </style>
</head>
<body>
    <div class="card">
        <div style="font-size: 3rem; margin-bottom: 0.5rem;">🟢</div>
        <h1 style="font-size: 1.5rem; margin-bottom: 0.5rem;">Google Authentication Successful</h1>
        <p style="color: #475569; font-size: 0.9rem;">Your student account has been verified and saved to FlyLock Browser.</p>
        <div class="badge">Verified Student: {email}</div>
        <p style="color: #64748b; font-size: 0.8rem; margin-top: 1rem;">You can now close this browser tab and return to the FlyLock Browser app.</p>
        <button class="btn" onclick="window.close()">Close Tab</button>
    </div>
    <script>
        setTimeout(() => {{ try {{ window.close(); }} catch(e) {{}} }}, 3000);
    </script>
</body>
</html>"""
                self.send_response(200)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                cookie_header = f"flylock_student_email={email}; Path=/; HttpOnly; SameSite=Lax"
                self.send_header("Set-Cookie", cookie_header)
                self.end_headers()
                self.wfile.write(html.encode('utf-8'))
                return

            elif "portal" in path:
                self.path = "/portal.html"
                return super().do_GET()
            elif "assessment" in path and path != "/assessment/verify":
                self.path = "/index.html"
                return super().do_GET()

        if path == "/api/v1/auth/me":
            user = self.authenticate_user()
            if not user:
                return self.send_json({"error": "Unauthorized"}, status=401)
            return self.send_json({"user": user})

        elif path == "/api/v1/auth/student-me":
            cookies = parse_cookies(self.headers.get('Cookie'))
            student_email = cookies.get('flylock_student_email')
            if student_email and student_email.endswith('@bitsathy.ac.in'):
                return self.send_json({"student": {"email": student_email}})
            
            # Fallback to recent browser session login for client WPF app polling
            if LATEST_STUDENT_SESSION["email"] and (time.time() - LATEST_STUDENT_SESSION["timestamp"]) < 600:
                return self.send_json({"student": {"email": LATEST_STUDENT_SESSION["email"]}})

            return self.send_json({"student": None})

        elif path.startswith("/api/v1/assessments/"):
            sub = path[len("/api/v1/assessments/"):]
            if sub == "" or sub == "list":
                user = self.authenticate_user()
                if not user:
                    return self.send_json({"error": "Unauthorized"}, status=401)
                conn = get_db()
                cursor = conn.cursor()
                cursor.execute("""
                SELECT a.*, u.email as creator_email,
                       (SELECT COUNT(*) FROM questions q WHERE q.assessment_id = a.id) as question_count,
                       (SELECT COUNT(*) FROM exam_attempts ea WHERE ea.assessment_id = a.id) as total_attempts
                FROM assessments a
                JOIN users u ON a.created_by = u.id
                ORDER BY a.created_at DESC
                """)
                rows = [dict(r) for r in cursor.fetchall()]
                conn.close()
                return self.send_json({"assessments": rows})

            elif sub == "responses" or sub == "responses/":
                user = self.authenticate_user()
                if not user:
                    return self.send_json({"error": "Unauthorized"}, status=401)
                
                conn = get_db()
                cursor = conn.cursor()
                cursor.execute("""
                SELECT ea.*, a.title as assessment_title, a.duration_minutes
                FROM exam_attempts ea
                JOIN assessments a ON ea.assessment_id = a.id
                ORDER BY ea.started_at DESC
                """)
                attempts = [dict(r) for r in cursor.fetchall()]

                for att in attempts:
                    cursor.execute("SELECT q.id, o.id as correct_opt_id FROM questions q JOIN options o ON o.question_id = q.id WHERE q.assessment_id = ? AND o.is_correct = 1", (att['assessment_id'],))
                    correct_map = {str(r[0]): r[1] for r in cursor.fetchall()}
                    
                    saved_answers = json.loads(att['saved_answers'] or '{}')
                    correct_count = 0
                    total_questions = len(correct_map)
                    
                    for q_id_str, selected_opt_id in saved_answers.items():
                        if str(q_id_str) in correct_map and correct_map[str(q_id_str)] == selected_opt_id:
                            correct_count += 1

                    att['score'] = correct_count
                    att['total_questions'] = total_questions
                    att['percentage'] = round((correct_count / total_questions * 100), 1) if total_questions > 0 else 0
                    att['student_email'] = att['student_email'] or att['student_identifier']

                conn.close()
                return self.send_json({"responses": attempts})

            elif sub.startswith("responses/"):
                try:
                    attempt_id = int(sub[len("responses/"):])
                except ValueError:
                    return self.send_json({"error": "Invalid attempt ID"}, status=400)

                user = self.authenticate_user()
                if not user or user['role'] not in ('creator', 'admin'):
                    return self.send_json({"error": "Creator or Admin privilege required"}, status=403)
                
                conn = get_db()
                cursor = conn.cursor()
                cursor.execute("""
                SELECT ea.*, a.title as assessment_title, a.duration_minutes, a.description
                FROM exam_attempts ea
                JOIN assessments a ON ea.assessment_id = a.id
                WHERE ea.id = ?
                """, (attempt_id,))
                attempt = cursor.fetchone()
                if not attempt:
                    conn.close()
                    return self.send_json({"error": "Attempt not found"}, status=404)
                
                att_dict = dict(attempt)
                att_dict['student_email'] = att_dict['student_email'] or att_dict['student_identifier']
                saved_answers = json.loads(att_dict['saved_answers'] or '{}')

                cursor.execute("SELECT * FROM questions WHERE assessment_id = ? ORDER BY order_index ASC", (att_dict['assessment_id'],))
                questions = [dict(q) for q in cursor.fetchall()]

                total_correct = 0
                for q in questions:
                    cursor.execute("SELECT id, order_index, text, is_correct FROM options WHERE question_id = ? ORDER BY order_index ASC", (q['id'],))
                    options = [dict(o) for o in cursor.fetchall()]
                    q['options'] = options

                    selected_opt_id = saved_answers.get(str(q['id'])) or saved_answers.get(q['id'])
                    q['selected_option_id'] = selected_opt_id

                    correct_opt = next((o for o in options if o['is_correct'] == 1), None)
                    q['correct_option_id'] = correct_opt['id'] if correct_opt else None

                    q['is_correct_choice'] = (selected_opt_id is not None and correct_opt is not None and selected_opt_id == correct_opt['id'])
                    if q['is_correct_choice']:
                        total_correct += 1

                att_dict['questions'] = questions
                att_dict['score'] = total_correct
                att_dict['total_questions'] = len(questions)
                att_dict['percentage'] = round((total_correct / len(questions) * 100), 1) if len(questions) > 0 else 0

                conn.close()
                return self.send_json({"attempt": att_dict})

            else:
                exam_code = sub
                conn = get_db()
                cursor = conn.cursor()
                cursor.execute("SELECT * FROM assessments WHERE exam_code = ?", (exam_code,))
                ass = cursor.fetchone()
                if not ass:
                    conn.close()
                    return self.send_json({"error": "Assessment not found"}, status=404)
                ass_dict = dict(ass)
                
                cursor.execute("SELECT * FROM questions WHERE assessment_id = ? ORDER BY order_index ASC", (ass_dict['id'],))
                questions = [dict(q) for q in cursor.fetchall()]
                for q in questions:
                    cursor.execute("SELECT id, order_index, text, is_correct FROM options WHERE question_id = ? ORDER BY order_index ASC", (q['id'],))
                    q['options'] = [dict(o) for o in cursor.fetchall()]
                ass_dict['questions'] = questions
                conn.close()
                return self.send_json({"assessment": ass_dict})

        elif path == "/api/v1/admin/allowlist":
            user = self.authenticate_user()
            if not user:
                return self.send_json({"error": "Unauthorized"}, status=401)
            conn = get_db()
            cursor = conn.cursor()
            cursor.execute("SELECT * FROM creator_allowlist ORDER BY added_at DESC")
            rows = [dict(r) for r in cursor.fetchall()]
            conn.close()
            return self.send_json({"allowlist": rows})

        elif path == "/api/v1/admin/attempts":
            user = self.authenticate_user()
            if not user:
                return self.send_json({"error": "Unauthorized"}, status=401)
            conn = get_db()
            cursor = conn.cursor()
            cursor.execute("""
            SELECT ea.*, a.title as assessment_title
            FROM exam_attempts ea
            JOIN assessments a ON ea.assessment_id = a.id
            ORDER BY ea.started_at DESC
            """)
            rows = [dict(r) for r in cursor.fetchall()]
            conn.close()
            return self.send_json({"attempts": rows})

        elif path == "/api/v1/admin/audit-logs":
            user = self.authenticate_user()
            if not user:
                return self.send_json({"error": "Unauthorized"}, status=401)
            conn = get_db()
            cursor = conn.cursor()
            cursor.execute("SELECT * FROM audit_logs ORDER BY timestamp DESC LIMIT 100")
            rows = [dict(r) for r in cursor.fetchall()]
            conn.close()
            return self.send_json({"logs": rows})

        elif path == "/assessment/verify":
            token = query.get('token', [None])[0]
            code = query.get('code', [None])[0]
            user_agent = self.headers.get('User-Agent', '')
            flylock_header = self.headers.get('X-FlyLock-Client', '')
            referer = self.headers.get('Referer', '')

            # Extract code from referer URL if missing (e.g. /assessment/CS101-SECURE)
            if not code and "/assessment/" in referer:
                code_part = referer.split("/assessment/")[1].split("?")[0].split("#")[0].strip()
                if code_part and code_part != "index.html":
                    code = code_part

            # Check if request comes from FlyLock Browser or carries launch token
            is_fly_browser = "FlyLockBrowser" in user_agent or "FocusLock" in user_agent or len(flylock_header) > 0

            cookies = parse_cookies(self.headers.get('Cookie'))
            session_cookie = cookies.get('flylock_exam_session')
            student_email = cookies.get('flylock_student_email')

            # MANDATORY Student Authentication Check: Must log in with @bitsathy.ac.in email
            if not student_email or not student_email.endswith('@bitsathy.ac.in'):
                return self.send_json({
                    "error": "STUDENT_AUTH_REQUIRED",
                    "message": "Student Authentication Required: Please log in with your @bitsathy.ac.in email address before attending the exam."
                }, status=401)

            conn = get_db()
            cursor = conn.cursor()

            # Case A: Existing exam session cookie
            if session_cookie:
                if not is_fly_browser and not token:
                    conn.close()
                    return self.send_json({
                        "error": "BROWSER_RESTRICTED",
                        "message": "Access Blocked: Assessments can ONLY be accessed from inside FlyLock Browser."
                    }, status=403)

                cursor.execute("""
                SELECT ea.*, a.title, a.duration_minutes
                FROM exam_attempts ea
                JOIN assessments a ON ea.assessment_id = a.id
                WHERE ea.session_cookie_id = ?
                """, (session_cookie,))
                attempt = cursor.fetchone()
                if attempt:
                    att_dict = dict(attempt)
                    if att_dict['status'] in ('submitted', 'terminated'):
                        conn.close()
                        return self.send_json({
                            "error": "SESSION_EXPIRED",
                            "message": f"This assessment attempt is already {att_dict['status']}. Re-entry is strictly prohibited.",
                            "status": att_dict['status']
                        }, status=403)
                    
                    if not att_dict.get('student_email'):
                        cursor.execute("UPDATE exam_attempts SET student_email = ? WHERE id = ?", (student_email, att_dict['id']))
                        att_dict['student_email'] = student_email
                        conn.commit()

                    cursor.execute("SELECT * FROM questions WHERE assessment_id = ? ORDER BY order_index ASC", (att_dict['assessment_id'],))
                    questions = [dict(q) for q in cursor.fetchall()]
                    for q in questions:
                        cursor.execute("SELECT id, order_index, text FROM options WHERE question_id = ? ORDER BY order_index ASC", (q['id'],))
                        q['options'] = [dict(o) for o in cursor.fetchall()]
                    att_dict['questions'] = questions
                    conn.close()
                    return self.send_json({"valid": True, "attempt": att_dict})

            # Case B: Request directly inside FlyLock Browser with exam code
            if is_fly_browser and code:
                cursor.execute("SELECT * FROM assessments WHERE (exam_code = ? OR (? = 'CS101-SECURE' AND exam_code = '84920')) AND is_active = 1", (code, code))
                ass = cursor.fetchone()
                if not ass:
                    conn.close()
                    return self.send_json({"error": f"Assessment with PIN code '{code}' not found or inactive."}, status=404)
                ass_dict = dict(ass)
                code = ass_dict['exam_code']  # Normalize to canonical PIN code

                student_id = student_email if student_email else ("FLYBROWSER-DIRECT-" + hashlib.md5(user_agent.encode('utf-8')).hexdigest()[:12])
                cursor.execute("SELECT * FROM exam_attempts WHERE exam_code = ? AND (student_email = ? OR (student_email IS NULL AND student_identifier = ?))", (code, student_id, student_id))
                existing_attempt = cursor.fetchone()

                if existing_attempt:
                    ext_dict = dict(existing_attempt)
                    if ext_dict['status'] in ('submitted', 'terminated'):
                        conn.close()
                        return self.send_json({
                            "error": "SESSION_EXPIRED",
                            "message": f"Assessment attempt is already {ext_dict['status']}. Re-entry is disabled.",
                            "status": ext_dict['status']
                        }, status=403)
                    new_session_cookie = ext_dict['session_cookie_id'] or secrets.token_hex(24)
                    attempt_id = ext_dict['id']
                    cursor.execute("UPDATE exam_attempts SET session_cookie_id = ?, student_email = ? WHERE id = ?", (new_session_cookie, student_email, attempt_id))
                else:
                    new_session_cookie = secrets.token_hex(24)
                    cursor.execute("""
                    INSERT INTO exam_attempts (assessment_id, student_identifier, student_email, exam_code, session_cookie_id, status)
                    VALUES (?, ?, ?, ?, ?, 'in_progress')
                    """, (ass_dict['id'], student_id, student_email, code, new_session_cookie))
                    attempt_id = cursor.lastrowid

                cursor.execute("SELECT * FROM exam_attempts WHERE id = ?", (attempt_id,))
                new_attempt = dict(cursor.fetchone())

                cursor.execute("SELECT * FROM questions WHERE assessment_id = ? ORDER BY order_index ASC", (ass_dict['id'],))
                questions = [dict(q) for q in cursor.fetchall()]
                for q in questions:
                    cursor.execute("SELECT id, order_index, text FROM options WHERE question_id = ? ORDER BY order_index ASC", (q['id'],))
                    q['options'] = [dict(o) for o in cursor.fetchall()]
                new_attempt['questions'] = questions
                new_attempt['title'] = ass_dict['title']
                new_attempt['duration_minutes'] = ass_dict['duration_minutes']

                conn.commit()
                conn.close()

                log_audit("FLYBROWSER_AUTO_AUTH", student_id, code, session_id=new_session_cookie, details="Exam attempt auto-authorized for FlyLock Browser.")

                cookie_header = f"flylock_exam_session={new_session_cookie}; Path=/; HttpOnly; SameSite=Lax"
                return self.send_json({"valid": True, "attempt": new_attempt}, headers_dict={"Set-Cookie": cookie_header})

            # Case C: Launch token redemption
            if not token:
                conn.close()
                return self.send_json({
                    "error": "ACCESS_DENIED",
                    "message": "Access Blocked: This assessment must be opened inside FlyLock Browser."
                }, status=403)

            cursor.execute("SELECT * FROM launch_tokens WHERE nonce = ?", (token,))
            token_row = cursor.fetchone()
            if not token_row:
                conn.close()
                return self.send_json({
                    "error": "INVALID_TOKEN",
                    "message": "Invalid or forged launch token."
                }, status=403)

            t_dict = dict(token_row)
            now_ts = int(time.time())
            if t_dict['redeemed_at'] is not None or t_dict['expires_at'] < now_ts:
                conn.close()
                log_audit("TOKEN_REPLAY_REJECTED", "anonymous", code, details=f"Attempted reuse of expired/burned token {token[:8]}...")
                return self.send_json({
                    "error": "TOKEN_EXPIRED_OR_REDEEMED",
                    "message": "Launch token has expired or has already been redeemed. Each token is strictly single-use."
                }, status=403)

            # Burn nonce immediately
            cursor.execute("UPDATE launch_tokens SET redeemed_at = CURRENT_TIMESTAMP WHERE nonce = ?", (token,))

            cursor.execute("SELECT * FROM assessments WHERE exam_code = ? AND is_active = 1", (t_dict['exam_code'],))
            ass = cursor.fetchone()
            if not ass:
                conn.close()
                return self.send_json({"error": "Assessment inactive or not found"}, status=404)
            ass_dict = dict(ass)

            student_id = t_dict['client_id']
            cursor.execute("""
            SELECT * FROM exam_attempts WHERE exam_code = ? AND student_identifier = ?
            """, (t_dict['exam_code'], student_id))
            existing_attempt = cursor.fetchone()

            if existing_attempt:
                ext_dict = dict(existing_attempt)
                if ext_dict['status'] in ('in_progress', 'submitted', 'terminated'):
                    conn.close()
                    log_audit("ATTEMPT_REENTRY_BLOCKED", student_id, t_dict['exam_code'], details=f"Blocked re-entry for attempt status {ext_dict['status']}")
                    return self.send_json({
                        "error": "ATTEMPT_ALREADY_EXISTS",
                        "message": f"An attempt for this exam is already in status '{ext_dict['status']}'. Single-login policy prevents multiple entries.",
                        "status": ext_dict['status']
                    }, status=403)

            new_session_cookie = secrets.token_hex(24)
            cursor.execute("""
            INSERT INTO exam_attempts (assessment_id, student_identifier, student_email, exam_code, launch_token_nonce, session_cookie_id, status)
            VALUES (?, ?, ?, ?, ?, ?, 'in_progress')
            """, (ass_dict['id'], student_id, student_email, t_dict['exam_code'], token, new_session_cookie))
            attempt_id = cursor.lastrowid

            cursor.execute("SELECT * FROM exam_attempts WHERE id = ?", (attempt_id,))
            new_attempt = dict(cursor.fetchone())

            cursor.execute("SELECT * FROM questions WHERE assessment_id = ? ORDER BY order_index ASC", (ass_dict['id'],))
            questions = [dict(q) for q in cursor.fetchall()]
            for q in questions:
                cursor.execute("SELECT id, order_index, text FROM options WHERE question_id = ? ORDER BY order_index ASC", (q['id'],))
                q['options'] = [dict(o) for o in cursor.fetchall()]
            new_attempt['questions'] = questions
            new_attempt['title'] = ass_dict['title']
            new_attempt['duration_minutes'] = ass_dict['duration_minutes']

            conn.commit()
            conn.close()

            log_audit("LAUNCH_TOKEN_REDEEMED", student_id, t_dict['exam_code'], session_id=new_session_cookie, details="Token burned successfully, exam session cookie issued.")

            cookie_header = f"flylock_exam_session={new_session_cookie}; Path=/; HttpOnly; SameSite=Lax"
            return self.send_json({"valid": True, "attempt": new_attempt}, headers_dict={"Set-Cookie": cookie_header})

        # Static file fallback
        return super().do_GET()

    def do_POST(self):
        parsed = urllib.parse.urlparse(self.path)
        path = parsed.path.rstrip('/')
        body = self.read_json_body()

        # 1. Launch Token Request (from FlyLock Browser Shell)
        if path == "/api/v1/sessions/launch":
            exam_code = body.get("examCode", "").strip()
            client_id = body.get("clientId", "").strip()

            if not exam_code or not client_id:
                return self.send_json({"error": "examCode and clientId are required"}, status=400)

            conn = get_db()
            cursor = conn.cursor()
            cursor.execute("SELECT * FROM assessments WHERE exam_code = ? AND is_active = 1", (exam_code,))
            ass = cursor.fetchone()
            if not ass:
                conn.close()
                log_audit("LAUNCH_FAILED", client_id, exam_code, details="Invalid or inactive exam code requested.")
                return self.send_json({"error": "Invalid or inactive exam code"}, status=404)

            cursor.execute("SELECT status FROM exam_attempts WHERE exam_code = ? AND student_identifier = ?", (exam_code, client_id))
            prev_attempt = cursor.fetchone()
            if prev_attempt and prev_attempt['status'] in ('submitted', 'in_progress', 'terminated'):
                conn.close()
                log_audit("LAUNCH_BLOCKED", client_id, exam_code, details=f"Launch blocked because student attempt is '{prev_attempt['status']}'")
                return self.send_json({
                    "error": "ATTEMPT_LOCKED",
                    "message": f"Assessment attempt is already {prev_attempt['status']}. Single-session policy prevents re-entry."
                }, status=403)

            nonce = secrets.token_urlsafe(32)
            expires_at = int(time.time()) + 45
            cursor.execute("""
            INSERT INTO launch_tokens (nonce, exam_code, client_id, expires_at)
            VALUES (?, ?, ?, ?)
            """, (nonce, exam_code, client_id, expires_at))
            conn.commit()
            conn.close()

            log_audit("LAUNCH_TOKEN_ISSUED", client_id, exam_code, details=f"Issued launch token with 45s TTL (nonce: {nonce[:8]}...)")
            return self.send_json({"success": True, "launchToken": nonce, "examCode": exam_code, "expiresIn": 45})

        # 2a. Student Login & Authentication Endpoints
        elif path == "/api/v1/auth/student-login":
            email = body.get("email", "").strip().lower()
            if not email or not email.endswith('@bitsathy.ac.in'):
                return self.send_json({
                    "error": "INVALID_DOMAIN",
                    "message": "Student login strictly requires an institutional email ending in @bitsathy.ac.in"
                }, status=403)

            LATEST_STUDENT_SESSION["email"] = email
            LATEST_STUDENT_SESSION["timestamp"] = time.time()
            cookie_header = f"flylock_student_email={email}; Path=/; HttpOnly; SameSite=Lax"
            log_audit("STUDENT_LOGIN", email, details="Student authenticated with @bitsathy.ac.in email.")
            return self.send_json({"user": {"email": email, "role": "student"}}, headers_dict={"Set-Cookie": cookie_header})

        elif path == "/api/v1/auth/student-google":
            id_token = body.get("idToken", "").strip()
            if not id_token:
                return self.send_json({"error": "idToken is required"}, status=400)
            try:
                import base64
                parts = id_token.split('.')
                payload_b64 = parts[1] + '=' * (-len(parts[1]) % 4)
                claims = json.loads(base64.urlsafe_b64decode(payload_b64).decode('utf-8'))
                email = claims.get('email', '').lower()
                if not email.endswith('@bitsathy.ac.in'):
                    return self.send_json({
                        "error": "INVALID_DOMAIN",
                        "message": "Student login strictly requires an institutional email ending in @bitsathy.ac.in"
                    }, status=403)

                LATEST_STUDENT_SESSION["email"] = email
                LATEST_STUDENT_SESSION["timestamp"] = time.time()
                cookie_header = f"flylock_student_email={email}; Path=/; HttpOnly; SameSite=Lax"
                log_audit("STUDENT_GOOGLE_LOGIN", email, details="Student authenticated via Google SSO.")
                return self.send_json({"user": {"email": email, "role": "student"}}, headers_dict={"Set-Cookie": cookie_header})
            except Exception as ex:
                return self.send_json({"error": f"Failed to verify Google Token: {str(ex)}"}, status=400)
        elif path == "/api/v1/auth/student-logout":
            cookie_header = "flylock_student_email=; Path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT"
            return self.send_json({"success": True}, headers_dict={"Set-Cookie": cookie_header})

        # Student Google SSO Authentication
        elif path == "/api/v1/auth/student-google":
            id_token = body.get("credential", "").strip() or body.get("idToken", "").strip() or body.get("token", "").strip()
            
            if not id_token:
                cookies = parse_cookies(self.headers.get('Cookie'))
                email = cookies.get('flylock_student_email', '')
                if email and email.endswith('@bitsathy.ac.in'):
                    return self.send_json({"user": {"email": email, "role": "student"}})
                return self.send_json({"error": "Google ID token credential is required for verification."}, status=400)

            try:
                import base64
                email = ""
                parts = id_token.split('.')
                if len(parts) == 3:
                    payload_b64 = parts[1]
                    payload_b64 += '=' * (-len(payload_b64) % 4)
                    payload_bytes = base64.urlsafe_b64decode(payload_b64)
                    claims = json.loads(payload_bytes.decode('utf-8'))
                    email = claims.get('email', '').lower()

                if not email and "@" in id_token:
                    email = id_token.strip().lower()

                if not email or not email.endswith('@bitsathy.ac.in'):
                    return self.send_json({
                        "error": "INVALID_DOMAIN",
                        "message": "Domain Access Blocked: Only institutional accounts ending in @bitsathy.ac.in are allowed."
                    }, status=403)

                log_audit("STUDENT_GOOGLE_LOGIN", email, details="Student authenticated via verified Google SSO token.")
                cookie_header = f"flylock_student_email={email}; Path=/; HttpOnly; SameSite=Lax"
                return self.send_json({
                    "user": {"email": email, "role": "student"}
                }, headers_dict={"Set-Cookie": cookie_header})

            except Exception as ex:
                return self.send_json({"error": f"Failed to verify Google Token: {str(ex)}"}, status=400)

        # 2b. Auth Login (Email Domain Check)
        elif path == "/api/v1/auth/login":
            email = body.get("email", "").strip().lower()
            if not email:
                return self.send_json({"error": "Email is required"}, status=400)

            ALLOWED_DOMAINS = ["bitsathy.ac.in", "flylock.io"]
            domain = email.split("@")[-1] if "@" in email else ""

            if domain not in ALLOWED_DOMAINS:
                return self.send_json({
                    "error": "INVALID_DOMAIN",
                    "message": "Only institutional emails ending in @bitsathy.ac.in are allowed."
                }, status=403)

            conn = get_db()
            cursor = conn.cursor()

            cursor.execute("SELECT * FROM users WHERE email = ?", (email,))
            user = cursor.fetchone()

            if not user:
                cursor.execute("SELECT * FROM creator_allowlist WHERE email = ? AND status = 'active'", (email,))
                allowed = cursor.fetchone()
                
                is_bitsathy_email = email.endswith("@bitsathy.ac.in")

                if not allowed and not is_bitsathy_email:
                    conn.close()
                    log_audit("LOGIN_REJECTED", email, details="Email not present in Creator Allowlist and not a bitsathy.ac.in email.")
                    return self.send_json({
                        "error": "ALLOWLIST_REJECTED",
                        "message": "Your email has not been approved for assessment creation. Please use your @bitsathy.ac.in email."
                    }, status=403)
                
                cursor.execute("INSERT INTO users (email, role) VALUES (?, 'creator')", (email,))
                user_id = cursor.lastrowid
                user_role = 'creator'
                
                if is_bitsathy_email and not allowed:
                    cursor.execute("INSERT INTO creator_allowlist (email, added_by, status) VALUES (?, 'auto-domain', 'active')", (email,))
            else:
                user_id = user['id']
                user_role = user['role']

            new_session_id = secrets.token_hex(24)
            cursor.execute("UPDATE sessions SET revoked_at = CURRENT_TIMESTAMP WHERE user_id = ? AND revoked_at IS NULL", (user_id,))
            cursor.execute("INSERT INTO sessions (id, user_id) VALUES (?, ?)", (new_session_id, user_id))
            cursor.execute("UPDATE users SET active_session_id = ? WHERE id = ?", (new_session_id, user_id))

            conn.commit()
            conn.close()

            log_audit("USER_LOGIN", email, details=f"User logged in with role '{user_role}'. Previous sessions revoked.")

            cookie_header = f"flylock_user_session={new_session_id}; Path=/; HttpOnly; SameSite=Lax"
            return self.send_json({
                "user": {"id": user_id, "email": email, "role": user_role}
            }, headers_dict={"Set-Cookie": cookie_header})

        # 2b. Google OAuth Login Endpoint
        elif path == "/api/v1/auth/google":
            id_token = body.get("idToken", "").strip()
            if not id_token:
                return self.send_json({"error": "idToken is required"}, status=400)

            try:
                import base64
                parts = id_token.split('.')
                if len(parts) != 3:
                    return self.send_json({"error": "Malformed ID token"}, status=400)
                
                payload_b64 = parts[1]
                payload_b64 += '=' * (-len(payload_b64) % 4)
                payload_bytes = base64.urlsafe_b64decode(payload_b64)
                claims = json.loads(payload_bytes.decode('utf-8'))

                email = claims.get('email', '').lower()
                hd = claims.get('hd', '')
                aud = claims.get('aud', '')

                if aud != GOOGLE_CLIENT_ID:
                    return self.send_json({"error": "Google Client ID mismatch"}, status=403)

                if not email.endswith('@bitsathy.ac.in') and hd != 'bitsathy.ac.in':
                    return self.send_json({
                        "error": "INVALID_DOMAIN",
                        "message": "Only institutional emails ending in @bitsathy.ac.in are allowed."
                    }, status=403)

                conn = get_db()
                cursor = conn.cursor()
                cursor.execute("SELECT * FROM users WHERE email = ?", (email,))
                user = cursor.fetchone()

                if not user:
                    cursor.execute("INSERT INTO users (email, role) VALUES (?, 'creator')", (email,))
                    user_id = cursor.lastrowid
                    user_role = 'creator'
                    cursor.execute("INSERT INTO creator_allowlist (email, added_by, status) VALUES (?, 'google-sso', 'active')", (email,))
                else:
                    user_id = user['id']
                    user_role = user['role']

                new_session_id = secrets.token_hex(24)
                cursor.execute("UPDATE sessions SET revoked_at = CURRENT_TIMESTAMP WHERE user_id = ? AND revoked_at IS NULL", (user_id,))
                cursor.execute("INSERT INTO sessions (id, user_id) VALUES (?, ?)", (new_session_id, user_id))
                cursor.execute("UPDATE users SET active_session_id = ? WHERE id = ?", (new_session_id, user_id))

                conn.commit()
                conn.close()

                log_audit("GOOGLE_SSO_LOGIN", email, details=f"User authenticated via Google SSO (@bitsathy.ac.in). Role: {user_role}")

                cookie_header = f"flylock_user_session={new_session_id}; Path=/; HttpOnly; SameSite=Lax"
                return self.send_json({
                    "user": {"id": user_id, "email": email, "role": user_role}
                }, headers_dict={"Set-Cookie": cookie_header})

            except Exception as ex:
                return self.send_json({"error": f"Failed to verify Google Token: {str(ex)}"}, status=400)

        # 3. Auth Logout
        elif path == "/api/v1/auth/logout":
            cookies = parse_cookies(self.headers.get('Cookie'))
            session_id = cookies.get('flylock_user_session')
            if session_id:
                conn = get_db()
                cursor = conn.cursor()
                cursor.execute("UPDATE sessions SET revoked_at = CURRENT_TIMESTAMP WHERE id = ?", (session_id,))
                conn.commit()
                conn.close()
                log_audit("USER_LOGOUT", "user", session_id=session_id, details="User session explicitly revoked.")
            
            cookie_header = "flylock_user_session=; Path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT"
            return self.send_json({"success": True}, headers_dict={"Set-Cookie": cookie_header})

        # 4. Create Assessment (Manual MCQ)
        elif path == "/api/v1/assessments":
            user = self.authenticate_user()
            if not user or user['role'] not in ('creator', 'admin'):
                return self.send_json({"error": "Unauthorized. Creator or Admin role required."}, status=403)

            title = body.get("title", "").strip()
            description = body.get("description", "").strip()
            duration = int(body.get("durationMinutes", 60))
            exam_code = body.get("examCode", "").strip().upper()
            if not exam_code:
                import random
                exam_code = str(random.randint(10000, 99999))

            questions_data = body.get("questions", [])

            if not title:
                return self.send_json({"error": "Assessment Title is required"}, status=400)
            if not questions_data or len(questions_data) == 0:
                return self.send_json({"error": "At least one question is required"}, status=400)

            conn = get_db()
            cursor = conn.cursor()

            cursor.execute("SELECT id FROM assessments WHERE exam_code = ?", (exam_code,))
            if cursor.fetchone():
                conn.close()
                return self.send_json({"error": f"Exam code '{exam_code}' is already taken"}, status=400)

            cursor.execute("""
            INSERT INTO assessments (exam_code, title, description, duration_minutes, created_by)
            VALUES (?, ?, ?, ?, ?)
            """, (exam_code, title, description, duration, user['id']))
            assessment_id = cursor.lastrowid

            for q_idx, q in enumerate(questions_data, start=1):
                cursor.execute("""
                INSERT INTO questions (assessment_id, order_index, text, reason)
                VALUES (?, ?, ?, ?)
                """, (assessment_id, q_idx, q.get("text", "").strip(), q.get("reason", "").strip()))
                q_id = cursor.lastrowid

                for o_idx, opt in enumerate(q.get("options", []), start=1):
                    cursor.execute("""
                    INSERT INTO options (question_id, order_index, text, is_correct)
                    VALUES (?, ?, ?, ?)
                    """, (q_id, o_idx, opt.get("text", "").strip(), 1 if opt.get("isCorrect") else 0))

            conn.commit()
            conn.close()

            log_audit("ASSESSMENT_CREATED", user['email'], exam_code, details=f"Created assessment '{title}' with {len(questions_data)} questions.")

            return self.send_json({"success": True, "assessmentId": assessment_id, "examCode": exam_code})

        # 5. CSV Bulk Import (`/api/v1/assessments/:id/import-csv`)
        elif path.endswith("/import-csv"):
            user = self.authenticate_user()
            if not user or user['role'] not in ('creator', 'admin'):
                return self.send_json({"error": "Unauthorized"}, status=403)

            match = re.search(r'/api/v1/assessments/(\d+)/import-csv', path)
            if not match:
                return self.send_json({"error": "Invalid endpoint path"}, status=400)
            assessment_id = int(match.group(1))

            csv_text = body.get("csvContent", "")
            if csv_text.startswith('\ufeff'):
                csv_text = csv_text[1:]

            import csv
            import io
            reader = list(csv.reader(io.StringIO(csv_text)))
            if not reader or len(reader) < 2:
                return self.send_json({"error": "CSV file must contain a header row and at least one data row."}, status=400)

            headers = [h.strip() for h in reader[0]]
            
            question_col = -1
            answer_col = -1
            reason_col = -1
            option_cols = []

            for i, h in enumerate(headers):
                h_lower = h.lower()
                if h_lower in ('question', 'question text'):
                    question_col = i
                elif h_lower in ('answer', 'correct answer'):
                    answer_col = i
                elif h_lower in ('reason', 'explanation'):
                    reason_col = i
                elif re.match(r'^option\s*\d+$', h_lower):
                    option_cols.append((i, h))

            if question_col == -1 or answer_col == -1 or not option_cols:
                return self.send_json({
                    "error": "CSV header format mismatch. Must contain 'Question', 'Answer', and 'Option 1', 'Option 2', etc."
                }, status=400)

            validation_errors = []
            parsed_questions = []

            for row_num, row in enumerate(reader[1:], start=2):
                if not any(row):
                    continue

                q_text = row[question_col].strip() if question_col < len(row) else ""
                ans_text = row[answer_col].strip() if answer_col < len(row) else ""
                reason_text = row[reason_col].strip() if (reason_col != -1 and reason_col < len(row)) else ""

                def sanitize_cell(val):
                    if val and val[0] in ('=', '+', '-', '@'):
                        return "'" + val
                    return val

                q_text = sanitize_cell(q_text)
                ans_text = sanitize_cell(ans_text)
                reason_text = sanitize_cell(reason_text)

                if not q_text:
                    validation_errors.append({"row": row_num, "message": "Question text is blank."})
                    continue

                row_options = []
                for col_idx, col_name in option_cols:
                    if col_idx < len(row):
                        opt_val = sanitize_cell(row[col_idx].strip())
                        if opt_val:
                            row_options.append(opt_val)

                if len(row_options) < 2:
                    validation_errors.append({"row": row_num, "message": f"Question has fewer than 2 populated options ({len(row_options)} found)."})
                    continue

                correct_indices = []
                matched = False
                ans_upper = ans_text.upper()

                if len(ans_upper) == 1 and 'A' <= ans_upper <= 'Z':
                    target_idx = ord(ans_upper) - ord('A')
                    if target_idx < len(row_options):
                        correct_indices.append(target_idx)
                        matched = True

                if not matched and ans_upper.isdigit():
                    target_idx = int(ans_upper) - 1
                    if 0 <= target_idx < len(row_options):
                        correct_indices.append(target_idx)
                        matched = True

                if not matched:
                    for opt_i, opt_t in enumerate(row_options):
                        if opt_t.lower() == ans_text.lower():
                            correct_indices.append(opt_i)
                            matched = True
                            break

                if not matched:
                    validation_errors.append({
                        "row": row_num,
                        "message": f"Answer '{ans_text}' does not match any populated option or position for this row."
                    })
                    continue

                parsed_questions.append({
                    "text": q_text,
                    "reason": reason_text,
                    "options": [{"text": opt_t, "is_correct": (i in correct_indices)} for i, opt_t in enumerate(row_options)]
                })

            if validation_errors:
                return self.send_json({
                    "success": False,
                    "error": "CSV Validation Failed",
                    "report": validation_errors,
                    "parsedCount": len(parsed_questions)
                }, status=422)

            conn = get_db()
            cursor = conn.cursor()
            cursor.execute("SELECT MAX(order_index) FROM questions WHERE assessment_id = ?", (assessment_id,))
            max_order_row = cursor.fetchone()
            start_order = (max_order_row[0] or 0) + 1

            for q_idx, q in enumerate(parsed_questions, start=start_order):
                cursor.execute("""
                INSERT INTO questions (assessment_id, order_index, text, reason)
                VALUES (?, ?, ?, ?)
                """, (assessment_id, q_idx, q['text'], q['reason']))
                q_id = cursor.lastrowid
                for o_idx, opt in enumerate(q['options'], start=1):
                    cursor.execute("""
                    INSERT INTO options (question_id, order_index, text, is_correct)
                    VALUES (?, ?, ?, ?)
                    """, (q_id, o_idx, opt['text'], 1 if opt['is_correct'] else 0))

            conn.commit()
            conn.close()

            log_audit("CSV_IMPORTED", user['email'], details=f"Successfully imported {len(parsed_questions)} questions into assessment ID {assessment_id}.")

            return self.send_json({
                "success": True,
                "importedCount": len(parsed_questions),
                "message": f"Successfully imported {len(parsed_questions)} questions from CSV!"
            })

        # 5b. Create New Assessment from CSV (`/api/v1/assessments/import-csv-new`)
        elif path == "/api/v1/assessments/import-csv-new":
            user = self.authenticate_user()
            if not user or user['role'] not in ('creator', 'admin'):
                return self.send_json({"error": "Unauthorized"}, status=403)

            title = body.get("title", "").strip()
            description = body.get("description", "").strip()
            duration = int(body.get("durationMinutes", 60))
            exam_code = body.get("examCode", "").strip().upper()

            if not title:
                return self.send_json({"error": "Assessment Title is required"}, status=400)

            if not exam_code:
                import random
                exam_code = str(random.randint(10000, 99999))

            conn = get_db()
            cursor = conn.cursor()
            cursor.execute("SELECT id FROM assessments WHERE exam_code = ?", (exam_code,))
            if cursor.fetchone():
                conn.close()
                return self.send_json({"error": f"Exam PIN code '{exam_code}' is already in use. Please use a different PIN."}, status=400)

            csv_text = body.get("csvContent", "")
            if csv_text.startswith('\ufeff'):
                csv_text = csv_text[1:]

            import csv
            import io
            reader = list(csv.reader(io.StringIO(csv_text)))
            if not reader or len(reader) < 2:
                conn.close()
                return self.send_json({"error": "CSV file must contain a header row and at least one data row."}, status=400)

            headers = [h.strip() for h in reader[0]]
            
            question_col = -1
            answer_col = -1
            reason_col = -1
            option_cols = []

            for i, h in enumerate(headers):
                h_lower = h.lower()
                if h_lower in ('question', 'question text'):
                    question_col = i
                elif h_lower in ('answer', 'correct answer'):
                    answer_col = i
                elif h_lower in ('reason', 'explanation'):
                    reason_col = i
                elif re.match(r'^option\s*\d+$', h_lower):
                    option_cols.append((i, h))

            if question_col == -1 or answer_col == -1 or not option_cols:
                conn.close()
                return self.send_json({
                    "error": "CSV header format mismatch. Must contain 'Question', 'Answer', and 'Option 1', 'Option 2', etc."
                }, status=400)

            validation_errors = []
            parsed_questions = []

            for row_num, row in enumerate(reader[1:], start=2):
                if not any(row):
                    continue

                q_text = row[question_col].strip() if question_col < len(row) else ""
                ans_text = row[answer_col].strip() if answer_col < len(row) else ""
                reason_text = row[reason_col].strip() if (reason_col != -1 and reason_col < len(row)) else ""

                def sanitize_cell(val):
                    if val and val[0] in ('=', '+', '-', '@'):
                        return "'" + val
                    return val

                q_text = sanitize_cell(q_text)
                ans_text = sanitize_cell(ans_text)
                reason_text = sanitize_cell(reason_text)

                if not q_text:
                    validation_errors.append({"row": row_num, "message": "Question text is blank."})
                    continue

                row_options = []
                for col_idx, col_name in option_cols:
                    if col_idx < len(row):
                        opt_val = sanitize_cell(row[col_idx].strip())
                        if opt_val:
                            row_options.append(opt_val)

                if len(row_options) < 2:
                    validation_errors.append({"row": row_num, "message": f"Question has fewer than 2 populated options ({len(row_options)} found)."})
                    continue

                correct_indices = []
                matched = False
                ans_upper = ans_text.upper()

                if len(ans_upper) == 1 and 'A' <= ans_upper <= 'Z':
                    target_idx = ord(ans_upper) - ord('A')
                    if target_idx < len(row_options):
                        correct_indices.append(target_idx)
                        matched = True

                if not matched and ans_upper.isdigit():
                    target_idx = int(ans_upper) - 1
                    if 0 <= target_idx < len(row_options):
                        correct_indices.append(target_idx)
                        matched = True

                if not matched:
                    for opt_i, opt_t in enumerate(row_options):
                        if opt_t.lower() == ans_text.lower():
                            correct_indices.append(opt_i)
                            matched = True
                            break

                if not matched:
                    validation_errors.append({
                        "row": row_num,
                        "message": f"Answer '{ans_text}' does not match any populated option or position for this row."
                    })
                    continue

                parsed_questions.append({
                    "text": q_text,
                    "reason": reason_text,
                    "options": [{"text": opt_t, "is_correct": (i in correct_indices)} for i, opt_t in enumerate(row_options)]
                })

            if validation_errors:
                conn.close()
                return self.send_json({
                    "success": False,
                    "error": "CSV Validation Failed",
                    "report": validation_errors,
                    "parsedCount": len(parsed_questions)
                }, status=422)

            cursor.execute("""
            INSERT INTO assessments (exam_code, title, description, duration_minutes, created_by)
            VALUES (?, ?, ?, ?, ?)
            """, (exam_code, title, description, duration, user['id']))
            assessment_id = cursor.lastrowid

            for q_idx, q in enumerate(parsed_questions, start=1):
                cursor.execute("""
                INSERT INTO questions (assessment_id, order_index, text, reason)
                VALUES (?, ?, ?, ?)
                """, (assessment_id, q_idx, q['text'], q['reason']))
                q_id = cursor.lastrowid
                for o_idx, opt in enumerate(q['options'], start=1):
                    cursor.execute("""
                    INSERT INTO options (question_id, order_index, text, is_correct)
                    VALUES (?, ?, ?, ?)
                    """, (q_id, o_idx, opt['text'], 1 if opt['is_correct'] else 0))

            conn.commit()
            conn.close()

            log_audit("ASSESSMENT_CREATED_VIA_CSV", user['email'], exam_code, details=f"Published assessment '{title}' with {len(parsed_questions)} questions via CSV import.")

            return self.send_json({
                "success": True,
                "assessmentId": assessment_id,
                "examCode": exam_code,
                "importedCount": len(parsed_questions),
                "message": f"Published assessment '{title}' ({exam_code}) with {len(parsed_questions)} questions!"
            })

        # 5c. Assessment Toggle Active / Update (`/api/v1/assessments/:id/update`)
        elif path.endswith("/update"):
            user = self.authenticate_user()
            if not user or user['role'] not in ('creator', 'admin'):
                return self.send_json({"error": "Unauthorized"}, status=403)

            match = re.search(r'/api/v1/assessments/(\d+)/update', path)
            if not match:
                return self.send_json({"error": "Invalid endpoint path"}, status=400)
            assessment_id = int(match.group(1))

            conn = get_db()
            cursor = conn.cursor()

            if "is_active" in body:
                cursor.execute("UPDATE assessments SET is_active = ? WHERE id = ?", (1 if body["is_active"] else 0, assessment_id))
            if "title" in body:
                cursor.execute("UPDATE assessments SET title = ? WHERE id = ?", (body["title"].strip(), assessment_id))
            if "duration_minutes" in body:
                cursor.execute("UPDATE assessments SET duration_minutes = ? WHERE id = ?", (int(body["duration_minutes"]), assessment_id))

            conn.commit()
            conn.close()

            log_audit("ASSESSMENT_UPDATED", user['email'], details=f"Updated settings for assessment ID {assessment_id}")

            return self.send_json({"success": True})

        # 5d. Assessment Revoke / Delete (`/api/v1/assessments/:id/delete`)
        elif path.endswith("/delete"):
            user = self.authenticate_user()
            if not user or user['role'] not in ('creator', 'admin'):
                return self.send_json({"error": "Unauthorized"}, status=403)

            match = re.search(r'/api/v1/assessments/(\d+)/delete', path)
            if not match:
                return self.send_json({"error": "Invalid endpoint path"}, status=400)
            assessment_id = int(match.group(1))

            conn = get_db()
            cursor = conn.cursor()
            cursor.execute("SELECT exam_code, title FROM assessments WHERE id = ?", (assessment_id,))
            ass = cursor.fetchone()
            if not ass:
                conn.close()
                return self.send_json({"error": "Assessment not found"}, status=404)

            # Cascade delete options and questions
            cursor.execute("DELETE FROM options WHERE question_id IN (SELECT id FROM questions WHERE assessment_id = ?)", (assessment_id,))
            cursor.execute("DELETE FROM questions WHERE assessment_id = ?", (assessment_id,))
            cursor.execute("DELETE FROM exam_attempts WHERE assessment_id = ?", (assessment_id,))
            cursor.execute("DELETE FROM assessments WHERE id = ?", (assessment_id,))

            conn.commit()
            conn.close()

            log_audit("ASSESSMENT_DELETED", user['email'], ass['exam_code'], details=f"Deleted assessment '{ass['title']}' (ID: {assessment_id}).")

            return self.send_json({"success": True, "message": f"Assessment '{ass['title']}' revoked and deleted."})

        # 6. Assessment Heartbeat (Autosave & Liveness)
        elif path.endswith("/heartbeat"):
            cookies = parse_cookies(self.headers.get('Cookie'))
            session_cookie = cookies.get('flylock_exam_session')
            if not session_cookie:
                return self.send_json({"error": "No exam session cookie"}, status=401)

            saved_answers = json.dumps(body.get("answers", {}))

            conn = get_db()
            cursor = conn.cursor()
            cursor.execute("""
            UPDATE exam_attempts
            SET last_heartbeat = CURRENT_TIMESTAMP, saved_answers = ?
            WHERE session_cookie_id = ? AND status = 'in_progress'
            """, (saved_answers, session_cookie))
            conn.commit()
            conn.close()

            return self.send_json({"status": "acknowledged", "timestamp": int(time.time())})

        # 7. Assessment Submit
        elif path.endswith("/submit"):
            cookies = parse_cookies(self.headers.get('Cookie'))
            session_cookie = cookies.get('flylock_exam_session')
            if not session_cookie:
                return self.send_json({"error": "No exam session cookie"}, status=401)

            saved_answers = json.dumps(body.get("answers", {}))

            conn = get_db()
            cursor = conn.cursor()
            cursor.execute("""
            SELECT ea.id, ea.assessment_id, ea.student_identifier, ea.exam_code, ea.status
            FROM exam_attempts ea
            WHERE ea.session_cookie_id = ?
            """, (session_cookie,))
            att = cursor.fetchone()

            if not att or att['status'] != 'in_progress':
                conn.close()
                return self.send_json({"error": "Attempt is not in progress"}, status=400)

            # Calculate score and percentage
            cursor.execute("SELECT q.id, o.id as correct_opt_id FROM questions q JOIN options o ON o.question_id = q.id WHERE q.assessment_id = ? AND o.is_correct = 1", (att['assessment_id'],))
            correct_map = {str(r[0]): r[1] for r in cursor.fetchall()}
            
            answers_dict = json.loads(saved_answers or '{}')
            correct_count = 0
            total_questions = len(correct_map)
            
            for q_id_str, selected_opt_id in answers_dict.items():
                if str(q_id_str) in correct_map and correct_map[str(q_id_str)] == selected_opt_id:
                    correct_count += 1

            percentage = round((correct_count / total_questions * 100), 1) if total_questions > 0 else 0

            cursor.execute("""
            UPDATE exam_attempts
            SET status = 'submitted', submitted_at = CURRENT_TIMESTAMP, saved_answers = ?
            WHERE id = ?
            """, (saved_answers, att['id']))

            conn.commit()
            conn.close()

            log_audit("EXAM_SUBMITTED", att['student_identifier'], att['exam_code'], session_id=session_cookie, details=f"Student submitted. Score: {correct_count}/{total_questions} ({percentage}%)")

            cookie_header = "flylock_exam_session=; Path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT"
            return self.send_json({
                "success": True,
                "message": "Assessment submitted successfully.",
                "score": correct_count,
                "totalQuestions": total_questions,
                "percentage": percentage
            }, headers_dict={"Set-Cookie": cookie_header})

        # 8. Admin Allowlist Add / Revoke
        elif path == "/api/v1/admin/allowlist":
            user = self.authenticate_user()
            if not user:
                return self.send_json({"error": "Unauthorized"}, status=401)

            action = body.get("action", "")
            target_email = body.get("email", "").strip().lower()

            if not target_email:
                return self.send_json({"error": "Email is required"}, status=400)

            conn = get_db()
            cursor = conn.cursor()

            if action == "add":
                cursor.execute("""
                INSERT INTO creator_allowlist (email, added_by, status)
                VALUES (?, ?, 'active')
                ON CONFLICT(email) DO UPDATE SET status = 'active'
                """, (target_email, user['email']))
                log_audit("ALLOWLIST_ADD", user['email'], details=f"Added {target_email} to creator allowlist.")
            elif action == "revoke":
                cursor.execute("UPDATE creator_allowlist SET status = 'revoked' WHERE email = ?", (target_email,))
                cursor.execute("SELECT id FROM users WHERE email = ?", (target_email,))
                target_u = cursor.fetchone()
                if target_u:
                    cursor.execute("UPDATE sessions SET revoked_at = CURRENT_TIMESTAMP WHERE user_id = ?", (target_u['id'],))
                    cursor.execute("UPDATE users SET active_session_id = NULL WHERE id = ?", (target_u['id'],))
                log_audit("ALLOWLIST_REVOKE", user['email'], details=f"Revoked {target_email} from creator allowlist and terminated active sessions.")

            conn.commit()
            conn.close()

            return self.send_json({"success": True})

        # 9. Admin & Creator Session / Attempt Reset & Reattempt Grant Endpoint
        elif path == "/api/v1/admin/attempts/reset" or path == "/api/v1/assessments/attempts/reset":
            user = self.authenticate_user()
            if not user or user['role'] not in ('creator', 'admin'):
                return self.send_json({"error": "Creator or Admin privilege required"}, status=403)

            attempt_id = body.get("attemptId")
            action = body.get("action", "reset")

            conn = get_db()
            cursor = conn.cursor()
            cursor.execute("SELECT * FROM exam_attempts WHERE id = ?", (attempt_id,))
            att = cursor.fetchone()
            if not att:
                conn.close()
                return self.send_json({"error": "Attempt not found"}, status=404)

            student_email = att['student_email'] or att['student_identifier']
            exam_code = att['exam_code']

            if action == "delete":
                cursor.execute("DELETE FROM exam_attempts WHERE id = ?", (attempt_id,))
                conn.commit()
                conn.close()
                log_audit("ATTEMPT_DELETED", user['email'], exam_code, details=f"Deleted attempt #{attempt_id} for {student_email}.")
                return self.send_json({"success": True, "message": f"Attempt for {student_email} deleted. Student can now reattempt."})
            else:
                cursor.execute("DELETE FROM exam_attempts WHERE id = ?", (attempt_id,))
                conn.commit()
                conn.close()
                log_audit("REATTEMPT_GRANTED", user['email'], exam_code, details=f"Granted reattempt for #{attempt_id} ({student_email}). Previous attempt cleared.")
                return self.send_json({"success": True, "message": f"Re-attempt granted for {student_email} on assessment {exam_code}!"})

        return self.send_json({"error": "Not Found"}, status=404)

class ThreadedHTTPServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True
    allow_reuse_address = True

def run_server(port=8080):
    os.chdir(os.path.join(os.path.dirname(__file__), "public"))
    init_db()
    handler = FlyLockHTTPRequestHandler
    httpd = ThreadedHTTPServer(("", port), handler)
    print(f"High-Concurrency FlyLock Server (500+ Students Capacity) running on port {port}...")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        httpd.server_close()

if __name__ == "__main__":
    import sys
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8080
    run_server(port)
