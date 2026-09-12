# Project Structure

Organized folder hierarchy for the Lecture Automation & Smart Review System.

---

## Root Directory

```
lecture-automation-system/
│
├── README.md
├── ARCHITECTURE.md
├── DATABASE_SCHEMA.md
├── API_CONTRACTS.md
├── STATE_MACHINES.md
├── MATCHING_ENGINE_SPEC.md
├── SECURITY_MODEL.md
├── DEPLOYMENT_PLAN.md
├── COST_CONTROL.md
├── PROJECT_STRUCTURE.md (this file)
│
├── .gitignore
├── LICENSE
├── .github/
│   └── workflows/
│       ├── test.yml
│       ├── lint.yml
│       └── deploy.yml
│
├── docs/
│   ├── user-guide.md
│   ├── admin-guide.md
│   ├── api-reference.md
│   ├── troubleshooting.md
│   └── faq.md
│
├── agent/                          # Windows Agent (C# .NET 8)
├── cloud/                          # Cloud Backend (Firebase, Cloud Functions)
├── web/                            # Web Dashboard (Next.js)
├── shared/                         # Shared code & utilities
└── tests/                          # End-to-end & integration tests
```

---

## Agent (Windows Agent)

### Directory Structure

```
agent/
│
├── LectureAgent.sln                # Solution file
├── LectureAgent/                   # Main application
│   └── Project.csproj
│
├── Directory.Build.props           # Shared build configuration
├── nuget.config                    # NuGet package sources
│
├── src/
│   ├── Domain/                     # Business logic (pure)
│   │   ├── Entities/
│   │   │   ├── LectureSession.cs
│   │   │   ├── TimetableEntry.cs
│   │   │   ├── UploadQueueEntry.cs
│   │   │   ├── Device.cs
│   │   │   ├── AuditLogEntry.cs
│   │   │   └── Config.cs
│   │   │
│   │   ├── ValueObjects/
│   │   │   ├── ConfidenceScore.cs
│   │   │   ├── FileHash.cs
│   │   │   ├── DriveFileId.cs
│   │   │   └── SessionId.cs
│   │   │
│   │   ├── Enums/
│   │   │   ├── LectureStatus.cs
│   │   │   ├── ReviewStatus.cs
│   │   │   ├── UploadStatus.cs
│   │   │   └── MatchingDecision.cs
│   │   │
│   │   └── Services/              # Business rules
│   │       ├── MatchingEngine.cs
│   │       ├── ConfidenceScorer.cs
│   │       ├── FileValidator.cs
│   │       ├── TimetableResolver.cs
│   │       └── UploadQueueManager.cs
│   │
│   ├── Application/                # Use cases / orchestration
│   │   ├── Services/
│   │   │   ├── LectureSessionService.cs
│   │   │   ├── FileWatcherService.cs
│   │   │   ├── MatchingService.cs
│   │   │   ├── UploadService.cs
│   │   │   ├── GoogleDriveService.cs
│   │   │   ├── TimetableSyncService.cs
│   │   │   ├── ReviewQueueService.cs
│   │   │   ├── AuditLogService.cs
│   │   │   └── ConfigService.cs
│   │   │
│   │   ├── DTOs/
│   │   │   ├── CreateLectureSessionRequest.cs
│   │   │   ├── MatchingResultDTO.cs
│   │   │   ├── UploadProgressDTO.cs
│   │   │   └── ConfirmLectureRequest.cs
│   │   │
│   │   └── Interfaces/
│   │       ├── IFileWatcher.cs
│   │       ├── IMatchingEngine.cs
│   │       ├── IGoogleDriveUploader.cs
│   │       ├── ITimetableProvider.cs
│   │       ├── INotificationService.cs
│   │       ├── IAuditLogger.cs
│   │       └── ICloudSyncService.cs
│   │
│   ├── Infrastructure/             # External integrations
│   │   ├── Database/
│   │   │   ├── LectureContext.cs   # EF Core DbContext
│   │   │   ├── Migrations/
│   │   │   │   ├── 001_InitialCreate.cs
│   │   │   │   └── 002_AddTimetableOverrides.cs
│   │   │   └── SqliteRepository.cs
│   │   │
│   │   ├── GoogleDrive/
│   │   │   ├── GoogleDriveUploader.cs
│   │   │   ├── GoogleOAuthHandler.cs
│   │   │   ├── DriveFileProcessor.cs
│   │   │   └── ResumableUploadManager.cs
│   │   │
│   │   ├── Firebase/
│   │   │   ├── FirestoreSync.cs
│   │   │   ├── FirebaseAuthService.cs
│   │   │   ├── FirebaseNotificationService.cs
│   │   │   └── FirebaseCloudMessagingHandler.cs
│   │   │
│   │   ├── FileSystem/
│   │   │   ├── FileWatcher.cs
│   │   │   ├── FileValidator.cs
│   │   │   ├── VideoMetadataExtractor.cs
│   │   │   └── FileSystemHelper.cs
│   │   │
│   │   ├── Logging/
│   │   │   ├── StructuredLogger.cs
│   │   │   ├── AuditLogger.cs
│   │   │   └── Serilog configuration
│   │   │
│   │   └── HTTP/
│   │       ├── HttpClientConfiguration.cs
│   │       └── RetryPolicy.cs (Polly)
│   │
│   ├── Presentation/               # Windows Service + REST API
│   │   ├── Program.cs
│   │   ├── Startup.cs
│   │   │
│   │   ├── Controllers/
│   │   │   ├── LecturesController.cs
│   │   │   ├── UploadQueueController.cs
│   │   │   ├── TimetableController.cs
│   │   │   ├── MatchingController.cs
│   │   │   ├── ConfigController.cs
│   │   │   ├── HealthController.cs
│   │   │   └── ReviewController.cs
│   │   │
│   │   ├── UI/
│   │   │   ├── MainWindow.xaml      # Main dashboard UI
│   │   │   ├── MainWindow.xaml.cs
│   │   │   ├── Views/
│   │   │   │   ├── DashboardView.xaml
│   │   │   │   ├── UploadQueueView.xaml
│   │   │   │   ├── ReviewPopup.xaml
│   │   │   │   ├── ErrorsView.xaml
│   │   │   │   └── SettingsView.xaml
│   │   │   │
│   │   │   └── ViewModels/
│   │   │       ├── DashboardViewModel.cs
│   │   │       ├── ReviewPopupViewModel.cs
│   │   │       └── SettingsViewModel.cs
│   │   │
│   │   └── Middleware/
│   │       ├── ErrorHandlingMiddleware.cs
│   │       ├── AuthenticationMiddleware.cs
│   │       └── LoggingMiddleware.cs
│   │
│   └── Utilities/
│       ├── Extensions/
│       │   ├── DateTimeExtensions.cs
│       │   ├── StringExtensions.cs
│       │   └── CollectionExtensions.cs
│       │
│       ├── Helpers/
│       │   ├── HashHelper.cs
│       │   ├── EncryptionHelper.cs
│       │   ├── JsonHelper.cs
│       │   └── ValidationHelper.cs
│       │
│       └── Constants/
│           ├── ErrorMessages.cs
│           ├── ConfigKeys.cs
│           └── StatusMessages.cs
│
├── tests/
│   ├── LectureAgent.UnitTests/
│   │   ├── Domain/
│   │   │   └── MatchingEngineTests.cs
│   │   │   └── ConfidenceScorerTests.cs
│   │   │   └── FileValidatorTests.cs
│   │   │
│   │   ├── Application/
│   │   │   └── LectureSessionServiceTests.cs
│   │   │   └── MatchingServiceTests.cs
│   │   │
│   │   └── Infrastructure/
│   │       └── SqliteRepositoryTests.cs
│   │
│   ├── LectureAgent.IntegrationTests/
│   │   ├── FileWatcherIntegrationTests.cs
│   │   ├── DatabaseIntegrationTests.cs
│   │   ├── GoogleDriveIntegrationTests.cs
│   │   └── EndToEndTests.cs
│   │
│   └── Fixtures/
│       ├── MockFileSystem.cs
│       ├── MockGoogleDrive.cs
│       └── TestDataBuilder.cs
│
├── config/
│   ├── appsettings.json            # Default configuration
│   ├── appsettings.Development.json
│   ├── appsettings.Production.json
│   ├── nlog.config                 # Logging configuration
│   └── serilog.config              # Structured logging config
│
├── scripts/
│   ├── install.ps1                 # Installation script
│   ├── uninstall.ps1               # Uninstallation script
│   ├── setup-oauth.ps1             # Google OAuth setup
│   └── create-windows-service.ps1
│
└── README.md                        # Agent-specific README
```

---

## Cloud (Firebase Backend)

### Directory Structure

```
cloud/
│
├── functions/                      # Cloud Functions (Node.js/TypeScript)
│   ├── src/
│   │   ├── index.ts                # Entry point
│   │   │
│   │   ├── handlers/
│   │   │   ├── timetableOverrideHandler.ts
│   │   │   ├── lectureCreatedHandler.ts
│   │   │   ├── uploadStatusHandler.ts
│   │   │   └── notificationHandler.ts
│   │   │
│   │   ├── services/
│   │   │   ├── notificationService.ts
│   │   │   ├── auditService.ts
│   │   │   ├── firestoreService.ts
│   │   │   └── garbageCollectionService.ts
│   │   │
│   │   └── utils/
│   │       ├── logger.ts
│   │       ├── errorHandler.ts
│   │       └── validators.ts
│   │
│   ├── tests/
│   │   ├── handlers.test.ts
│   │   └── services.test.ts
│   │
│   ├── package.json
│   ├── tsconfig.json
│   └── .env.example
│
├── firestore/
│   ├── firestore.rules             # Firestore security rules
│   ├── firestore.indexes.json      # Composite indexes
│   └── collections/                # Collection schemas (documentation)
│       ├── organizations.md
│       ├── centers.md
│       ├── lectures.md
│       ├── reviewQueue.md
│       ├── auditLog.md
│       └── users.md
│
├── storage/
│   └── storage.rules               # Firebase Storage rules (if used)
│
└── firebase.json                   # Firebase configuration
```

---

## Web (Next.js Dashboard)

### Directory Structure

```
web/
│
├── public/                         # Static assets
│   ├── icons/
│   │   ├── logo.svg
│   │   └── favicon.ico
│   ├── images/
│   └── manifest.json              # PWA manifest
│
├── src/
│   ├── pages/
│   │   ├── _app.tsx               # App wrapper
│   │   ├── _document.tsx          # Document template
│   │   ├── index.tsx              # Home/redirect
│   │   │
│   │   ├── login.tsx
│   │   ├── register.tsx
│   │   │
│   │   ├── dashboard/
│   │   │   ├── index.tsx          # Main dashboard
│   │   │   ├── [centerId].tsx     # Center view
│   │   │   └── [centerId]/
│   │   │       ├── rooms.tsx
│   │   │       └── devices.tsx
│   │   │
│   │   ├── review/
│   │   │   ├── index.tsx          # Review queue
│   │   │   └── [lectureId].tsx    # Lecture detail
│   │   │
│   │   ├── admin/
│   │   │   ├── index.tsx
│   │   │   ├── centers.tsx
│   │   │   ├── users.tsx
│   │   │   ├── timetable.tsx
│   │   │   └── audit-logs.tsx
│   │   │
│   │   ├── health/
│   │   │   ├── devices.tsx
│   │   │   └── system.tsx
│   │   │
│   │   └── api/                   # Next.js API routes
│   │       ├── auth/
│   │       │   ├── login.ts
│   │       │   ├── logout.ts
│   │       │   └── refresh.ts
│   │       └── lectures/
│   │           └── [id].ts
│   │
│   ├── components/
│   │   ├── common/
│   │   │   ├── Header.tsx
│   │   │   ├── Sidebar.tsx
│   │   │   ├── Footer.tsx
│   │   │   ├── Navigation.tsx
│   │   │   └── Loading.tsx
│   │   │
│   │   ├── dashboard/
│   │   │   ├── Summary.tsx
│   │   │   ├── StatusChart.tsx
│   │   │   └── LectureTable.tsx
│   │   │
│   │   ├── review/
│   │   │   ├── ReviewQueue.tsx
│   │   │   ├── LectureReviewCard.tsx
│   │   │   ├── LectureDetail.tsx
│   │   │   ├── MatchingReason.tsx
│   │   │   └── ReviewActions.tsx
│   │   │
│   │   ├── admin/
│   │   │   ├── CenterManagement.tsx
│   │   │   ├── UserManagement.tsx
│   │   │   ├── TimetableEditor.tsx
│   │   │   └── AuditLogViewer.tsx
│   │   │
│   │   └── forms/
│   │       ├── LoginForm.tsx
│   │       ├── BatchSelector.tsx
│   │       ├── SubjectSelector.tsx
│   │       └── OverrideForm.tsx
│   │
│   ├── services/
│   │   ├── api.ts                 # API client
│   │   ├── authService.ts
│   │   ├── lectureService.ts
│   │   ├── firestoreService.ts
│   │   └── notificationService.ts
│   │
│   ├── hooks/
│   │   ├── useAuth.ts
│   │   ├── useLectures.ts
│   │   ├── useReviewQueue.ts
│   │   └── useFirestore.ts
│   │
│   ├── context/
│   │   ├── AuthContext.tsx
│   │   ├── UserContext.tsx
│   │   └── AppContext.tsx
│   │
│   ├── styles/
│   │   ├── globals.css             # Tailwind imports
│   │   └── layout.module.css
│   │
│   ├── types/
│   │   ├── index.ts                # Shared types
│   │   ├── lecture.ts
│   │   ├── user.ts
│   │   ├── timetable.ts
│   │   └── api.ts
│   │
│   └── utils/
│       ├── formatters.ts
│       ├── validators.ts
│       ├── datetime.ts
│       └── storage.ts
│
├── public/
│   └── service-worker.js          # PWA offline support
│
├── tests/
│   ├── __mocks__/
│   │   └── firebase.ts
│   ├── pages/
│   │   └── login.test.tsx
│   └── components/
│       └── ReviewQueue.test.tsx
│
├── package.json
├── tsconfig.json
├── tailwind.config.js
├── next.config.js
├── .env.example
└── README.md
```

---

## Shared Code

### Directory Structure

```
shared/
│
├── models/                         # Shared data models
│   ├── LectureSession.ts
│   ├── TimetableEntry.ts
│   ├── User.ts
│   └── AuditLog.ts
│
├── constants/
│   ├── statusConstants.ts
│   ├── roleConstants.ts
│   └── errorCodes.ts
│
├── types/
│   ├── api.ts                     # Shared API types
│   ├── auth.ts
│   └── errors.ts
│
├── utils/
│   ├── validation.ts
│   ├── formatting.ts
│   └── datetime.ts
│
└── package.json                    # If published as npm package
```

---

## Tests (End-to-End & Integration)

### Directory Structure

```
tests/
│
├── e2e/
│   ├── lecture-flow.spec.ts        # Full lecture lifecycle
│   ├── review-flow.spec.ts         # Reviewer workflow
│   ├── timetable-correction.spec.ts
│   └── upload-retry.spec.ts
│
├── integration/
│   ├── firebase.test.ts            # Firebase integration
│   ├── google-drive.test.ts        # Drive integration
│   └── center-agent.test.ts        # Agent communication
│
├── performance/
│   ├── matching-performance.test.ts
│   ├── database-performance.test.ts
│   └── load.test.ts               # Load testing
│
├── fixtures/
│   ├── sample-lectures.json
│   ├── sample-timetables.json
│   └── test-data-generator.ts
│
├── config/
│   ├── jest.config.js
│   └── cypress.config.js
│
├── .env.test
└── README.md
```

---

## Documentation

### Directory Structure

```
docs/
│
├── user-guide.md                  # For reviewers & operators
├── admin-guide.md                 # For center admins
├── api-reference.md               # API documentation
├── troubleshooting.md             # Common issues & solutions
├── faq.md                         # Frequently asked questions
│
├── installation/
│   ├── windows-agent-setup.md
│   ├── firebase-setup.md
│   ├── google-drive-oauth.md
│   └── web-deployment.md
│
├── development/
│   ├── dev-environment-setup.md
│   ├── architecture-overview.md
│   ├── contributing.md
│   └── coding-standards.md
│
└── operations/
    ├── monitoring.md
    ├── alerting.md
    ├── backup-recovery.md
    └── disaster-recovery.md
```

---

## Version Control

### .gitignore Entries

```
# Sensitive
.env
.env.local
*.key
*.pem
secrets/
credentials/

# Build outputs
agent/bin/
agent/obj/
web/.next/
web/out/
cloud/functions/lib/

# Dependencies
node_modules/
packages/

# IDE
.vscode/
.idea/
*.swp
*.swo

# OS
.DS_Store
Thumbs.db

# Logs
logs/
*.log

# Test coverage
coverage/
```

---

## Build Pipeline

### GitHub Actions Workflows

```
.github/workflows/
├── test.yml              # Run tests on every PR
├── lint.yml              # Lint code
├── security.yml          # Security scan
└── deploy.yml            # Deploy to production
```

---

## Naming Conventions

### Files
- **C# files**: `PascalCase.cs` (ClassName.cs)
- **TypeScript/JavaScript**: `camelCase.ts` or `PascalCase.tsx` (components)
- **Documentation**: `kebab-case.md`

### Classes & Interfaces
- **Classes**: `PascalCase` (e.g., `LectureSessionService`)
- **Interfaces**: `IPascalCase` (e.g., `IFileWatcher`)
- **Enums**: `PascalCase` (e.g., `LectureStatus`)

### Methods & Properties
- **Public**: `PascalCase`
- **Private**: `_camelCase` or `_PascalCase`

### Folders
- **PascalCase** for domain/feature folders
- **kebab-case** for utility/config folders

---

## Deployment Artifacts

### Agent
```
Releases/
└── LectureAgent-1.0.0.msi          # Windows installer
```

### Web
```
Firebase Hosting
└── https://lasrs-prod.web.app      # PWA dashboard
```

### Cloud
```
Cloud Functions
├── us-central1/timetableOverride/
├── us-central1/lectureCreated/
└── us-central1/notifications/
```

---

**Status**: Project Structure Phase  
**Last Updated**: 2026-09-02  
**Version**: 1.0.0-alpha
