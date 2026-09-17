import { useMemo, useState } from 'react';
import {
  AlertCircle,
  Ban,
  CalendarX,
  CheckCircle2,
  Clock,
  CloudUpload,
  FileText,
  Folder,
  PauseCircle,
  RotateCcw,
  Video,
} from 'lucide-react';
import { RoomSelector } from '../components/RoomSelector';
import type { AgentInfo, ControlState, HealthStatus, MissingSlot, MonitorSnapshot, QueueEntry, TimetableEntry } from '../types';
import { Pill } from '../ui';

interface LiveScreenProps {
  health: HealthStatus | null;
  snapshot: MonitorSnapshot | null;
  agentInfo: AgentInfo | null;
  controlState: ControlState | null;
  timetable: TimetableEntry[];
  rooms: string[];
  selectedRoom: string;
  onSelectRoom: (roomId: string) => void;
  pendingReviewCount: number;
  missingSlots: MissingSlot[];
  busy: boolean;
  onRetryEntry: (queueEntryId: string) => void;
  onCancelEntry: (queueEntryId: string) => void;
  onRetryAllFailed: () => void;
  onEditFolder?: (item: QueueEntry) => void;
}

function formatUptime(seconds: number): string {
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  return h > 0 ? `${h}h ${m}m` : `${m}m`;
}

function formatRelativeTime(dateStr: string): string {
  try {
    const d = new Date(dateStr);
    const now = new Date();
    const diffMs = now.getTime() - d.getTime();
    if (diffMs < 0) return 'Just now';
    const diffSec = Math.floor(diffMs / 1000);
    if (diffSec < 60) return `${diffSec}s ago`;
    const diffMin = Math.floor(diffSec / 60);
    if (diffMin < 60) return `${diffMin}m ago`;
    const diffHr = Math.floor(diffMin / 60);
    if (diffHr < 24) return `${diffHr}h ago`;
    return d.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' });
  } catch {
    return '';
  }
}

function currentSlot(timetable: TimetableEntry[]): TimetableEntry | null {
  const now = new Date();
  const nowMinutes = now.getHours() * 60 + now.getMinutes();

  const toMinutes = (time: string) => {
    const [h, m] = time.split(':').map(Number);
    return (h || 0) * 60 + (m || 0);
  };

  return (
    timetable.find((slot) => {
      const start = toMinutes(slot.slotStartTime);
      const end = toMinutes(slot.slotEndTime);
      return nowMinutes >= start && nowMinutes < end;
    }) ?? null
  );
}

export function LiveScreen({
  health,
  snapshot,
  agentInfo,
  controlState,
  timetable,
  rooms,
  selectedRoom,
  onSelectRoom,
  pendingReviewCount,
  missingSlots,
  busy,
  onRetryEntry,
  onCancelEntry,
  onRetryAllFailed,
  onEditFolder,
}: LiveScreenProps) {
  const activeQueue = snapshot?.queue.filter((q) => q.status === 'Uploading' || q.status === 'Pending') ?? [];
  const failedToday = snapshot?.summary.failedToday ?? snapshot?.summary.failed ?? 0;
  const uploadedToday = snapshot?.summary.uploadedToday ?? snapshot?.summary.uploaded ?? 0;
  const slot = currentSlot(timetable);

  // Strictly filter queue to last 24h or active transfers
  const feedItems = useMemo(() => {
    const baseTime = snapshot?.generatedAt ? new Date(snapshot.generatedAt).getTime() : 0;
    const cutoff24h = baseTime > 0 ? baseTime - 24 * 60 * 60 * 1000 : 0;
    return (snapshot?.queue ?? []).filter((q) => {
      const isPendingOrUploading = q.status === 'Uploading' || q.status === 'Pending';
      const isRecent = cutoff24h === 0 || new Date(q.updatedAt).getTime() >= cutoff24h;
      const matchesRoom = selectedRoom === 'ALL' || !selectedRoom || !q.roomId || q.roomId === selectedRoom;
      return (isPendingOrUploading || isRecent) && matchesRoom;
    });
  }, [snapshot, selectedRoom]);

  const isAllRooms = selectedRoom === 'ALL' || !selectedRoom;
  const [showMissingDetails, setShowMissingDetails] = useState(false);

  return (
    <div className="space-y-4">
      {/* Room Selector Dropdown */}
      <RoomSelector
        rooms={rooms}
        selectedRoom={selectedRoom}
        onSelectRoom={onSelectRoom}
        localRoomId={agentInfo?.roomId}
      />

      {missingSlots.length > 0 && !isAllRooms && (
        <div className="bg-[var(--paper)] border border-rose-800/40 p-4 space-y-3 font-mono">
          <div className="flex items-center justify-between gap-3">
            <div className="flex items-center gap-2.5 min-w-0">
              <div className="w-8 h-8 border border-rose-800/40 bg-[var(--cream)] text-rose-500 flex items-center justify-center shrink-0">
                <CalendarX className="w-4 h-4" />
              </div>
              <div className="min-w-0">
                <p className="font-serif text-sm font-normal text-[var(--ink)] truncate">
                  {missingSlots.length} lecture{missingSlots.length > 1 ? 's' : ''} missing today (Room {selectedRoom})
                </p>
                <p className="text-[11px] text-[var(--stone)] truncate">
                  // recordings not detected yet for scheduled slots
                </p>
              </div>
            </div>
            <button
              onClick={() => setShowMissingDetails((prev) => !prev)}
              className="px-3 py-1.5 border border-rose-800/40 bg-[var(--cream)] text-rose-500 hover:bg-[var(--paper)] text-[10px] font-mono uppercase tracking-wider shrink-0 transition cursor-pointer"
            >
              {showMissingDetails ? 'Hide details' : `Show all (${missingSlots.length})`}
            </button>
          </div>

          {showMissingDetails && (
            <div className="pt-3 border-t border-[var(--rule)] grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-2">
              {missingSlots.map((m) => (
                <div key={m.slotId || m.timetableEntryId} className="bg-[var(--cream)] border border-[var(--rule)] p-2.5 text-xs">
                  <span className="font-mono text-xs text-[var(--ink)] block">{m.slotStartTime.slice(0, 5)}–{m.slotEndTime.slice(0, 5)}</span>
                  <span className="truncate block text-[11px] text-[var(--stone)]">{m.batchId} · {m.subjectId}</span>
                  {m.teacherId && <span className="text-[10px] text-[var(--stone)] block truncate">Teacher: {m.teacherId}</span>}
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {(controlState?.uploadsPaused || controlState?.monitoringPaused) && (
        <div className="bg-[var(--paper)] border border-[var(--rule)] p-3.5 flex items-start gap-2.5">
          <PauseCircle className="w-4 h-4 text-[var(--clay)] shrink-0 mt-0.5" />
          <p className="text-xs font-mono text-[var(--ink)] leading-relaxed">
            {controlState?.uploadsPaused && <><strong>Uploads paused.</strong> New queue entries will wait. </>}
            {controlState?.monitoringPaused && <><strong>Monitoring paused.</strong> New files in the folder are ignored. </>}
            Resume from the Controls tab.
          </p>
        </div>
      )}

      {/* 4 Quick Metrics Cards (24h Window) matching officemobile */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        {/* Uploaded Today Card */}
        <div className="bg-[var(--paper)] border border-[var(--rule)] p-4 flex flex-col justify-between transition-colors hover:border-[var(--stone)]">
          <div className="flex items-center justify-between text-[var(--stone)] font-mono text-[10px] uppercase tracking-wider">
            <span>01 · Uploaded (24h)</span>
            <CheckCircle2 className="w-3.5 h-3.5 text-emerald-500" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="font-serif text-3xl font-light text-[var(--ink)]">{uploadedToday}</span>
            <span className="font-mono text-[10px] text-[var(--stone)]">in Drive</span>
          </div>
        </div>

        {/* Drive Queue Card */}
        <div className="bg-[var(--paper)] border border-[var(--rule)] p-4 flex flex-col justify-between transition-colors hover:border-[var(--stone)]">
          <div className="flex items-center justify-between text-[var(--stone)] font-mono text-[10px] uppercase tracking-wider">
            <span>02 · Drive Queue</span>
            <CloudUpload className="w-3.5 h-3.5 text-[var(--ink)]" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="font-serif text-3xl font-light text-[var(--ink)]">{activeQueue.length}</span>
            <span className="font-mono text-[10px] text-[var(--stone)]">in transit</span>
          </div>
        </div>

        {/* Pending Reviews Card */}
        <div className="bg-[var(--paper)] border border-[var(--rule)] p-4 flex flex-col justify-between transition-colors hover:border-[var(--stone)]">
          <div className="flex items-center justify-between text-[var(--stone)] font-mono text-[10px] uppercase tracking-wider">
            <span>03 · Pending Reviews</span>
            <AlertCircle className={`w-3.5 h-3.5 ${pendingReviewCount > 0 ? 'text-[var(--clay)]' : 'text-[var(--stone)]'}`} />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className={`font-serif text-3xl font-light ${pendingReviewCount > 0 ? 'text-[var(--clay)]' : 'text-[var(--ink)]'}`}>
              {pendingReviewCount}
            </span>
            {pendingReviewCount > 0 && (
              <span className="font-mono text-[10px] text-[var(--clay)]">Action required</span>
            )}
          </div>
        </div>

        {/* Failed (24h) Card */}
        <div className="bg-[var(--paper)] border border-[var(--rule)] p-4 flex flex-col justify-between transition-colors hover:border-[var(--stone)]">
          <div className="flex items-center justify-between text-[var(--stone)] font-mono text-[10px] uppercase tracking-wider">
            <span>04 · Failed (24h)</span>
            <RotateCcw className={`w-3.5 h-3.5 ${failedToday > 0 ? 'text-red-400' : 'text-[var(--stone)]'}`} />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className={`font-serif text-3xl font-light ${failedToday > 0 ? 'text-red-400' : 'text-[var(--ink)]'}`}>
              {failedToday}
            </span>
            {failedToday > 0 ? (
              <span className="font-mono text-[10px] text-red-400">Needs retry</span>
            ) : (
              <span className="font-mono text-[10px] text-[var(--stone)]">0 errors</span>
            )}
          </div>
        </div>
      </div>

      {/* Desktop 2-Column Grid: Left is Status & Diagnostics, Right is Live Feed */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
        {/* Left Column: Room Status & Diagnostics */}
        <div className="lg:col-span-5 space-y-4 lg:sticky lg:top-20">
          <div className="bg-[var(--paper)] border border-[var(--rule)] p-5">
            <div className="flex items-baseline justify-between border-b border-[var(--rule)] pb-3 mb-3">
              <div className="flex items-center gap-2">
                <span className="font-mono text-[10px] uppercase tracking-widest text-[var(--stone)]">SYSTEM STATUS</span>
                <span className="font-mono text-[11px] px-1.5 py-0.2 border border-[var(--rule)] bg-[var(--cream)] text-[var(--ink)]">
                  {isAllRooms ? 'All Rooms' : `Room ${selectedRoom}`}
                </span>
              </div>
              <Pill tone={agentInfo?.fileWatcherEnabled && !controlState?.monitoringPaused ? 'emerald' : 'amber'}>
                {agentInfo?.fileWatcherEnabled ? (controlState?.monitoringPaused ? 'Watcher Paused' : 'Watcher Active') : 'Watcher Off'}
              </Pill>
            </div>

            <div className="space-y-2 text-xs font-mono">
              {!isAllRooms ? (
                <>
                  <div className="flex justify-between py-1.5 border-b border-[var(--rule)]">
                    <span className="text-[var(--stone)]">Current Slot</span>
                    {slot ? (
                      <span className="font-medium text-[var(--ink)]">
                        {slot.batchId} · {slot.subjectId}
                      </span>
                    ) : (
                      <span className="text-[var(--stone)]">No slot scheduled now</span>
                    )}
                  </div>
                  <div className="flex justify-between py-1.5 border-b border-[var(--rule)]">
                    <span className="text-[var(--stone)]">Monitored Folder</span>
                    <span className="text-[var(--ink)] truncate max-w-[200px]" title={agentInfo?.monitorFolder}>
                      {agentInfo?.monitorFolder || '...'}
                    </span>
                  </div>
                  <div className="flex justify-between py-1.5 border-b border-[var(--rule)]">
                    <span className="text-[var(--stone)]">Agent Uptime</span>
                    <span className="text-[var(--ink)]">
                      {agentInfo ? formatUptime(agentInfo.uptimeSeconds) : '...'}
                    </span>
                  </div>
                  <div className="flex justify-between py-1.5 border-b border-[var(--rule)]">
                    <span className="text-[var(--stone)]">Database</span>
                    <span className="text-emerald-500 flex items-center gap-1.5">
                      <span className="w-1.5 h-1.5 rounded-full bg-emerald-500"></span>
                      {health?.database ?? 'Connected (SQLite WAL)'}
                    </span>
                  </div>
                  <div className="flex justify-between py-1.5">
                    <span className="text-[var(--stone)]">Google Drive</span>
                    <span className="text-[var(--ink)] flex items-center gap-1.5">
                      <span className="w-1.5 h-1.5 rounded-full bg-emerald-500"></span>
                      {agentInfo?.googleDriveEnabled ? 'Live Connected' : 'Mock Mode'}
                    </span>
                  </div>
                </>
              ) : (
                <>
                  <div className="flex justify-between py-1.5 border-b border-[var(--rule)]">
                    <span className="text-[var(--stone)]">Scope</span>
                    <span className="text-[var(--ink)]">All Classroom PCs across Center</span>
                  </div>
                  <div className="flex justify-between py-1.5 border-b border-[var(--rule)]">
                    <span className="text-[var(--stone)]">Active Classroom PCs</span>
                    <span className="text-[var(--ink)]">{rooms.length} Rooms</span>
                  </div>
                  <div className="flex justify-between py-1.5 border-b border-[var(--rule)]">
                    <span className="text-[var(--stone)]">Local PC Room</span>
                    <span className="text-[var(--ink)] font-bold">Room {agentInfo?.roomId || '603'}</span>
                  </div>
                  <div className="flex justify-between py-1.5">
                    <span className="text-[var(--stone)]">Google Drive</span>
                    <span className="text-[var(--ink)] flex items-center gap-1.5">
                      <span className="w-1.5 h-1.5 rounded-full bg-emerald-500"></span>
                      {agentInfo?.googleDriveEnabled ? 'Live Connected' : 'Mock Mode'}
                    </span>
                  </div>
                </>
              )}
            </div>
          </div>
        </div>

        {/* Right Column: Live Upload Feed */}
        <div className="lg:col-span-7 space-y-3">
          <div className="flex items-baseline justify-between px-1 pb-2 border-b border-[var(--rule)]">
            <div>
              <div className="font-mono text-[10px] uppercase tracking-wider text-[var(--stone)]">
                FEED · STRICT 24-HOUR WINDOW
              </div>
              <h3 className="font-serif text-lg font-normal text-[var(--ink)] tracking-tight">
                Live Upload Feed
              </h3>
            </div>

            <div className="flex items-center gap-2 font-mono text-[10px] uppercase tracking-wider">
              <span className="inline-flex items-center gap-1.5 text-emerald-500">
                <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse"></span>
                Auto-Refresh
              </span>

              {failedToday > 0 && (
                <button
                  onClick={onRetryAllFailed}
                  disabled={busy}
                  className="px-2 py-1 border border-red-700/40 text-red-400 hover:bg-red-950/20 cursor-pointer"
                >
                  Retry All ({failedToday})
                </button>
              )}
            </div>
          </div>

          {feedItems.length === 0 ? (
            <div className="bg-[var(--paper)] border border-[var(--rule)] p-8 text-center text-[var(--stone)] text-xs font-mono space-y-3">
              <div className="w-10 h-10 border border-[var(--rule)] flex items-center justify-center text-[var(--ink)] mx-auto">
                <CloudUpload className="w-5 h-5" />
              </div>
              <div className="space-y-1">
                <p className="font-serif text-base text-[var(--ink)]">
                  Upload Queue is Clear
                </p>
                <p className="text-[11px] text-[var(--stone)] max-w-md mx-auto leading-relaxed">
                  No transfers in flight or completed in the last 24 hours {isAllRooms ? 'across the center' : `for Room ${selectedRoom}`}.
                </p>
              </div>
              <div className="pt-1 flex items-center justify-center">
                <span className="inline-flex items-center gap-1.5 px-3 py-1 text-[10px] border border-[var(--rule)] text-[var(--stone)]">
                  <span className="w-1.5 h-1.5 rounded-full bg-emerald-500"></span>
                  Monitoring: <strong className="text-[var(--ink)] font-normal truncate max-w-[240px]">{agentInfo?.monitorFolder || 'Classroom recording folder'}</strong>
                </span>
              </div>
            </div>
          ) : (
            <div className="space-y-2">
              {feedItems.slice(0, 15).map((item) => {
                const canRetry = item.status === 'Failed' || item.status === 'FailedPermanently';
                const canCancel = item.status === 'Pending' || item.status === 'Failed' || item.status === 'FailedPermanently';
                const timeLabel = formatRelativeTime(item.updatedAt);

                return (
                  <div key={item.queueEntryId} className="bg-[var(--paper)] border border-[var(--rule)] p-3.5 space-y-2 transition-colors hover:border-[var(--stone)]">
                    <div className="flex items-start justify-between gap-2">
                      <div className="flex items-center gap-2 min-w-0">
                        {item.fileType === 'VIDEO' ? (
                          <div className="w-6 h-6 border border-[var(--rule)] flex items-center justify-center text-[var(--stone)] shrink-0">
                            <Video className="w-3.5 h-3.5" />
                          </div>
                        ) : (
                          <div className="w-6 h-6 border border-[var(--rule)] flex items-center justify-center text-[var(--stone)] shrink-0">
                            <FileText className="w-3.5 h-3.5" />
                          </div>
                        )}
                        <div className="min-w-0">
                          <span className="text-xs font-mono text-[var(--ink)] truncate block">{item.fileName}</span>
                          <div className="flex items-center gap-2 mt-0.5 font-mono text-[10px] text-[var(--stone)]">
                            {item.roomId && (
                              <span className="px-1 border border-[var(--rule)]">
                                Room {item.roomId}
                              </span>
                            )}
                            <span className="flex items-center gap-0.5">
                              <Clock className="w-2.5 h-2.5" /> {timeLabel}
                            </span>
                          </div>
                        </div>
                      </div>

                      <span className={`text-[10px] px-2 py-0.5 font-mono uppercase tracking-wider shrink-0 border ${
                        item.status === 'Uploaded' ? 'bg-emerald-950/20 text-emerald-500 border-emerald-700/40' :
                        item.status === 'Uploading' ? 'bg-[var(--cream)] text-[var(--ink)] border-[var(--rule)] animate-pulse' :
                        item.status === 'FailedPermanently' ? 'bg-red-950/20 text-red-400 border-red-700/40' :
                        'bg-amber-950/20 text-amber-500 border-amber-700/40'
                      }`}>
                        {item.status}
                      </span>
                    </div>

                    {/* 2px Progress bar matching officemobile */}
                    <div className="space-y-1">
                      <div className="w-full bg-[var(--rule)] h-1 overflow-hidden">
                        <div
                          className="h-full bg-[var(--ink)] transition-all duration-300"
                          style={{ width: `${item.progressPercentage}%` }}
                        />
                      </div>
                      <div className="flex justify-between text-[10px] text-[var(--stone)] font-mono">
                        <span>{(item.fileSizeBytes / (1024 * 1024)).toFixed(1)} MB</span>
                        <span>{item.progressPercentage}%</span>
                      </div>
                    </div>

                    {(canRetry || canCancel) && (
                      <div className="flex items-center gap-3 pt-1 border-t border-[var(--rule)] font-mono text-[11px] uppercase tracking-wider">
                        {canRetry && (
                          <button
                            onClick={() => onRetryEntry(item.queueEntryId)}
                            disabled={busy}
                            className="text-[var(--ink)] hover:underline flex items-center gap-1 cursor-pointer"
                          >
                            <RotateCcw className="w-3 h-3" /> Retry
                          </button>
                        )}
                        {canRetry && onEditFolder && (
                          <button
                            onClick={() => onEditFolder(item)}
                            disabled={busy}
                            className="text-[var(--ink)] hover:underline flex items-center gap-1 cursor-pointer"
                          >
                            <Folder className="w-3 h-3" /> Change Folder
                          </button>
                        )}
                        {canCancel && (
                          <button
                            onClick={() => onCancelEntry(item.queueEntryId)}
                            disabled={busy}
                            className="text-red-400 hover:underline flex items-center gap-1 cursor-pointer"
                          >
                            <Ban className="w-3 h-3" /> Cancel
                          </button>
                        )}
                        {item.lastError && (
                          <span className="text-[10px] text-red-400 truncate flex items-center gap-1 min-w-0">
                            <AlertCircle className="w-3 h-3 shrink-0" />
                            {item.lastError}
                          </span>
                        )}
                      </div>
                    )}

                    {item.driveFileId && (
                      <div className="text-[10px] text-[var(--stone)] font-mono flex items-center gap-1 truncate pt-1 border-t border-[var(--rule)]">
                        <CheckCircle2 className="w-3 h-3 text-emerald-500 shrink-0" />
                        <span className="truncate">Drive File ID: <strong className="text-[var(--ink)] font-normal">{item.driveFileId}</strong></span>
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
