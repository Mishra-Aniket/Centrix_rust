import {
  CalendarClock,
  CloudUpload,
  Copy,
  FolderSearch,
  KeyRound,
  LogOut,
  MonitorPause,
  PauseCircle,
  PlayCircle,
  RefreshCw,
  Server,
  UploadCloud,
} from 'lucide-react';
import type { AgentInfo, ControlState, HealthStatus, MonitorSnapshot } from '../types';
import { ActionButton, Card, Pill } from '../ui';

interface ControlsScreenProps {
  agentInfo: AgentInfo | null;
  controlState: ControlState | null;
  health: HealthStatus | null;
  snapshot: MonitorSnapshot | null;
  busy: boolean;
  onToggleUploads: () => void;
  onToggleMonitoring: () => void;
  onToggleSync: () => void;
  onRescan: () => void;
  onRetryAllFailed: () => void;
  onSyncNow: () => void;
  onLogout: () => void;
}

function ControlRow({
  icon,
  title,
  description,
  running,
  onToggle,
  busy,
  children,
}: {
  icon: React.ReactNode;
  title: string;
  description: string;
  running: boolean;
  onToggle: () => void;
  busy: boolean;
  children?: React.ReactNode;
}) {
  return (
    <div className="border-b border-slate-800/80 pb-3 mb-3 last:border-b-0 last:pb-0 last:mb-0">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-start gap-2.5 min-w-0">
          <div className="mt-0.5 shrink-0">{icon}</div>
          <div className="min-w-0">
            <div className="flex items-center gap-1.5">
              <span className="text-xs font-bold text-white">{title}</span>
              <Pill tone={running ? 'emerald' : 'amber'}>{running ? 'Running' : 'Paused'}</Pill>
            </div>
            <p className="text-[10px] text-slate-400 leading-relaxed">{description}</p>
          </div>
        </div>
        <button
          onClick={onToggle}
          disabled={busy}
          className={`p-2.5 rounded-xl border active:scale-95 transition disabled:opacity-40 shrink-0 ${
            running
              ? 'bg-slate-800 border-slate-700 text-slate-200'
              : 'bg-emerald-500/10 border-emerald-500/40 text-emerald-300'
          }`}
          title={running ? 'Pause' : 'Resume'}
        >
          {running ? <PauseCircle className="w-5 h-5" /> : <PlayCircle className="w-5 h-5" />}
        </button>
      </div>
      {children && <div className="mt-2.5 flex gap-2">{children}</div>}
    </div>
  );
}

export function ControlsScreen({
  agentInfo,
  controlState,
  health,
  snapshot,
  busy,
  onToggleUploads,
  onToggleMonitoring,
  onToggleSync,
  onRescan,
  onRetryAllFailed,
  onSyncNow,
  onLogout,
}: ControlsScreenProps) {
  const copy = async (text: string) => {
    try {
      await navigator.clipboard.writeText(text);
    } catch {
      // Clipboard permission denied on older browsers; the text is visible anyway.
    }
  };

  const failedCount = snapshot?.summary.failed ?? 0;

  return (
    <div className="space-y-3">
      <div className="px-1">
        <h2 className="text-sm font-bold text-white">Agent Controls</h2>
        <p className="text-[11px] text-slate-400">Remote switches — changes apply instantly on the PC</p>
      </div>

      <Card className="space-y-0">
        <ControlRow
          icon={<UploadCloud className="w-4 h-4 text-cyan-400" />}
          title="Upload Engine"
          description="Sends queued recordings to Google Drive (up to 3 concurrent). Pausing does not stop an upload already in flight."
          running={!controlState?.uploadsPaused}
          onToggle={onToggleUploads}
          busy={busy}
        >
          <ActionButton tone="primary" onClick={onRetryAllFailed} disabled={busy || failedCount === 0} className="flex-1">
            <RefreshCw className="w-3.5 h-3.5" />
            Retry all failed{failedCount > 0 ? ` (${failedCount})` : ''}
          </ActionButton>
        </ControlRow>

        <ControlRow
          icon={<MonitorPause className="w-4 h-4 text-indigo-400" />}
          title="File Monitoring"
          description="Watches the recording folder for new files. While paused, new files are ignored."
          running={!controlState?.monitoringPaused}
          onToggle={onToggleMonitoring}
          busy={busy}
        >
          <ActionButton tone="primary" onClick={onRescan} disabled={busy} className="flex-1">
            <FolderSearch className="w-3.5 h-3.5" />
            Rescan folder now
          </ActionButton>
        </ControlRow>

        <ControlRow
          icon={<CalendarClock className="w-4 h-4 text-amber-400" />}
          title="Timetable Sheet Sync"
          description={`Pulls the timetable from Google Sheets every ${agentInfo?.sheetSyncIntervalMinutes ?? 5} min and replaces the configured room's slots with sheet data.`}
          running={!controlState?.timetableSyncPaused}
          onToggle={onToggleSync}
          busy={busy}
        >
          <ActionButton tone="primary" onClick={onSyncNow} disabled={busy || controlState?.timetableSyncPaused} className="flex-1">
            <CloudUpload className="w-3.5 h-3.5" />
            Sync from sheet now
          </ActionButton>
        </ControlRow>
      </Card>

      <Card className="space-y-2.5 text-xs">
        <div className="flex items-center justify-between border-b border-slate-800 pb-2">
          <h4 className="font-bold text-slate-300 flex items-center gap-1.5">
            <Server className="w-3.5 h-3.5 text-cyan-400" />
            Agent Info
          </h4>
          <Pill tone="cyan">v{agentInfo?.agentVersion ?? '...'}</Pill>
        </div>
        <div className="flex justify-between">
          <span className="text-slate-400">Center / Room</span>
          <span className="font-semibold text-slate-200 text-right">
            {agentInfo?.centerId || '...'} · Room {agentInfo?.roomId || '...'}
          </span>
        </div>
        <div className="flex justify-between">
          <span className="text-slate-400">Host</span>
          <span className="font-mono text-slate-200">{agentInfo?.machineName || '...'}</span>
        </div>
        <div className="flex justify-between">
          <span className="text-slate-400">Monitored Folder</span>
          <span className="font-mono text-cyan-400 truncate max-w-[170px]" title={agentInfo?.monitorFolder}>
            {agentInfo?.monitorFolder || '...'}
          </span>
        </div>
        <div className="flex justify-between">
          <span className="text-slate-400">Google Drive</span>
          <span className="font-semibold text-slate-200">
            {agentInfo?.googleDriveEnabled ? `Live → ${agentInfo.driveRootFolder}` : 'Mock mode'}
          </span>
        </div>
        <div className="flex justify-between">
          <span className="text-slate-400">Database</span>
          <span className="font-semibold text-emerald-400">{health?.database ?? '...'}</span>
        </div>
      </Card>

      <Card className="space-y-2.5 text-xs">
        <h4 className="font-bold text-slate-300">Open on another device (same WiFi)</h4>
        {(agentInfo?.lanIpv4Addresses.length ?? 0) === 0 ? (
          <p className="text-[11px] text-slate-500">
            No LAN address detected. Use the URL printed by the agent script.
          </p>
        ) : (
          agentInfo!.lanIpv4Addresses.map((ip) => {
            const url = `http://${ip}:${agentInfo!.httpPort}/`;
            return (
              <div key={ip} className="flex items-center justify-between gap-2 bg-slate-800/60 border border-slate-700 rounded-xl px-3 py-2">
                <span className="font-mono text-[11px] text-cyan-300 truncate">{url}</span>
                <button
                  onClick={() => copy(url)}
                  className="p-1.5 rounded-lg bg-slate-700 text-slate-300 hover:text-white active:scale-95 transition shrink-0"
                  title="Copy URL"
                >
                  <Copy className="w-3.5 h-3.5" />
                </button>
              </div>
            );
          })
        )}
      </Card>

      <Card className="space-y-3">
        <div className="flex items-center justify-between">
          <h4 className="font-bold text-slate-300 flex items-center gap-1.5 text-xs">
            <KeyRound className="w-3.5 h-3.5 text-slate-400" />
            Connection
          </h4>
          <Pill tone="emerald">Key stored</Pill>
        </div>
        <p className="text-[10px] text-slate-400 leading-relaxed">
          The API key lives only in this browser. Log out to enter a different key or agent address.
        </p>
        <ActionButton tone="danger" onClick={onLogout} disabled={busy} className="w-full">
          <LogOut className="w-3.5 h-3.5" />
          Log out / change key
        </ActionButton>
      </Card>
    </div>
  );
}
