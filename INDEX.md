# Documentation Index

Complete architecture and design documentation for the Lecture Automation & Smart Review System (LASRS).

---

## Quick Navigation

### Getting Started
- [README.md](./README.md) — Project overview, vision, and tech stack

### Architecture & Design
- [ARCHITECTURE.md](./ARCHITECTURE.md) — System architecture, components, data flow
- [DATABASE_SCHEMA.md](./DATABASE_SCHEMA.md) — SQLite and Firestore schemas
- [STATE_MACHINES.md](./STATE_MACHINES.md) — Lecture session and queue lifecycle
- [MATCHING_ENGINE_SPEC.md](./MATCHING_ENGINE_SPEC.md) — Scoring algorithm and confidence
- [API_CONTRACTS.md](./API_CONTRACTS.md) — REST API specifications (center + cloud)

### Operations & Deployment
- [DEPLOYMENT_PLAN.md](./DEPLOYMENT_PLAN.md) — Release strategy, rollout, monitoring
- [SECURITY_MODEL.md](./SECURITY_MODEL.md) — Authentication, authorization, encryption
- [COST_CONTROL.md](./COST_CONTROL.md) — Budget optimization, pricing, scaling

### Implementation
- [PROJECT_STRUCTURE.md](./PROJECT_STRUCTURE.md) — Folder hierarchy and file organization

---

## Build Phases

### Phase 1: Core Windows Agent ✅ (Documented)
- File watcher + lecture detection
- SQLite persistence
- Basic timetable matching
- Local review popup
- Local dashboard

**Reference**: ARCHITECTURE.md § 2, DATABASE_SCHEMA.md, MATCHING_ENGINE_SPEC.md

### Phase 2: Google Drive Integration ✅ (Documented)
- OAuth setup
- Resumable uploads
- Folder mapping
- Duplicate detection
- Retry logic

**Reference**: ARCHITECTURE.md § 6, API_CONTRACTS.md § 4, SECURITY_MODEL.md § 6

### Phase 3: Smart Matching ✅ (Documented)
- Confidence scoring
- Rule engine
- Ambiguity detection
- Extended lecture handling

**Reference**: MATCHING_ENGINE_SPEC.md (comprehensive)

### Phase 4: Cloud Sync ✅ (Documented)
- Firebase authentication
- Firestore sync
- Review queue
- Notifications

**Reference**: ARCHITECTURE.md § 3, API_CONTRACTS.md § Part 2, SECURITY_MODEL.md

### Phase 5: PWA Review Dashboard ✅ (Documented)
- Phone & laptop UI
- Real-time updates
- Multi-reviewer concurrency

**Reference**: ARCHITECTURE.md § 4, API_CONTRACTS.md § Review endpoints, PROJECT_STRUCTURE.md § web/

### Phase 6: Advanced Features ✅ (Documented)
- Timetable overrides
- Automated correction/move
- Audit logging
- Missing lecture detection
- Device health monitoring
- Auto-update mechanism

**Reference**: STATE_MACHINES.md § Special Cases, ARCHITECTURE.md § 9

### Phase 7: Scale Testing ✅ (Documented)
- 500 center simulation
- 10,000–20,000 lectures/day
- Concurrent uploads
- Cloud outage recovery

**Reference**: DEPLOYMENT_PLAN.md § 8, COST_CONTROL.md § 6

---

## Key Documents by Role

### For Project Managers
1. [README.md](./README.md) — Vision, scope, metrics
2. [DEPLOYMENT_PLAN.md](./DEPLOYMENT_PLAN.md) — Timeline, phases, rollout
3. [COST_CONTROL.md](./COST_CONTROL.md) — Budget and ROI

### For Architects
1. [ARCHITECTURE.md](./ARCHITECTURE.md) — System design
2. [DATABASE_SCHEMA.md](./DATABASE_SCHEMA.md) — Data model
3. [SECURITY_MODEL.md](./SECURITY_MODEL.md) — Security & compliance
4. [PROJECT_STRUCTURE.md](./PROJECT_STRUCTURE.md) — Code organization

### For Backend Developers (Agent + Cloud)
1. [ARCHITECTURE.md](./ARCHITECTURE.md) § 2–3 — Component breakdown
2. [API_CONTRACTS.md](./API_CONTRACTS.md) — API specifications
3. [DATABASE_SCHEMA.md](./DATABASE_SCHEMA.md) — Schema and queries
4. [MATCHING_ENGINE_SPEC.md](./MATCHING_ENGINE_SPEC.md) — Algorithm details
5. [PROJECT_STRUCTURE.md](./PROJECT_STRUCTURE.md) § agent/ — Code layout

### For Frontend Developers (Web)
1. [API_CONTRACTS.md](./API_CONTRACTS.md) § Part 2 — Cloud APIs
2. [PROJECT_STRUCTURE.md](./PROJECT_STRUCTURE.md) § web/ — React structure
3. [ARCHITECTURE.md](./ARCHITECTURE.md) § 4 — PWA design

### For QA & Testing
1. [DEPLOYMENT_PLAN.md](./DEPLOYMENT_PLAN.md) § 3–4 — Test strategy
2. [STATE_MACHINES.md](./STATE_MACHINES.md) — Scenarios to test
3. [ARCHITECTURE.md](./ARCHITECTURE.md) § 9 — Failure scenarios

### For Operations & DevOps
1. [DEPLOYMENT_PLAN.md](./DEPLOYMENT_PLAN.md) — Release & rollout
2. [COST_CONTROL.md](./COST_CONTROL.md) — Cost monitoring
3. [SECURITY_MODEL.md](./SECURITY_MODEL.md) § 14 — Ops checklist

---

## Critical Design Decisions

| Decision | Rationale | Reference |
|----------|-----------|-----------|
| **Edge-First Architecture** | Keep heavy processing at centers, cloud for coordination only | ARCHITECTURE.md § 1 |
| **No Central Video Storage** | Direct center → Google Drive uploads, zero bandwidth cost | ARCHITECTURE.md § 6, COST_CONTROL.md § 7 |
| **SQLite for Local DB** | Zero setup, ACID transactions, adequate for scale | ARCHITECTURE.md § 10, DATABASE_SCHEMA.md § 1 |
| **Firestore for Cloud** | Managed auth, metadata only, auto-scaling, low cost | ARCHITECTURE.md § 3, COST_CONTROL.md § 1 |
| **Confidence Scoring** | Deterministic rule-based, not LLM-driven, always human when uncertain | MATCHING_ENGINE_SPEC.md |
| **Offline-First Centers** | Continue recording/processing without internet, sync when online | ARCHITECTURE.md § 7 |
| **Immutable Audit Trail** | Every change traceable, reversible, compliance-ready | SECURITY_MODEL.md § 8 |
| **Role-Based Access** | Centers see only their data, multi-tenant isolation | SECURITY_MODEL.md § 1–2 |

---

## Technology Stack Summary

| Component | Technology | Why |
|-----------|-----------|-----|
| Agent | C# .NET 8 | Windows Services, strong typing, async/await |
| Local DB | SQLite | Zero setup, ACID, sufficient capacity |
| Cloud DB | Firestore | Managed, lightweight, auto-scaling |
| Cloud Auth | Firebase Auth | Built-in, integrates with Firestore |
| Notifications | FCM | Free, managed by Firebase |
| Video Storage | Google Drive | Direct uploads, no central cost |
| Web Framework | Next.js | SSR, PWA, TypeScript, deployment ready |
| CSS | Tailwind | Rapid UI development, responsive design |

---

## Estimated Metrics

### Scale
- **Centers**: 500
- **Daily Lectures**: 10,000–20,000
- **Users**: ~10,000 (mix of admins, reviewers, operators)
- **Rooms per Center**: 5–10
- **Devices per Room**: 1–2

### Performance
- **File Detection**: < 1 second (after stability period)
- **Matching Engine**: 90–100% accuracy on high-confidence cases
- **Upload Time**: Depends on file size, typically 5–30 minutes
- **Review Time**: 2–5 minutes per uncertain lecture
- **API Latency**: < 500ms (P95)

### Reliability
- **Cloud Uptime SLA**: 99.5%
- **Upload Success Rate**: 99% (with retries)
- **Auto-Assignment Accuracy**: 95%+
- **Center Recovery**: < 5 minutes after internet restoration

### Cost
- **Monthly Cloud**: ~$360 (500 centers)
- **Per-Center Cost**: ~$1/month
- **Video Bandwidth Cost**: $0 (direct to Google Drive)
- **Break-Even**: 1 week (if charging $50/center/month)

---

## Next Steps (After Architecture Approval)

1. **Stakeholder Review** (1 week)
   - Present architecture to stakeholders
   - Address concerns & questions
   - Get sign-off on design

2. **Detailed Specification** (1 week)
   - Write detailed API specs (OpenAPI)
   - Create database migration scripts
   - Define error codes & messages

3. **Development Setup** (1 week)
   - Initialize repositories
   - Set up CI/CD pipelines
   - Configure development environments

4. **Phase 1 Implementation** (4 weeks)
   - Windows agent core
   - SQLite setup
   - File watcher + basic matching
   - Local UI

5. **Phase 1 Testing** (1 week)
   - Unit tests
   - Integration tests
   - Manual QA

---

## Document Status

| Document | Status | Last Updated |
|----------|--------|--------------|
| README.md | ✅ Complete | 2026-09-02 |
| ARCHITECTURE.md | ✅ Complete | 2026-09-02 |
| DATABASE_SCHEMA.md | ✅ Complete | 2026-09-02 |
| STATE_MACHINES.md | ✅ Complete | 2026-09-02 |
| MATCHING_ENGINE_SPEC.md | ✅ Complete | 2026-09-02 |
| API_CONTRACTS.md | ✅ Complete | 2026-09-02 |
| SECURITY_MODEL.md | ✅ Complete | 2026-09-02 |
| DEPLOYMENT_PLAN.md | ✅ Complete | 2026-09-02 |
| COST_CONTROL.md | ✅ Complete | 2026-09-02 |
| PROJECT_STRUCTURE.md | ✅ Complete | 2026-09-02 |

**All documentation complete and ready for implementation.**

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0-alpha | 2026-09-02 | Initial comprehensive documentation package |

---

**Project**: Lecture Automation & Smart Review System (LASRS)  
**Current Phase**: Architecture & Documentation ✅  
**Next Phase**: Phase 1 Implementation (Windows Agent)  
**Status**: Ready for Development Kickoff

---

For questions or clarifications, refer to the relevant section in the documentation above.
