# Security Model

---

## 1. Authentication & Authorization

### Role-Based Access Control (RBAC)

Four role types:

#### SUPER_ADMIN
- Access to all organizations, centers, rooms
- User management (create, edit, delete, ban)
- Timetable management (organization-wide)
- System configuration
- Audit logs (read all)
- Device management
- Update/rollback authority

#### CENTER_ADMIN
- Assigned center(s) only
- All rooms in assigned centers
- Timetable management (center-level)
- User management (center-only)
- Review queue (center-only)
- Lecture corrections
- Audit logs (center-level)
- Device management (assigned centers)

#### REVIEWER
- Assigned center(s) only
- Review queue (center-level)
- Approve/reject/correct individual lectures
- View audit logs (limited to their actions)
- Cannot modify timetable
- Cannot manage users
- Cannot manage devices

#### ROOM_OPERATOR
- Assigned room(s) only
- See local recording status
- See upload progress
- Limited corrections (batch/subject only)
- Cannot approve/verify
- Local-only permissions

### Authorization Matrix

| Action | SUPER_ADMIN | CENTER_ADMIN | REVIEWER | ROOM_OPERATOR |
|--------|-------------|--------------|----------|---------------|
| Create User | ✅ | ✅ (center-level) | ❌ | ❌ |
| Modify Timetable | ✅ | ✅ (assigned center) | ❌ | ❌ |
| Create Override | ✅ | ✅ (assigned center) | ❌ | ❌ |
| Review Lecture | ✅ | ✅ (assigned center) | ✅ (assigned center) | ❌ |
| Approve Review | ✅ | ✅ (assigned center) | ✅ | ❌ |
| View Audit Log | ✅ | ✅ (assigned center) | ✅ (limited) | ❌ |
| Manage Devices | ✅ | ✅ (assigned center) | ❌ | ❌ |
| View All Centers | ✅ | ❌ | ❌ | ❌ |

---

## 2. Data Isolation

### Center-Level Isolation

Every record belongs to exactly one center:

```
organization_id → center_id → room_id → device_id → lecture_session_id
```

Firestore security rules enforce this:

```javascript
match /organizations/{orgId}/centers/{centerId} {
  // User must be authorized for this center
  allow read, write: if userAuthorizedForCenter(request.auth.uid, centerId);
}
```

### User Authorization Enforcement

Every user has:
```json
{
  "userId": "U-001",
  "role": "REVIEWER",
  "authorizedCenterIds": ["C-001", "C-002"],
  "organizationId": "ORG-001"
}
```

Query enforcement:
- When user requests `/api/review-queue?centerId=C-003`
- System checks: Is C-003 in user.authorizedCenterIds?
- If NO → 403 Forbidden

### Cross-Center Contamination Prevention

Firestore index prevents:
```
User (REVIEWER, C-001, C-002) cannot read C-003 data
```

Database constraint:
```sql
-- SQLite: foreign key constraint
PRAGMA foreign_keys = ON;
```

---

## 3. Authentication Methods

### Firebase Authentication

Users authenticate via Firebase:

1. **Email/Password**:
   ```
   POST /auth/login
   Email + password → Firebase
   Returns: idToken + refreshToken
   ```

2. **Google Sign-In** (optional for future):
   ```
   User clicks "Sign in with Google"
   → OAuth 2.0 flow
   → Firebase handles token
   ```

3. **Organization-Specific SSO** (optional for enterprise):
   ```
   SAML 2.0 or OpenID Connect
   → Firebase custom claims
   ```

### Token Management

**ID Token**:
- Short-lived (1 hour)
- Sent in every API request: `Authorization: Bearer <idToken>`
- Contains: user ID, roles, authorized centers

**Refresh Token**:
- Long-lived (7 days)
- Stored securely in client local storage
- Used to get new ID token when expired
- Revocable server-side

**Token Storage (Web)**:
```javascript
// Secure local storage (httpOnly not possible in web)
// Risk: XSS can steal tokens
// Mitigation: CSP headers, input sanitization
localStorage.setItem('idToken', token);
```

**Token Storage (Windows Agent)**:
```csharp
// Windows Credential Manager (secure)
CredentialSet("LASRS_IdToken", token, CredentialType.Generic);
```

---

## 4. API Security

### HTTPS/TLS
- All APIs over HTTPS
- Certificate pinning (optional for critical endpoints)
- TLS 1.2 minimum

### Rate Limiting

Per-user, per-endpoint:
```
GET /api/review-queue: 100 req/min
POST /api/lectures: 10 req/min
GET /api/lectures/{id}: 1000 req/min
```

Exceeded → `429 Too Many Requests`

### CORS (Cross-Origin Resource Sharing)

Web frontend restricted:
```
Access-Control-Allow-Origin: https://lasrs.example.com
Access-Control-Allow-Methods: GET, POST, PUT, DELETE
Access-Control-Allow-Headers: Authorization, Content-Type
```

### API Key (for service-to-service)

Center agent → Cloud (limited):
```
POST /api/lectures
Headers: {
  "Authorization": "Bearer <idToken>",
  "X-Device-Id": "DEV-001",
  "X-Center-Id": "C-001"
}
```

Never use API keys for user endpoints.

---

## 5. Data Encryption

### In Transit
- All APIs over HTTPS (TLS 1.2+)
- Certificate pinning on Windows agent

### At Rest

**Firestore** (managed by Google):
- Encryption at rest (by default)
- Customer-managed encryption keys (CMEK) optional

**SQLite** (on center PC):
```csharp
// Windows DPAPI (Data Protection API)
var encryptedPassword = ProtectedData.Protect(
    password, 
    null, 
    DataProtectionScope.CurrentUser
);
```

**Google Drive** (managed by Google):
- Encryption at rest (by default)
- All files encrypted end-to-end

### Sensitive Fields to Encrypt

- Google Drive refresh tokens (store encrypted in SQLite)
- Teacher/student personal data (if any)
- Audit log sensitive details (configurable)

---

## 6. Google Drive OAuth

### Secure OAuth Flow

**Step 1**: Center admin initiates OAuth
```
Windows Agent → Opens browser
→ Google OAuth consent screen
→ User clicks "Allow"
```

**Step 2**: Authorization code exchanged
```
Authorization Code
  ↓
Agent (local) → Google API
  ↓
Returns: accessToken + refreshToken (short-lived)
```

**Step 3**: Tokens stored securely
```
refreshToken → Encrypted in SQLite (DPAPI)
accessToken → Memory only (expires in 1 hour)
```

**Step 4**: Upload uses token
```
Agent → Google Drive API
Authorization: Bearer <accessToken>
Upload file
```

**Step 5**: Token refresh
```
accessToken expired?
  ↓
Use refreshToken → Get new accessToken
  ↓
Continue upload (resumable)
```

### Security Requirements

✅ **NO hardcoded credentials** in code  
✅ **NO credentials in logs**  
✅ **NO credentials in config files** (except refreshToken encrypted)  
✅ **NO sharing credentials between devices**  
✅ **Refresh tokens rotated** after use  
✅ **Tokens revokable** by user  

---

## 7. Device Security

### Device Registration

1. **First-time setup**:
   ```
   Admin logs in on Windows PC
   → Agent registers device
   → Device ID generated (UUID)
   → Stored in local SQLite
   → Sent to Firestore
   ```

2. **Device identity**:
   ```json
   {
     "deviceId": "DEV-001",
     "deviceName": "Lecture Hall A Camera",
     "macAddress": "AA:BB:CC:DD:EE:FF",
     "windowsDeviceId": "...",
     "registeredAt": "2026-09-01T10:00:00Z",
     "status": "ACTIVE"
   }
   ```

### Device Revocation

If device is stolen/compromised:
```
Admin Portal:
  Centers → Devices → DEV-001
  → Click "Revoke"
  → Status = DISABLED
  → Device stops uploading
  → Google Drive access revoked
```

### Device Heartbeat

Every 5 minutes:
```
Device → Firestore: {
  "lastHeartbeat": now,
  "status": "ONLINE",
  "appVersion": "1.0.5",
  "diskFree": 500GB
}
```

Absence detection (no heartbeat for 5 min → OFFLINE alert)

---

## 8. Audit Logging

### What's Logged

✅ User login/logout  
✅ Every data modification (lecture, timetable, user, device)  
✅ Authorization denials  
✅ Failed uploads / errors  
✅ Sensitive actions (user deletion, device revocation)  
✅ API access (optional, can be high volume)  

### Audit Log Format

```json
{
  "auditEntryId": "AUD-001",
  "timestamp": "2026-09-02T11:00:00Z",
  "userId": "U-001",
  "userRole": "REVIEWER",
  "userIpAddress": "192.168.1.100",
  "action": "LECTURE_APPROVED",
  "resourceType": "LECTURE_SESSION",
  "resourceId": "LSN-2026-09-02-0001",
  "oldValues": {
    "status": "REVIEW_REQUIRED",
    "batchId": "LJ151MA"
  },
  "newValues": {
    "status": "CONFIRMED",
    "batchId": "LJ153EA"
  },
  "result": "SUCCESS",
  "resultDetail": null,
  "organizationId": "ORG-001",
  "centerId": "C-001"
}
```

### Audit Log Retention

- **Immutable**: Cannot delete/edit entries
- **Retained**: 3 years minimum
- **Queryable**: By date, user, resource, action
- **Searchable**: Encrypted at-rest, queryable via Firestore

---

## 9. Input Validation & Injection Prevention

### Validation Rules

```csharp
public class LectureCreateRequest
{
    [Required]
    [StringLength(255)]
    public string OrganizationId { get; set; }
    
    [Required]
    [StringLength(255)]
    public string CenterId { get; set; }
    
    [Range(0, 2147483647)]
    public long VideoFileSize { get; set; }
    
    [RegularExpression(@"^[A-Za-z0-9\-_.]{64}$")] // SHA-256
    public string VideoFileHash { get; set; }
}
```

### SQL Injection Prevention

Use parameterized queries:
```csharp
// ❌ WRONG
string query = $"SELECT * FROM lecture_sessions WHERE center_id = '{centerId}'";

// ✅ CORRECT
var query = "SELECT * FROM lecture_sessions WHERE center_id = @centerId";
var cmd = new SqliteCommand(query);
cmd.Parameters.AddWithValue("@centerId", centerId);
```

### XSS Prevention (Web)

- Sanitize user input (DOMPurify)
- Content Security Policy (CSP) headers
- No `innerHTML`, use `textContent`
- Escape output in templates

```javascript
// ❌ WRONG
html += `<div>${userName}</div>`;

// ✅ CORRECT
const div = document.createElement('div');
div.textContent = userName;
```

### CSRF Prevention

All state-changing requests (POST, PUT, DELETE) require CSRF token:
```html
<input type="hidden" name="_csrf" value="abc123">
```

---

## 10. Secret Management

### Configuration Secrets

```
❌ DO NOT commit to Git:
- Google OAuth credentials
- Firebase keys
- Database passwords
- API keys
- Encryption keys

✅ DO use:
- Environment variables
- Cloud Secret Manager (Google Cloud)
- Azure Key Vault (if Azure)
- HashiCorp Vault (on-prem)
```

### Example (.env.local - gitignored)

```
GOOGLE_OAUTH_CLIENT_ID=abc123.apps.googleusercontent.com
GOOGLE_OAUTH_CLIENT_SECRET=secret_xyz
FIREBASE_PROJECT_ID=lasrs-project
FIREBASE_PRIVATE_KEY=-----BEGIN PRIVATE KEY-----...
ENCRYPTION_KEY=base64:xyz
```

### Secret Rotation

- Google OAuth refresh token: auto-rotated by Google (best practice)
- App encryption keys: rotate annually
- Database passwords: rotate semi-annually

---

## 11. Compliance & Regulations

### Data Privacy

**GDPR** (if EU users):
- User consent for data processing
- Right to access personal data
- Right to erasure ("right to be forgotten")
- Data portability
- Privacy policy + terms

**FERPA** (if US educational institution):
- Student educational records protected
- Only authorized users can access
- Audit trail required

### Implementation

Firestore privacy rules:
```javascript
// Student data accessible only to authorized reviewers
match /organizations/{orgId}/centers/{centerId}/lectures/{lectureId} {
  allow read: if userAuthorizedForCenter(request.auth.uid, centerId) 
           && request.auth.token.role in ['CENTER_ADMIN', 'REVIEWER'];
}
```

Deletion policy:
```
User requests deletion → Compliance review
→ Audit log flagged (retain for legal hold)
→ User data anonymized (no deletion in audit)
→ Media data deleted from Google Drive
```

---

## 12. Incident Response

### Security Incident Protocol

1. **Detection**:
   - Anomaly alerts (e.g., excessive failed auth)
   - User reports (e.g., "My password compromised")
   - Automated monitoring (e.g., unusual API patterns)

2. **Containment**:
   ```
   Incident detected
   → Disable affected user/device immediately
   → Revoke all active tokens
   → Isolate affected center (optional)
   → Notify admin
   ```

3. **Investigation**:
   ```
   Review audit logs
   → Determine scope (which users/data affected)
   → Check for unauthorized access
   → Preserve evidence
   ```

4. **Notification**:
   ```
   If data breach:
   → Notify affected users (email)
   → Notify data protection authority (if required)
   → Public statement (if severe)
   ```

5. **Recovery**:
   ```
   Reset affected users' passwords
   → Force re-authentication
   → Regenerate API keys
   → Rotate credentials
   → Monitor for re-exploit
   ```

---

## 13. Testing Security

### Automated Security Tests

```csharp
[TestFixture]
public class SecurityTests
{
    [Test]
    public void UnauthorizedUser_CannotAccessOtherCenterData()
    {
        // User authorized for C-001
        var user = GetUser("REVIEWER", authorizedCenters: ["C-001"]);
        
        // Attempt to access C-002 data
        var result = api.GetLectures(user, centerId: "C-002");
        
        // Should fail
        Assert.AreEqual(403, result.StatusCode);
    }
    
    [Test]
    public void SqlInjection_AttemptFails()
    {
        string maliciousInput = "'; DROP TABLE lectures; --";
        var result = db.GetLectures(centerId: maliciousInput);
        
        // Should not crash or delete data
        Assert.IsNotNull(result);
    }
    
    [Test]
    public void Xss_InputSanitized()
    {
        string xssPayload = "<script>alert('xss')</script>";
        var lecture = CreateLecture(batchId: xssPayload);
        
        var html = RenderLecture(lecture);
        Assert.IsFalse(html.Contains("<script>"));
    }
}
```

### Penetration Testing

- Annual third-party pentest
- Focus: authentication, authorization, data isolation
- Simulate: XSS, CSRF, injection, privilege escalation

---

## 14. Security Checklist (Pre-Production)

- [ ] All passwords hashed (bcrypt, not MD5)
- [ ] All API endpoints authenticated
- [ ] Authorization enforced on every resource
- [ ] HTTPS/TLS enabled
- [ ] Secrets not in logs or error messages
- [ ] Rate limiting configured
- [ ] Audit logging active
- [ ] SQL injection tests pass
- [ ] XSS tests pass
- [ ] CSRF tokens implemented
- [ ] Device registration secure
- [ ] Google OAuth secure flow
- [ ] Data encryption at rest configured
- [ ] Backup encryption configured
- [ ] Incident response plan documented
- [ ] Privacy policy reviewed
- [ ] GDPR/FERPA compliance verified
- [ ] Security tests in CI/CD pipeline

---

**Status**: Security Model Phase  
**Last Updated**: 2026-09-02  
**Version**: 1.0.0-alpha
