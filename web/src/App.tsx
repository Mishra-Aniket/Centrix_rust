import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Building2,
  Calendar,
  CheckCircle2,
  Edit3,
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
  RoomOverview,
  TimetableEntry,
  TimetableOverride,
} from './types';
import * as api from './api';
import { clearApiKey, isConfigured, onUnauthorized } from './config';
import { LoginScreen } from './screens/Login';
import { LiveScreen } from './screens/Live';
import { ReviewScreen } from './screens/Review';
import { ScheduleScreen } from './screens/Schedule';
import { ControlsScreen } from './screens/Controls';
import { CenterScreen } from './screens/Center';
import { ActionButton, Field, Modal, inputClass } from './ui';

type Tab = 'live' | 'review' | 'schedule' | 'center' | 'controls';

export function App() {
  const [authed, setAuthed] = useState(isConfigured());

  useEffect(
    () =>
      onUnauthorized(() => {
        setAuthed(false);
      }),
    []
  );

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
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [toast, setToast] = useState<string | null>(null);

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

      const [hData, sData, lData, tData, oData, cData, mData] = await Promise.all([
        api.fetchHealth().catch(() => null),
        api.fetchSnapshot().catch(() => null),
        api.fetchLectures(center, 30).catch(() => null),
        center ? api.fetchTimetable(center, room).catch(() => null) : Promise.resolve(null),
        center ? api.fetchOverrides(center, room).catch(() => null) : Promise.resolve(null),
        api.fetchControlState().catch(() => null),
        center ? api.fetchMissingLectures(center, room).catch(() => null) : Promise.resolve(null),
      ]);

      if (hData) setHealth(hData);
      if (sData) setSnapshot(sData);
      if (lData) setLectures(lData.items);
      if (tData) setTimetable(tData);
      if (oData) setOverrides(oData);
      if (cData) setControlState(cData);
      setMissingSlots(mData ?? []);
    } catch (err) {
      console.error('Failed to load data:', err);
    } finally {
      setLoading(false);
    }
  }, [selectedRoom]);

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

  // ---------- Lecture actions ----------

  const handleConfirm = async (lecture: LectureSession, batch?: string, subject?: string, teacher?: string) => {
    const b = batch || lecture.batchId || 'JEE-2026';
    const s = subject || lecture.subjectId || 'PHYSICS';
    const t = teacher || lecture.teacherId || '';
    await runAction(
      () => api.confirmLecture(lecture.lectureSessionId, b, s, t),
      `✅ Approved for ${b} / ${s}! Upload routing updated.`
    );
    setOverrideModal((prev) => ({ ...prev, open: false, lecture: null }));
  };

  const handleRematch = (lecture: LectureSession) =>
    runAction(() => api.rematchLecture(lecture.lectureSessionId), '🔄 Matching engine re-run for this lecture');

  const handleForceEnqueue = (lecture: LectureSession) =>
    runAction(() => api.forceEnqueueLecture(lecture.lectureSessionId), '⬆️ Upload queued despite duplicate flag');

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
    const today = new Date().toISOString().split('T')[0];

    await runAction(
      () =>
        api.createTimetableEntry({
          organizationId: agentInfo.organizationId,
          centerId: agentInfo.centerId,
          roomId: effectiveRoom,
          scheduledDate: `${today}T00:00:00`,
          slotStartTime: `${addSlotModal.slotStartTime}:00`,
          slotEndTime: `${addSlotModal.slotEndTime}:00`,
          slotId,
          batchId: addSlotModal.batchId,
          subjectId: addSlotModal.subjectId,
          teacherId: addSlotModal.teacherId,
        }),
      `✅ New slot added for ${addSlotModal.batchId}!`
    );
    setAddSlotModal((prev) => ({ ...prev, open: false }));
  };

  const handleCancelSlot = (slot: TimetableEntry) => {
    if (!agentInfo) return;
    if (!window.confirm(`Cancel "${slot.batchId} / ${slot.subjectId}" (${slot.slotStartTime.slice(0, 5)}) for today?`)) {
      return;
    }

    const today = new Date().toISOString().split('T')[0];
    runAction(
      () =>
        api.cancelTimetableSlot({
          organizationId: agentInfo.organizationId,
          centerId: agentInfo.centerId,
          roomId: slot.roomId,
          timetableEntryId: slot.timetableEntryId,
          scheduledDate: slot.scheduledDate?.split('T')[0] || `${today}T00:00:00`,
          slotId: slot.slotId,
        }),
      `🚫 Slot ${slot.slotStartTime.slice(0, 5)} cancelled for today`
    );
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

  const pendingReviews = useMemo(
    () => lectures.filter((l) => l.status === 'ReviewRequired' || l.reviewStatus === 'Pending'),
    [lectures]
  );

  const duplicates = useMemo(() => lectures.filter((l) => l.status === 'Duplicate'), [lectures]);

  const rooms = useMemo(() => {
    const set = new Set<string>();
    if (agentInfo?.roomId) set.add(agentInfo.roomId);
    lectures.forEach((l) => l.roomId && set.add(l.roomId));
    if (set.size === 0) set.add('603');
    return Array.from(set);
  }, [agentInfo, lectures]);

  // ---------- Modal state ----------

  const [overrideModal, setOverrideModal] = useState<{
    open: boolean;
    lecture: LectureSession | null;
    batchId: string;
    subjectId: string;
    teacherId: string;
  }>({ open: false, lecture: null, batchId: '', subjectId: '', teacherId: '' });

  const [addSlotModal, setAddSlotModal] = useState({
    open: false,
    slotStartTime: '14:00',
    slotEndTime: '15:30',
    batchId: '',
    subjectId: '',
    teacherId: '',
  });

  return (
    <div className="flex flex-col min-h-screen bg-[#090d16] text-slate-100 font-sans pb-20 select-none">
      {/* Toast */}
      {toast && (
        <div className="fixed top-4 left-4 right-4 z-50 flex items-center justify-center pointer-events-none">
          <div className="bg-slate-900/95 border border-cyan-500/50 text-cyan-300 px-4 py-3 rounded-xl shadow-2xl backdrop-blur-md text-sm font-medium">
            {toast}
          </div>
        </div>
      )}

      {/* Top Header */}
      <header className="sticky top-0 z-30 bg-[#0c1220]/90 border-b border-slate-800/80 backdrop-blur-md px-4 py-3 flex items-center justify-between">
        <div className="flex items-center gap-2.5">
          <div className="w-8 h-8 rounded-lg bg-black border border-white/10 flex items-center justify-center p-0.5 shadow-lg shadow-cyan-500/10">
            <img src="/logo.png" alt="PW Logo" className="w-full h-full object-contain rounded" />
          </div>
          <div>
            <div className="flex items-center gap-1.5">
              <span className="font-bold text-base tracking-tight text-white">Centrix</span>
              <span className="text-[10px] px-1.5 py-0.5 rounded bg-cyan-500/10 text-cyan-400 border border-cyan-500/30 font-mono">PW Ops</span>
            </div>
            <p className="text-[11px] text-slate-400">
              Center: <strong className="text-slate-200">{agentInfo?.centerId || '...'}</strong>
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <div className={`flex items-center gap-1.5 px-2.5 py-1 rounded-full border text-xs font-medium ${
            health?.status === 'Healthy'
              ? 'bg-emerald-500/10 border-emerald-500/30 text-emerald-400'
              : 'bg-red-500/10 border-red-500/30 text-red-300'
          }`}>
            <span className={`w-2 h-2 rounded-full ${health?.status === 'Healthy' ? 'bg-emerald-400 animate-ping' : 'bg-red-400'}`} />
            <span>{health?.status === 'Healthy' ? 'Agent Live' : 'Offline'}</span>
          </div>
          <button
            onClick={loadData}
            disabled={loading}
            className="p-2 rounded-lg bg-slate-800/80 border border-slate-700 text-slate-300 hover:text-white active:scale-95 transition"
            title="Refresh"
          >
            <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin text-cyan-400' : ''}`} />
          </button>
        </div>
      </header>

      {/* Content Body */}
      <main className="flex-1 p-4 max-w-md mx-auto w-full space-y-4">
        {activeTab === 'live' && (
          <LiveScreen
            health={health}
            snapshot={snapshot}
            agentInfo={agentInfo}
            controlState={controlState}
            timetable={timetable}
            selectedRoom={effectiveRoom}
            pendingReviewCount={pendingReviews.length}
            missingSlots={missingSlots}
            busy={busy}
            onRetryEntry={handleRetryEntry}
            onCancelEntry={handleCancelEntry}
            onRetryAllFailed={handleRetryAllFailed}
          />
        )}

        {activeTab === 'review' && (
          <ReviewScreen
            pendingReviews={pendingReviews}
            duplicates={duplicates}
            busy={busy}
            onApprove={(item) => handleConfirm(item)}
            onEdit={(item) =>
              setOverrideModal({
                open: true,
                lecture: item,
                batchId: item.batchId || 'JEE-2026-BATCH-A',
                subjectId: item.subjectId || 'PHYSICS',
                teacherId: item.teacherId || '',
              })
            }
            onRematch={handleRematch}
            onForceEnqueue={handleForceEnqueue}
          />
        )}

        {activeTab === 'schedule' && (
          <ScheduleScreen
            timetable={timetable}
            overrides={overrides}
            rooms={rooms}
            selectedRoom={effectiveRoom}
            agentInfo={agentInfo}
            busy={busy}
            onSelectRoom={setSelectedRoom}
            onOpenAddSlot={() => setAddSlotModal((prev) => ({ ...prev, open: true }))}
            onCancelSlot={handleCancelSlot}
            onUndoOverride={handleUndoOverride}
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

        {activeTab === 'controls' && (
          <ControlsScreen
            agentInfo={agentInfo}
            controlState={controlState}
            health={health}
            snapshot={snapshot}
            busy={busy}
            onToggleUploads={toggleUploads}
            onToggleMonitoring={toggleMonitoring}
            onToggleSync={toggleTimetableSync}
            onRescan={handleRescan}
            onRetryAllFailed={handleRetryAllFailed}
            onSyncNow={handleSyncNow}
            onLogout={onLogout}
          />
        )}
      </main>

      {/* Override Modal */}
      <Modal
        open={overrideModal.open && overrideModal.lecture !== null}
        title="Change Batch & Subject"
        icon={<Edit3 className="w-4 h-4 text-cyan-400" />}
        onClose={() => setOverrideModal({ open: false, lecture: null, batchId: '', subjectId: '', teacherId: '' })}
      >
        <div className="space-y-3 text-xs">
          <Field label="Target Batch">
            <input
              type="text"
              value={overrideModal.batchId}
              onChange={(e) => setOverrideModal((prev) => ({ ...prev, batchId: e.target.value }))}
              className={inputClass}
            />
          </Field>
          <Field label="Subject">
            <input
              type="text"
              value={overrideModal.subjectId}
              onChange={(e) => setOverrideModal((prev) => ({ ...prev, subjectId: e.target.value }))}
              className={inputClass}
            />
          </Field>
          <Field label="Teacher (Optional)">
            <input
              type="text"
              value={overrideModal.teacherId}
              onChange={(e) => setOverrideModal((prev) => ({ ...prev, teacherId: e.target.value }))}
              className={inputClass}
            />
          </Field>
        </div>

        <div className="pt-2">
          <ActionButton
            tone="primary"
            onClick={() => handleConfirm(overrideModal.lecture!, overrideModal.batchId, overrideModal.subjectId, overrideModal.teacherId)}
            disabled={busy}
            className="w-full py-2.5"
          >
            Save & Route to Drive
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

      {/* Bottom Mobile Tab Bar */}
      <nav className="fixed bottom-0 left-0 right-0 z-40 bg-[#0c1220]/95 border-t border-slate-800/80 backdrop-blur-lg px-3 py-2 flex items-center justify-around max-w-md mx-auto">
        {([
          { id: 'live', icon: Radio, label: 'Live' },
          { id: 'review', icon: CheckCircle2, label: 'Review', badge: pendingReviews.length },
          { id: 'schedule', icon: Calendar, label: 'Schedule' },
          { id: 'center', icon: Building2, label: 'Center' },
          { id: 'controls', icon: Settings2, label: 'Controls' },
        ] as { id: Tab; icon: typeof Radio; label: string; badge?: number }[]).map((tab) => {
          const Icon = tab.icon;
          const active = activeTab === tab.id;
          return (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={`relative flex flex-col items-center gap-1 py-1 px-3 rounded-xl transition ${
                active ? 'text-cyan-400' : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              <Icon className="w-5 h-5" />
              <span className="text-[10px] font-medium">{tab.label}</span>
              {tab.badge ? (
                <span className="absolute top-0 right-2 w-4 h-4 bg-amber-500 text-slate-950 font-bold text-[9px] rounded-full flex items-center justify-center">
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
