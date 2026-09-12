# Deployment Plan

---

## 1. Pre-Deployment Checklist

### Code Quality
- [ ] All unit tests pass (>80% coverage)
- [ ] All integration tests pass
- [ ] Linting passes (no style violations)
- [ ] Security scan passes (OWASP Top 10)
- [ ] Performance tests pass (SLA compliance)
- [ ] No hardcoded secrets/credentials

### Documentation
- [ ] Architecture documented
- [ ] API documented (OpenAPI/Swagger)
- [ ] Database schema documented
- [ ] Deployment guide written
- [ ] Troubleshooting guide written
- [ ] User manual (reviewer) written

### Infrastructure
- [ ] Firebase project created & configured
- [ ] Google Cloud project created
- [ ] Database (Firestore) initialized
- [ ] CDN configured (for PWA)
- [ ] Secrets management configured
- [ ] Monitoring & alerting configured
- [ ] Backup configured

### Security
- [ ] SSL/TLS certificates obtained
- [ ] OAuth keys configured
- [ ] Security audit completed
- [ ] Penetration test passed
- [ ] Data privacy review passed
- [ ] GDPR/FERPA compliance verified

---

## 2. Deployment Environments

### Dev Environment
- **Purpose**: Active development, testing
- **Firebase**: Separate dev project
- **Data**: Test data only, no production
- **Access**: Developers + QA
- **SLA**: None

### Staging Environment
- **Purpose**: Pre-production validation
- **Firebase**: Separate staging project (or same with separate DB)
- **Data**: Sanitized production data (no PII)
- **Access**: QA, limited reviewers
- **SLA**: Best-effort (may be unavailable for deployment)
- **Testing**: Load testing, chaos engineering

### Production Environment
- **Purpose**: Live system for 500+ centers
- **Firebase**: Production project
- **Data**: Real operational data
- **Access**: End users + support staff
- **SLA**: 99.5% uptime
- **Monitoring**: Full observability

---

## 3. Release Strategy

### Versioning
Use semantic versioning: `MAJOR.MINOR.PATCH`

Example:
- `1.0.0`: Initial release
- `1.1.0`: New feature
- `1.1.1`: Bug fix

### Release Phases

#### Phase 1: Alpha (Internal)
- **Timeline**: Week 1–4
- **Participants**: Development team
- **Focus**: Core functionality, basic testing
- **Deliverables**:
  - Windows agent (file watcher, matching)
  - Firebase auth & Firestore setup
  - Local SQLite schemas
  - Google Drive OAuth integration

#### Phase 2: Beta (Pilot Centers)
- **Timeline**: Week 5–8
- **Participants**: 2–3 pilot centers (50 users)
- **Focus**: Real-world testing, edge cases
- **Deployment**:
  - Gradual rollout
  - Daily monitoring
  - Weekly feedback sessions

#### Phase 3: RC (Release Candidate)
- **Timeline**: Week 9–10
- **Participants**: 10–15 centers (200 users)
- **Focus**: Load testing, stability
- **Monitoring**: CPU, memory, database performance

#### Phase 4: GA (General Availability)
- **Timeline**: Week 11+
- **Participants**: All 500 centers
- **Rollout**: Staggered (50 centers/day)
- **Monitoring**: 24/7 alerting

---

## 4. Windows Agent Deployment

### Distribution Method

**Option A**: Installer (.msi)
```
Center admin downloads LectureAgent-1.0.0.msi
Runs installer → Setup wizard
→ Configures organization ID, center ID
→ Registers device
→ Starts Windows Service
```

**Option B**: GitHub Releases
```
Publish signed .exe or .msi to GitHub Releases
Center admin downloads & runs
→ Auto-updates enabled
```

**Option C**: Windows Package Manager
```
winget install lasrs-agent
```

### Installation Steps

1. **Download**:
   ```
   https://releases.lasrs.example.com/LectureAgent-1.0.0.msi
   SHA-256: abc123... (verify signature)
   ```

2. **Install**:
   ```
   msiexec /i LectureAgent-1.0.0.msi
   /q (quiet, no UI)
   /norestart (or /forcerestart)
   ```

3. **Post-Installation**:
   ```
   Windows Service "LectureAgentService" starts automatically
   App creates/initializes SQLite database
   App generates device ID
   App registers device with Firestore
   ```

4. **Configuration**:
   ```
   Admin opens settings:
   - Organization ID: ORG-001
   - Center ID: C-001
   - Room ID: R-001
   - Recording folder: D:\Recordings
   - Google Drive folder: /Center-001
   ```

5. **Google Drive OAuth**:
   ```
   Admin clicks "Connect to Google Drive"
   → Browser opens OAuth consent
   → User approves
   → Refresh token stored in encrypted local storage
   → Upload ready
   ```

### Automatic Updates

On startup:
```csharp
if (currentVersion < latestVersion) {
    // Check release notes
    if (isMinorOrPatchUpdate) {
        // Auto-download & update
        DownloadAndInstall(latestVersion);
        RestartService();
    } else if (isMajorUpdate) {
        // Show notification
        NotifyUser("Major update available");
        // User manually updates
    }
}
```

### Rollback

If release is unstable:
```
DevOps → Stop rollout to remaining centers
→ Notify deployed centers
→ Provide rollback script
Center admin → Runs rollback
→ App downgrades to previous version
→ Service restarts
```

---

## 5. Web Dashboard Deployment

### Build & Package

```bash
cd web
npm run build
# Outputs: out/ (Next.js static export)

# Package
zip -r lasrs-web-1.0.0.zip out/
# Upload to Firebase Hosting
firebase deploy --only hosting
```

### Firebase Hosting

```javascript
// firebase.json
{
  "hosting": {
    "public": "out",
    "ignore": ["firebase.json", "**/node_modules/**"],
    "redirects": [],
    "rewrites": [
      {
        "source": "**",
        "destination": "/index.html"
      }
    ]
  }
}
```

Deploy:
```bash
firebase deploy --only hosting
# Deployed to: https://lasrs-prod.web.app
```

### CDN & Caching

```
Firebase Hosting automatically:
- Caches static assets (30 days)
- Serves from global CDN
- Invalidates cache on redeploy
```

### PWA Service Worker

```javascript
// public/service-worker.js
self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open('v1').then((cache) => {
      return cache.addAll([
        '/',
        '/offline.html',
        '/manifest.json'
      ]);
    })
  );
});
```

---

## 6. Cloud Backend Deployment

### Firebase Configuration

```bash
firebase init
firebase projects:list
firebase use lasrs-prod

firebase deploy --only firestore
firebase deploy --only functions
firebase deploy --only storage
firebase deploy --only hosting
```

### Firestore Rules Deployment

```
Update security rules:
firestore.rules → Deploy via Firebase CLI
→ Active immediately
→ No downtime

Verification: Test rules in Firestore Emulator
```

### Cloud Functions Deployment

```bash
cd functions
npm install
firebase deploy --only functions
# Deploy to: us-central1

# Monitor:
firebase functions:log
# Check errors, latency, memory usage
```

---

## 7. Database Migration

### SQLite (Center-Local)

**First-time setup**:
```csharp
public class DbMigration
{
    public static void Initialize()
    {
        using var db = new LectureContext();
        db.Database.EnsureCreated(); // Create if not exists
        db.Database.Migrate(); // Apply pending migrations
    }
}
```

**Upgrading versions**:
```
Agent 1.0 → 1.1 (adds new table)
→ App detects old schema
→ Runs migration script
→ Backs up old database
→ Applies new schema
→ Verifies integrity
```

### Firestore (Cloud)

**Collection creation**:
```javascript
// Cloud Function (one-time)
async function createCollections() {
  const org = await db.collection('organizations').add({
    name: 'Organization ABC',
    createdAt: FieldValue.serverTimestamp()
  });
  return org.id;
}
```

**No schema validation needed** (Firestore is schemaless).

---

## 8. Monitoring & Alerting

### Application Metrics

Monitor:
- Request latency (API response time)
- Error rate (5xx responses)
- Active users (concurrent)
- Upload throughput (MB/s)
- Matching confidence distribution
- Review queue size

### Infrastructure Metrics

Monitor:
- Firestore reads/writes per minute
- Storage (Firestore & Drive quotas)
- Cloud Function execution time
- CPU & memory (if using VMs)
- Network bandwidth

### Alerting Thresholds

```
Alert if:
- API error rate > 5% for 5 minutes
- P99 latency > 5 seconds
- Review queue > 1000 items (unclearly processed)
- Upload failures > 10% for 30 minutes
- Device offline > 50% (center-level)
- Firestore throttled (rate limited)
```

### Dashboards

**Operations**:
- Center status (online/offline)
- Daily recordings processed
- Review queue depth
- Upload success rate

**Errors**:
- Recent errors
- Error patterns
- Failed uploads (by center)
- User complaints

---

## 9. Rollout Schedule (500 Centers)

### Week 1: Staging (5 centers)
- Pilot partners
- Daily standups
- Monitoring 24/7
- Ready to rollback

### Week 2–3: Early Adopters (25 centers)
- Tech-savvy centers
- Bi-weekly feedback
- Monitor for issues
- Document learnings

### Week 4–5: Expansion (100 centers)
- Diverse mix of centers
- Weekly region-based rollout
- Automated deployments
- Support team ready

### Week 6–8: General Rollout (370 centers)
- 50 centers per day
- Production monitoring
- Escalation procedures
- Success criteria per center

### Week 9+: Steady State
- All centers online
- Continuous monitoring
- Bug fixes (patch updates)
- Feature development (next phase)

---

## 10. Support & SLA

### Support Tiers

| Level | Response | Resolution | Target |
|-------|----------|-----------|--------|
| Tier 1 | 15 min | 4 hours | Device offline, upload failure |
| Tier 2 | 1 hour | 24 hours | Review queue issues, matching bugs |
| Tier 3 | 4 hours | 72 hours | Feature requests, minor bugs |

### SLA Targets

- **Availability**: 99.5% (21.9 hours downtime/month)
- **Upload Success**: 99% (retry until success)
- **Matching Accuracy**: 95% (for auto-assign)
- **Review Time**: 90% processed within 4 hours

---

## 11. Post-Launch Monitoring (First Month)

### Daily Checks

- [ ] All 500 centers online?
- [ ] Error rate < 5%?
- [ ] Upload backlog < 1000?
- [ ] No critical bugs?
- [ ] User satisfaction > 8/10?

### Weekly Reviews

- [ ] Upload throughput meets SLA?
- [ ] Matching accuracy > 95%?
- [ ] Review queue clearing?
- [ ] Cost within budget?
- [ ] Performance regressions?

### Action Items

If any check fails:
- Incident investigation
- Root cause analysis
- Hotfix (if needed)
- Postmortem + learnings
- Plan prevention

---

**Status**: Deployment Phase  
**Last Updated**: 2026-09-02  
**Version**: 1.0.0-alpha
