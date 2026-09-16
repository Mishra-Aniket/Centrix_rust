import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Activity,
  Building2,
  Calendar,
  CheckCircle2,
  FolderEdit,
  Plus,
  Radio,
  RefreshCw,
  Settings2,
} from 'lucide-react';
import type {
  AgentInfo,
  ControlState,
  HealthStatus,
  LectureSession,
  MissingSlot,
  MonitorSnapshot,
  QueueEntry,
  RoomOverview,
  TimetableEntry,
  TimetableOverride,
  TimetableSummary,
} from './types';
import * as api from './api';
import { clearApiKey, isConfigured, onUnauthorized } from './config';
import { LoginScreen } from './screens/Login';
import { LiveScreen } from './screens/Live';
import { ReviewScreen } from './screens/Review';
import { ScheduleScreen } from './screens/Schedule';
import { ControlsScreen } from './screens/Controls';
import { CenterScreen } from './screens/Center';
import { StudioLiveScreen } from './screens/StudioLive';
import { DriveFolderPicker } from './components/DriveFolderPicker';
import { VideoThumbnail } from './components/VideoThumbnail';
import { ActionButton, Field, Modal, inputClass } from './ui';
import { isTauri, getServiceStatus, readSettings, notify, getBandwidthSettings } from './tauri';
import { SetupWizard } from './screens/SetupWizard';

type Tab = 'live' | 'review' | 'schedule' | 'center' | 'studio' | 'controls';

export function App() {
  const [needsSetup, setNeedsSetup] = useState(false);
  const [checkingSetup, setCheckingSetup] = useState(isTauri());
  const [authed, setAuthed] = useState(isConfigured());

  useEffect(() => {
    if (!isTauri()) return;
    readSettings().then((settings) => {
      const apiKey = (settings?.Auth as any)?.ApiKey;
      if (!apiKey) {
        setNeedsSetup(true);
      }
      setCheckingSetup(false);
    }).catch(() => setCheckingSetup(false));
  }, []);

  useEffect(
    () =>
      onUnauthorized(() => {
        setAuthed(false);
      }),
    []
  );

  if (checkingSetup) {
    return <div className="flex items-center justify-center h-screen bg-[#1b1033]"><div className="text-white text-lg">Loading Centrix...</div></div>;
  }

  if (needsSetup) {
    return <SetupWizard onComplete={() => { setNeedsSetup(false); setAuthed(true); }} />;
  }

  if (!authed) {
    return <LoginScreen onConnected={() => setAuthed(true)} />;
  }

  return <Dashboard onLogout={() => { clearApiKey(); setAuthed(false); }} />;
}

function Dashboard({ onLogout }: { onLogout: () => void }) {
  const [activeTab, setActiveTab] = useState<Tab>('live');
  const [health, setHealth] = useState<HealthStatus | null>(null);
  const [snapshot, setSnapshot] = useState<MonitorSnapshot | null>(null);
  const [agentInfo, setAgentInfo] = useState<AgentInfo | null>(null);
  const [controlState, setControlState] = useState<ControlState | null>(null);
  const [lectures, setLectures] = useState<LectureSession[]>([]);
  const [timetable, setTimetable] = useState<TimetableEntry[]>([]);
  const [overrides, setOverrides] = useState<TimetableOverride[]>([]);
  const [missingSlots, setMissingSlots] = useState<MissingSlot[]>([]);
  const [centerOverview, setCenterOverview] = useState<RoomOverview[]>([]);
  const [selectedRoom, setSelectedRoom] = useState('');
  const [selectedDate, setSelectedDate] = useState<string>(() => {
    const d = new Date();
    const year = d.getFullYear();
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  });
  const [availableDates, setAvailableDates] = useState<string[]>([]);
  const [timetableRooms, setTimetableRooms] = useState<string[]>([]);
  const [timetableSummary, setTimetableSummary] = useState<TimetableSummary | null>(null);
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [toast, setToast] = useState<string | null>(null);
  const [serviceState, setServiceState] = useState<string>('unknown');

  useEffect(() => {
    if (!isTauri()) return;
    const poll = async () => {
      try {
        const status = await getServiceStatus();
        setServiceState(status.state);
      } catch { setServiceState('unknown'); }
    };
    poll();
    const interval = setInterval(poll, 3000);
    return () => clearInterval(interval);
  }, []);

  const agentInfoRef = useRef<AgentInfo | null>(null);
  const effectiveRoom = selectedRoom || agentInfo?.roomId || '603';

  const showToast = useCallback((msg: string) => {
    setToast(msg);
    setTimeout(() => setToast(null), 3500);
  }, []);

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      const info = await api.fetchAgentInfo().catch(() => null);
      if (info) {
        agentInfoRef.current = info;
        setAgentInfo(info);
      }

      const center = info?.centerId || agentInfoRef.current?.centerId || '';
      const room = selectedRoom || info?.roomId || '603';

      const [hData, sData, lData, tData, oData, cData, mData, datesData, roomsData, summaryData] = await Promise.all([
        api.fetchHealth().catch(() => null),
        api.fetchSnapshot(24, room).catch(() => null),
        api.fetchLectures(center, 30).catch(() => null),
        center ? api.fetchTimetable(center, room, selectedDate).catch(() => null) : Promise.resolve(null),
        center ? api.fetchOverrides(center, room, selectedDate).catch(() => null) : Promise.resolve(null),
        api.fetchControlState().catch(() => null),
        center ? api.fetchMissingLectures(center, room === 'ALL' ? (info?.roomId || '603') : room).catch(() => null) : Promise.resolve(null),
        center ? api.fetchTimetableDates(center, room).catch(() => null) : Promise.resolve(null),
        center ? api.fetchTimetableRooms(center).catch(() => null) : Promise.resolve(null),
        center ? api.fetchTimetableSummary(center).catch(() => null) : Promise.resolve(null),
      ]);

      if (hData) setHealth(hData);
      if (sData) setSnapshot(sData);
      if (lData) setLectures(lData.items);
      if (tData) setTimetable(tData);
      if (oData) setOverrides(oData);
      if (cData) setControlState(cData);
      if (datesData) setAvailableDates(datesData);
      if (roomsData) setTimetableRooms(roomsData);
      if (summaryData) setTimetableSummary(summaryData);
      setMissingSlots(mData ?? []);
    } catch (err) {
      console.error('Failed to load data:', err);
    } finally {
      setLoading(false);
    }
  }, [selectedRoom, selectedDate]);

  // --- Native Notification Tracking ---
  const notifiedIdsRef = useRef<Set<string>>(new Set());

  // Fire native notifications when data changes
  useEffect(() => {
    if (!isTauri()) return;

    // Notify for new ReviewRequired lectures
    for (const l of lectures) {
      if (
        (l.status === 'ReviewRequired' || l.reviewStatus === 'Pending') &&
        !notifiedIdsRef.current.has(`review-${l.lectureSessionId}`)
      ) {
        notifiedIdsRef.current.add(`review-${l.lectureSessionId}`);
        notify(
          '📋 Lecture Needs Review',
          `${l.batchId || 'Unknown Batch'} / ${l.subjectId || 'Unknown Subject'} — Confidence: ${l.confidenceScore}%`
        );
      }
    }

    // Notify for completed uploads
    if (snapshot?.queue) {
      for (const q of snapshot.queue) {
        if (
          q.status === 'Uploaded' &&
          !notifiedIdsRef.current.has(`uploaded-${q.queueEntryId}`)
        ) {
          notifiedIdsRef.current.add(`uploaded-${q.queueEntryId}`);
          notify(
            '✅ Lecture Uploaded',
            `${q.fileName} uploaded to Google Drive successfully`
          );
        }

        if (
          (q.status === 'Failed' || q.status === 'FailedPermanently') &&
          !notifiedIdsRef.current.has(`failed-${q.queueEntryId}`)
        ) {
          notifiedIdsRef.current.add(`failed-${q.queueEntryId}`);
          notify(
            '❌ Upload Failed',
            `${q.fileName}${q.lastError ? ': ' + q.lastError : ''}`
          );
        }
      }
    }

    // Limit the Set size to prevent memory leak
    if (notifiedIdsRef.current.size > 500) {
      const arr = Array.from(notifiedIdsRef.current);
      notifiedIdsRef.current = new Set(arr.slice(-200));
    }
  }, [lectures, snapshot]);

  // Initial load + reload whenever the room changes
  useEffect(() => {
    loadData();
  }, [loadData]);

  // Live tab polls every 4s while the page is visible; other tabs fetch on demand
  useEffect(() => {
    if (activeTab !== 'live') return;

    const interval = setInterval(() => {
      if (document.visibilityState === 'visible') loadData();
    }, 4000);

    const onVisible = () => {
      if (document.visibilityState === 'visible') loadData();
    };
    document.addEventListener('visibilitychange', onVisible);

    return () => {
      clearInterval(interval);
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, [activeTab, loadData]);

  // Refresh data when switching to any non-live tab
  useEffect(() => {
    if (activeTab !== 'live') loadData();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab]);

  // Center tab polls every 5s while visible
  const loadCenterOverview = useCallback(async () => {
    try {
      const rooms = await api.fetchCenterOverview().catch(() => null);
      if (rooms) setCenterOverview(rooms);
    } catch {
      // keep last known overview
    }
  }, []);

  useEffect(() => {
    if (activeTab !== 'center') return;
    loadCenterOverview();
    const interval = setInterval(() => {
      if (document.visibilityState === 'visible') loadCenterOverview();
    }, 5000);
    return () => clearInterval(interval);
  }, [activeTab, loadCenterOverview]);

  const runAction = useCallback(
    async (fn: () => Promise<unknown>, successMessage: string) => {
      setBusy(true);
      try {
        await fn();
        if (successMessage) showToast(successMessage);
        await loadData();
      } catch (err) {
        showToast(`❌ ${err instanceof Error ? err.message : 'Action failed'}`);
      } finally {
        setBusy(false);
      }
    },
    [loadData, showToast]
  );

  const handleConfirm = async (
    lecture: LectureSession,
    batch?: string,
    subject?: string,
    teacher?: string,
    driveFolder?: string
  ) => {
    const b = (batch || lecture.batchId || '').trim();
    if (!b || b.toLowerCase() === 'unassigned') {
      // Prompt operator to select the actual batch and Drive folder
      setOverrideModal({
        open: true,
        lecture,
        batchId: '',
        subjectId: lecture.subjectId || 'PHYSICS',
        teacherId: lecture.teacherId || '',
        driveFolderPath: lecture.driveFolderPath || '',
      });
      return;
    }

    const s = (subject || lecture.subjectId || 'PHYSICS').trim();
    const t = (teacher || lecture.teacherId || '').trim();
    const d = (driveFolder || lecture.driveFolderPath || b).trim();
    await runAction(
      () => api.confirmLecture(lecture.lectureSessionId, b, s, t, 'Web Dashboard', d),
      `✅ Routed to "${d}" for ${b} / ${s}!`
    );
    setOverrideModal((prev) => ({ ...prev, open: false, lecture: null }));
  };

  const handleManualUpload = async (formData: FormData) => {
    await runAction(
      () => api.uploadLectureFile(formData),
      '✅ File uploaded and queued for Google Drive!'
    );
    await loadData();
  };

  const handleRematch = (lecture: LectureSession) =>
    runAction(() => api.rematchLecture(lecture.lectureSessionId), '🔄 Matching engine re-run for this lecture');

  const handleForceEnqueue = (lecture: LectureSession) =>
    runAction(() => api.forceEnqueueLecture(lecture.lectureSessionId), '⬆️ Upload queued despite duplicate flag');

  const handleCancelLecture = (lecture: LectureSession) => {
    if (!window.confirm(`Cancel lecture "${lecture.batchId || lecture.lectureSessionId}"? This will stop and remove it from uploads.`)) {
      return;
    }
    runAction(() => api.cancelLecture(lecture.lectureSessionId), '🚫 Lecture cancelled');
  };

  const handleEnqueueUpload = (lecture: LectureSession) =>
    runAction(() => api.enqueueLectureUpload(lecture.lectureSessionId), '⬆️ Upload queued to Google Drive!');

  // ---------- Upload queue actions ----------

  const handleRetryEntry = (queueEntryId: string) =>
    runAction(() => api.retryUpload(queueEntryId), '🔄 Upload queued for retry');

  const handleCancelEntry = (queueEntryId: string) =>
    runAction(() => api.cancelUpload(queueEntryId), '🚫 Upload cancelled');

  const handleRetryAllFailed = () =>
    runAction(async () => {
      const res = await api.retryAllFailedUploads();
      showToast(res.retried > 0 ? `🔄 ${res.retried} failed upload(s) re-queued` : 'No failed uploads to retry');
    }, '');

  // ---------- Monitoring actions ----------

  const handleRescan = () =>
    runAction(async () => {
      const res = await api.rescanFolder();
      showToast(res.newlyTracked > 0 ? `🔎 Rescan found ${res.newlyTracked} new file(s)` : '🔎 No new files found');
    }, '');

  // ---------- Timetable actions ----------

  const handleCreateSlot = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!agentInfo) return;

    const slotId = `SLOT-${effectiveRoom}-EXTRA-${Date.now().toString(36).toUpperCase().slice(-4)}`;
    const slotDate = addSlotModal.date || selectedDate;

    await runAction(
      () =>
        api.createTimetableEntry({
          organizationId: agentInfo.organizationId,
          centerId: agentInfo.centerId,
          roomId: effectiveRoom,
          scheduledDate: `${slotDate}T00:00:00`,
          slotStartTime: `${addSlotModal.slotStartTime}:00`,
          slotEndTime: `${addSlotModal.slotEndTime}:00`,
          slotId,
          batchId: addSlotModal.batchId,
          subjectId: addSlotModal.subjectId,
          teacherId: addSlotModal.teacherId,
        }),
      `✅ New slot added for ${addSlotModal.batchId} (${slotDate})!`
    );
    setAddSlotModal((prev) => ({ ...prev, open: false }));
    loadData();
  };

  const handleCancelSlot = (slot: TimetableEntry) => {
    if (!agentInfo) return;
    const targetDate = slot.scheduledDate ? slot.scheduledDate.split('T')[0] : selectedDate;
    if (!window.confirm(`Cancel "${slot.batchId} / ${slot.subjectId}" (${slot.slotStartTime.slice(0, 5)}) for ${targetDate}?`)) {
      return;
    }

    runAction(
      () =>
        api.cancelTimetableSlot({
          organizationId: agentInfo.organizationId,
          centerId: agentInfo.centerId,
          roomId: slot.roomId,
          timetableEntryId: slot.timetableEntryId,
          scheduledDate: `${targetDate}T00:00:00`,
          slotId: slot.slotId,
        }),
      `🚫 Slot ${slot.slotStartTime.slice(0, 5)} cancelled for ${targetDate}`
    );
  };

  const handleSyncTimetable = async () => {
    await runAction(
      () => api.syncTimetableNow(),
      '🔄 Timetable synced from Google Sheet!'
    );
    await loadData();
  };

  const handleUndoOverride = (overrideId: string) =>
    runAction(() => api.deactivateOverride(overrideId), '↩️ Override removed');

  // ---------- Runtime switches ----------

  const toggleUploads = async () => {
    setBusy(true);
    try {
      const resuming = controlState?.uploadsPaused;
      const next = resuming ? await api.resumeUploads() : await api.pauseUploads();
      setControlState(next);
      showToast(resuming ? '▶️ Upload engine resumed' : '⏸️ Upload engine paused');
    } catch (err) {
      showToast(`❌ ${err instanceof Error ? err.message : 'Action failed'}`);
    } finally {
      setBusy(false);
    }
  };

  const toggleMonitoring = async () => {
    setBusy(true);
    try {
      const resuming = controlState?.monitoringPaused;
      const next = resuming ? await api.resumeMonitoring() : await api.pauseMonitoring();
      setControlState(next);
      showToast(resuming ? '▶️ File monitoring resumed' : '⏸️ File monitoring paused');
    } catch (err) {
      showToast(`❌ ${err instanceof Error ? err.message : 'Action failed'}`);
    } finally {
      setBusy(false);
    }
  };

  const toggleTimetableSync = async () => {
    setBusy(true);
    try {
      const resuming = controlState?.timetableSyncPaused;
      const next = resuming ? await api.resumeTimetableSync() : await api.pauseTimetableSync();
      setControlState(next);
      showToast(resuming ? '▶️ Timetable sheet sync resumed' : '⏸️ Timetable sheet sync paused');
    } catch (err) {
      showToast(`❌ ${err instanceof Error ? err.message : 'Action failed'}`);
    } finally {
      setBusy(false);
    }
  };

  const handleSyncNow = () =>
    runAction(() => api.syncTimetableNow(), '📅 Timetable synced from Google Sheet');

  // ---------- Derived ----------

  // Lectures stay in Review until actually uploaded/verified in Google Drive
  const pendingReviews = useMemo(
    () =>
      lectures.filter(
        (l) =>
          l.status !== 'Uploaded' &&
          l.status !== 'Verified' &&
          l.status !== 'Cancelled' &&
          l.status !== 'Rejected'
      ),
    [lectures]
  );

  const unapprovedCount = useMemo(
    () =>
      lectures.filter(
        (l) =>
          l.status === 'ReviewRequired' ||
          l.reviewStatus === 'Pending' ||
          l.status === 'Detected' ||
          l.status === 'Processing'
      ).length,
    [lectures]
  );

  const failedQueueItems = useMemo(
    () => snapshot?.queue.filter((q) => q.status === 'Failed' || q.status === 'FailedPermanently') || [],
    [snapshot]
  );

  const duplicates = useMemo(() => lectures.filter((l) => l.status === 'Duplicate'), [lectures]);

  const rooms = useMemo(() => {
    const set = new Set<string>();
    if (agentInfo?.roomId) set.add(agentInfo.roomId);
    timetableRooms.forEach((r) => r && set.add(r));
    centerOverview.forEach((r) => r.name && set.add(r.name));
    lectures.forEach((l) => l.roomId && set.add(l.roomId));
    timetable.forEach((t) => t.roomId && set.add(t.roomId));
    if (snapshot?.queue) {
      snapshot.queue.forEach((q) => q.roomId && set.add(q.roomId));
    }
    if (set.size === 0) set.add('603');
    return Array.from(set).filter((r) => r !== 'ALL').sort();
  }, [agentInfo, timetableRooms, centerOverview, lectures, timetable, snapshot]);

  // ---------- Modal state ----------

  const [overrideModal, setOverrideModal] = useState<{
    open: boolean;
    lecture: LectureSession | null;
    batchId: string;
    subjectId: string;
    teacherId: string;
    driveFolderPath: string;
  }>({
    open: false,
    lecture: null,
    batchId: '',
    subjectId: '',
    teacherId: '',
    driveFolderPath: '',
  });

  const [folderModal, setFolderModal] = useState<{
    open: boolean;
    queueEntry: QueueEntry | null;
    folderPath: string;
    batchId: string;
  }>({ open: false, queueEntry: null, folderPath: '', batchId: '' });

  const handleOpenFolderModal = (queueEntry: QueueEntry) => {
    setFolderModal({
      open: true,
      queueEntry,
      folderPath: queueEntry.driveFolderPath || queueEntry.batchId || '',
      batchId: queueEntry.batchId || '',
    });
  };

  const handleSaveUploadFolder = async () => {
    if (!folderModal.queueEntry) return;
    const targetFolder = folderModal.folderPath.trim();
    const targetBatch = folderModal.batchId.trim() || targetFolder;
    await runAction(
      () => api.updateUploadFolder(folderModal.queueEntry!.queueEntryId, targetFolder, targetBatch),
      `✅ Destination set to "${targetFolder}". Upload re-queued!`
    );
    setFolderModal({ open: false, queueEntry: null, folderPath: '', batchId: '' });
  };

  const [addSlotModal, setAddSlotModal] = useState({
    open: false,
    date: selectedDate,
    slotStartTime: '14:00',
    slotEndTime: '15:30',
    batchId: '',
    subjectId: '',
    teacherId: '',
  });

  const navigationTabs: { id: Tab; icon: typeof Radio; label: string; badge?: number }[] = [
    { id: 'live', icon: Radio, label: 'Live' },
    { id: 'review', icon: CheckCircle2, label: 'Review', badge: unapprovedCount + failedQueueItems.length },
    { id: 'schedule', icon: Calendar, label: 'Schedule' },
    { id: 'studio', icon: Activity, label: 'Studio' },
    { id: 'center', icon: Building2, label: 'Center' },
    { id: 'controls', icon: Settings2, label: 'Controls' },
  ];

  return (
    <div className="flex flex-col min-h-screen bg-slate-50 text-slate-900 font-sans select-none">
      {/* Toast */}
      {toast && (
        <div className="fixed top-4 left-4 right-4 z-50 flex items-center justify-center pointer-events-none">
          <div className="bg-slate-900/95 text-white px-4 py-3 rounded-2xl shadow-xl backdrop-blur-md text-xs font-semibold">
            {toast}
          </div>
        </div>
      )}

      {/* Top Header */}
      <header className="sticky top-0 z-30 bg-white/95 border-b border-slate-200/90 backdrop-blur-md px-4 sm:px-6 lg:px-8 py-2.5 flex items-center justify-between shadow-xs">
        <div className="flex items-center gap-3">
          <div className="w-9 h-9 rounded-xl bg-white border border-slate-200 flex items-center justify-center p-1 shadow-xs shrink-0">
            <img src="/logo.png" alt="PW Logo" className="w-full h-full object-contain" />
          </div>
          <div>
            <div className="flex items-center gap-1.5">
              {isTauri() && (
                <span className={`inline-block w-2.5 h-2.5 rounded-full mr-1.5 ${
                  serviceState === 'Running' ? 'bg-emerald-400' :
                  serviceState === 'Stopped' ? 'bg-red-400' :
                  serviceState === 'NotInstalled' ? 'bg-gray-400' :
                  'bg-amber-400'
                }`} title={`Agent: ${serviceState}`} />
              )}
              <span className="font-bold text-base tracking-tight text-slate-900">Centrix</span>
              <span className="text-[10px] px-1.5 py-0.5 rounded-md bg-cyan-50 text-cyan-700 border border-cyan-200 font-semibold font-mono">PW Ops</span>
            </div>
            <p className="text-[11px] text-slate-500 truncate max-w-[180px] sm:max-w-none">
              Center: <strong className="text-slate-800">{agentInfo?.centerId || '...'}</strong>
            </p>
          </div>
        </div>

        {/* Desktop Navigation Tabs (Visible on md and up) */}
        <nav className="hidden md:flex items-center gap-1 bg-slate-100/90 p-1 rounded-2xl border border-slate-200/70 shadow-2xs">
          {navigationTabs.map((tab) => {
            const Icon = tab.icon;
            const active = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                onClick={() => setActiveTab(tab.id)}
                className={`relative flex items-center gap-2 px-3.5 py-1.5 rounded-xl text-xs font-semibold transition-all duration-150 ${
                  active
                    ? 'bg-white text-cyan-700 shadow-xs'
                    : 'text-slate-600 hover:text-slate-900 hover:bg-white/60'
                }`}
              >
                <Icon className={`w-4 h-4 ${active ? 'text-cyan-600' : 'text-slate-400'}`} />
                <span>{tab.label}</span>
                {tab.badge ? (
                  <span className="ml-1 px-1.5 py-0.2 bg-amber-500 text-white font-bold text-[10px] rounded-full shadow-xs">
                    {tab.badge}
                  </span>
                ) : null}
              </button>
            );
          })}
        </nav>

        {/* Right Action Controls */}
        <div className="flex items-center gap-2 sm:gap-2.5">
          {/* Quick Room Selector (Visible on desktop lg:) */}
          {rooms.length > 0 && (
            <div className="hidden lg:flex items-center gap-1.5">
              <select
                value={effectiveRoom}
                onChange={(e) => setSelectedRoom(e.target.value)}
                className="text-xs font-bold py-1.5 px-2.5 bg-slate-50 hover:bg-slate-100 border border-slate-200 rounded-xl text-slate-800 shadow-2xs hover:border-slate-300 focus:outline-hidden focus:ring-2 focus:ring-cyan-500/30 cursor-pointer transition"
                title="Quick Room Switcher"
              >
                <option value="ALL">All Rooms ({rooms.length})</option>
                {rooms.map((r) => (
                  <option key={r} value={r}>
                    Room {r} {r === agentInfo?.roomId ? '★ (This PC)' : ''}
                  </option>
                ))}
              </select>
            </div>
          )}

          {/* Live Status Pill */}
          <div className={`flex items-center gap-1.5 px-3 py-1.5 rounded-full border text-xs font-semibold shadow-2xs ${
            health?.status === 'Healthy'
              ? 'bg-emerald-50 border-emerald-200 text-emerald-700'
              : 'bg-red-50 border-red-200 text-red-700'
          }`}>
            <span className={`w-2 h-2 rounded-full ${health?.status === 'Healthy' ? 'bg-emerald-500 animate-ping' : 'bg-red-500'}`} />
            <span>{health?.status === 'Healthy' ? 'Agent Live' : 'Offline'}</span>
          </div>

          <button
            onClick={loadData}
            disabled={loading}
            className="p-2 rounded-xl bg-white border border-slate-200 text-slate-600 hover:text-slate-900 hover:bg-slate-50 shadow-xs active:scale-95 transition"
            title="Refresh"
          >
            <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin text-cyan-600' : ''}`} />
          </button>
        </div>
      </header>

      {/* Content Body */}
      <main className="flex-1 p-4 sm:p-6 lg:p-8 max-w-7xl mx-auto w-full space-y-6 pb-24 md:pb-12">
        {activeTab === 'live' && (
          <LiveScreen
            health={health}
            snapshot={snapshot}
            agentInfo={agentInfo}
            controlState={controlState}
            timetable={timetable}
            rooms={rooms}
            selectedRoom={effectiveRoom}
            onSelectRoom={setSelectedRoom}
            pendingReviewCount={unapprovedCount}
            missingSlots={missingSlots}
            busy={busy}
            onRetryEntry={handleRetryEntry}
            onCancelEntry={handleCancelEntry}
            onRetryAllFailed={handleRetryAllFailed}
            onEditFolder={handleOpenFolderModal}
          />
        )}

        {activeTab === 'review' && (
          <ReviewScreen
            pendingReviews={pendingReviews}
            duplicates={duplicates}
            failedQueueItems={failedQueueItems}
            queueItems={snapshot?.queue || []}
            rooms={rooms}
            busy={busy}
            onApprove={(item) => handleConfirm(item)}
            onEdit={(item) =>
              setOverrideModal({
                open: true,
                lecture: item,
                batchId: item.batchId || item.driveFolderPath || 'JEE-2026',
                subjectId: item.subjectId || 'PHYSICS',
                teacherId: item.teacherId || '',
                driveFolderPath: item.driveFolderPath || item.batchId || 'JEE-2026',
              })
            }
            onRematch={handleRematch}
            onForceEnqueue={handleForceEnqueue}
            onEditUploadFolder={handleOpenFolderModal}
            onRetryUpload={handleRetryEntry}
            onCancelUpload={handleCancelEntry}
            onManualUpload={handleManualUpload}
            onCancelLecture={handleCancelLecture}
            onEnqueueUpload={handleEnqueueUpload}
          />
        )}

        {activeTab === 'schedule' && (
          <ScheduleScreen
            timetable={timetable}
            overrides={overrides}
            rooms={rooms}
            selectedRoom={effectiveRoom}
            selectedDate={selectedDate}
            availableDates={availableDates}
            timetableSummary={timetableSummary}
            busy={busy}
            onSelectRoom={setSelectedRoom}
            onSelectDate={setSelectedDate}
            onOpenAddSlot={() => setAddSlotModal((prev) => ({ ...prev, open: true, date: selectedDate }))}
            onCancelSlot={handleCancelSlot}
            onUndoOverride={handleUndoOverride}
            onSyncNow={handleSyncTimetable}
          />
        )}

        {activeTab === 'center' && (
          <CenterScreen
            overview={centerOverview}
            busy={busy}
            loading={loading}
            onRefresh={loadCenterOverview}
            onSaveRoom={(room) =>
              runAction(() => api.saveCenterRoom(room), `✅ Room ${room.name} saved`)
            }
            onDeleteRoom={(name) => {
              if (window.confirm(`Remove ${name} from the center view?`)) {
                runAction(() => api.deleteCenterRoom(name), `🗑️ ${name} removed`);
              }
            }}
          />
        )}

        {activeTab === 'studio' && (
          <StudioLiveScreen />
        )}

        {activeTab === 'controls' && (
          <ControlsScreen
            agentInfo={agentInfo}
            controlState={controlState}
            health={health}
            snapshot={snapshot}
            rooms={timetableRooms}
            currentRoom={effectiveRoom}
            busy={busy}
            onToggleUploads={toggleUploads}
            onToggleMonitoring={toggleMonitoring}
            onToggleSync={toggleTimetableSync}
            onRescan={handleRescan}
            onRetryAllFailed={handleRetryAllFailed}
            onSyncNow={handleSyncNow}
            onLogout={onLogout}
            onFolderUpdated={loadData}
            onFileUploaded={loadData}
          />
        )}
      </main>

      {/* Override / Target Drive Folder Modal */}
      <Modal
        open={overrideModal.open && overrideModal.lecture !== null}
        title="Approve & Route to Google Drive"
        icon={<FolderEdit className="w-5 h-5 text-cyan-500" />}
        maxWidth="max-w-xl sm:max-w-2xl"
        onClose={() =>
          setOverrideModal({
            open: false,
            lecture: null,
            batchId: '',
            subjectId: '',
            teacherId: '',
            driveFolderPath: '',
          })
        }
      >
        <div className="space-y-4 text-xs">
          {/* Lecture Preview Card */}
          {overrideModal.lecture && (
            <div className="bg-slate-50 border border-slate-200/90 rounded-2xl p-3.5 space-y-2.5">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2">
                  <span className="text-xs font-bold text-slate-900 font-mono">
                    Room {overrideModal.lecture.roomId}
                  </span>
                  <span className="text-[10px] px-2 py-0.5 rounded-md bg-cyan-50 text-cyan-700 border border-cyan-200 font-semibold font-mono">
                    {overrideModal.lecture.lectureSessionId}
                  </span>
                </div>
                <div className="text-[11px] text-slate-500 font-mono">
                  {new Date(overrideModal.lecture.detectedStartTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                  {' - '}
                  {new Date(overrideModal.lecture.detectedEndTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                </div>
              </div>

              {/* Compact Video/PDF Thumbnail or File Banner */}
              <VideoThumbnail
                filePath={overrideModal.lecture.videoFilePath || overrideModal.lecture.pdfFilePath}
                fileName={(overrideModal.lecture.videoFilePath || overrideModal.lecture.pdfFilePath)?.split(/[/\\]/).pop()}
                fileType={overrideModal.lecture.pdfFilePath ? 'PDF' : 'VIDEO'}
                compact
              />
            </div>
          )}

          {/* Direct Google Drive Folder Selector */}
          <DriveFolderPicker
            value={overrideModal.driveFolderPath}
            onChange={(f) =>
              setOverrideModal((prev) => ({
                ...prev,
                driveFolderPath: f,
                batchId: prev.batchId && prev.batchId !== 'Unassigned' ? prev.batchId : f,
              }))
            }
          />

          <div className="grid grid-cols-2 gap-3">
            <Field label="Target Batch Name">
              <input
                type="text"
                value={overrideModal.batchId}
                onChange={(e) => setOverrideModal((prev) => ({ ...prev, batchId: e.target.value }))}
                placeholder="e.g. 11TH-JEE-A"
                className={inputClass}
                required
              />
            </Field>
            <Field label="Subject">
              <input
                type="text"
                value={overrideModal.subjectId}
                onChange={(e) => setOverrideModal((prev) => ({ ...prev, subjectId: e.target.value }))}
                placeholder="e.g. PHYSICS"
                className={inputClass}
                required
              />
            </Field>
          </div>

          <Field label="Teacher (Optional)">
            <input
              type="text"
              value={overrideModal.teacherId}
              onChange={(e) => setOverrideModal((prev) => ({ ...prev, teacherId: e.target.value }))}
              placeholder="e.g. PROF_VERMA"
              className={inputClass}
            />
          </Field>
        </div>

        <div className="pt-3 border-t border-slate-100 flex items-center gap-2">
          <button
            type="button"
            onClick={() =>
              setOverrideModal({
                open: false,
                lecture: null,
                batchId: '',
                subjectId: '',
                teacherId: '',
                driveFolderPath: '',
              })
            }
            className="flex-1 py-2.5 rounded-xl border border-slate-200 text-slate-600 hover:bg-slate-50 font-semibold text-xs transition active:scale-95"
          >
            Cancel
          </button>
          <ActionButton
            tone="primary"
            onClick={() =>
              handleConfirm(
                overrideModal.lecture!,
                overrideModal.batchId,
                overrideModal.subjectId,
                overrideModal.teacherId,
                overrideModal.driveFolderPath
              )
            }
            disabled={busy || !overrideModal.driveFolderPath.trim()}
            className="flex-2 py-2.5 text-xs font-bold shadow-lg shadow-cyan-500/20"
          >
            🚀 Approve & Route to Drive
          </ActionButton>
        </div>
      </Modal>

      {/* Add Slot Modal */}
      <Modal
        open={addSlotModal.open}
        title="Add Extra Timetable Slot"
        icon={<Plus className="w-4 h-4 text-cyan-400" />}
        onClose={() => setAddSlotModal((prev) => ({ ...prev, open: false }))}
      >
        <form onSubmit={handleCreateSlot} className="space-y-4">
          <div className="space-y-3 text-xs">
            <Field label="Slot Date">
              <input
                type="date"
                value={addSlotModal.date}
                onChange={(e) => setAddSlotModal((prev) => ({ ...prev, date: e.target.value }))}
                className={inputClass}
                required
              />
            </Field>

            <div className="grid grid-cols-2 gap-2">
              <Field label="Start Time">
                <input
                  type="time"
                  value={addSlotModal.slotStartTime}
                  onChange={(e) => setAddSlotModal((prev) => ({ ...prev, slotStartTime: e.target.value }))}
                  className={inputClass}
                  required
                />
              </Field>
              <Field label="End Time">
                <input
                  type="time"
                  value={addSlotModal.slotEndTime}
                  onChange={(e) => setAddSlotModal((prev) => ({ ...prev, slotEndTime: e.target.value }))}
                  className={inputClass}
                  required
                />
              </Field>
            </div>

            <Field label="Batch Name">
              <input
                type="text"
                value={addSlotModal.batchId}
                onChange={(e) => setAddSlotModal((prev) => ({ ...prev, batchId: e.target.value }))}
                placeholder="e.g. JEE-2026-BATCH-A"
                className={inputClass}
                required
              />
            </Field>

            <Field label="Subject">
              <input
                type="text"
                value={addSlotModal.subjectId}
                onChange={(e) => setAddSlotModal((prev) => ({ ...prev, subjectId: e.target.value }))}
                placeholder="e.g. PHYSICS"
                className={inputClass}
                required
              />
            </Field>

            <Field label="Teacher (Optional)">
              <input
                type="text"
                value={addSlotModal.teacherId}
                onChange={(e) => setAddSlotModal((prev) => ({ ...prev, teacherId: e.target.value }))}
                className={inputClass}
              />
            </Field>
          </div>

          <button
            type="submit"
            disabled={busy}
            className="w-full py-2.5 rounded-xl bg-cyan-500 hover:bg-cyan-400 font-bold text-white text-xs shadow-lg shadow-cyan-500/20 active:scale-95 transition disabled:opacity-40"
          >
            Create Slot
          </button>
        </form>
      </Modal>

      {/* Target Drive Folder Selection Modal */}
      <Modal
        open={folderModal.open && folderModal.queueEntry !== null}
        title="Select Drive Folder / Batch"
        icon={<FolderEdit className="w-5 h-5 text-cyan-500" />}
        maxWidth="max-w-xl sm:max-w-2xl"
        onClose={() => setFolderModal({ open: false, queueEntry: null, folderPath: '', batchId: '' })}
      >
        <div className="space-y-3 text-xs">
          <div className="bg-slate-50 border border-slate-200 rounded-xl p-2.5 space-y-1">
            <span className="text-[10px] text-slate-500 uppercase tracking-wider block font-semibold">
              Selected Recording File
            </span>
            <span className="text-xs font-mono font-bold text-slate-800 break-all">
              {folderModal.queueEntry?.fileName}
            </span>
            {folderModal.queueEntry?.lastError && (
              <p className="text-[11px] text-rose-600 mt-1 leading-snug">
                Error: {folderModal.queueEntry.lastError}
              </p>
            )}
          </div>

          {/* Google Drive Folder Selector */}
          <DriveFolderPicker
            value={folderModal.folderPath}
            onChange={(f) =>
              setFolderModal((prev) => ({
                ...prev,
                folderPath: f,
                batchId: prev.batchId ? prev.batchId : f,
              }))
            }
          />

          <Field label="Batch ID (Optional)">
            <input
              type="text"
              value={folderModal.batchId}
              onChange={(e) => setFolderModal((prev) => ({ ...prev, batchId: e.target.value }))}
              placeholder="e.g. 11TH-JEE-A"
              className={inputClass}
            />
          </Field>
        </div>

        <div className="pt-2">
          <ActionButton
            tone="primary"
            onClick={handleSaveUploadFolder}
            disabled={busy || !folderModal.folderPath.trim()}
            className="w-full py-2.5"
          >
            Save Folder & Retry Upload Now
          </ActionButton>
        </div>
      </Modal>

      {/* Bottom Mobile Tab Bar (Mobile only, hidden on desktop) */}
      <nav className="md:hidden fixed bottom-0 left-0 right-0 z-40 bg-white/95 border-t border-slate-200/90 backdrop-blur-lg px-3 py-2 flex items-center justify-around max-w-md mx-auto shadow-md">
        {([
          { id: 'live', icon: Radio, label: 'Live' },
          { id: 'review', icon: CheckCircle2, label: 'Review', badge: unapprovedCount + failedQueueItems.length },
          { id: 'schedule', icon: Calendar, label: 'Schedule' },
          { id: 'studio', icon: Activity, label: 'Studio' },
          { id: 'center', icon: Building2, label: 'Center' },
          { id: 'controls', icon: Settings2, label: 'Controls' },
        ] as { id: Tab; icon: typeof Radio; label: string; badge?: number }[]).map((tab) => {
          const Icon = tab.icon;
          const active = activeTab === tab.id;
          return (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={`relative flex flex-col items-center gap-1 py-1 px-3 rounded-xl transition active:scale-95 ${
                active ? 'text-cyan-600 font-bold' : 'text-slate-400 hover:text-slate-600'
              }`}
            >
              <Icon className="w-5 h-5" />
              <span className="text-[10px]">{tab.label}</span>
              {tab.badge ? (
                <span className="absolute top-0 right-2 w-4 h-4 bg-amber-500 text-white font-bold text-[9px] rounded-full flex items-center justify-center shadow-xs">
                  {tab.badge}
                </span>
              ) : null}
            </button>
          );
        })}
      </nav>
    </div>
  );
}

export default App;
