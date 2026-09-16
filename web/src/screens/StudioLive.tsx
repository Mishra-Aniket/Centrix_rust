import { useCallback, useEffect, useMemo, useState } from 'react';
import { Activity, Cloud, CloudOff, FileVideo, FileText, Filter, Key, RefreshCw } from 'lucide-react';
import type { StudioFile, StudioCenter, StudioStatus } from '../types';
import * as api from '../api';
import { ActionButton, Field, Modal, inputClass } from '../ui';

function formatTime(iso: string): string {
  try {
    return new Date(iso).toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit', hour12: true });
  } catch {
    return '—';
  }
}

function formatDate(iso: string): string {
  try {
    return new Date(iso).toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' });
  } catch {
    return '—';
  }
}

function FileTypeBadge({ type }: { type: string }) {
  const colors: Record<string, string> = {
    video: 'bg-purple-100 text-purple-700 border-purple-200',
    notes: 'bg-blue-100 text-blue-700 border-blue-200',
    demo: 'bg-amber-100 text-amber-700 border-amber-200',
  };
  return (
    <span className={`px-2 py-0.5 rounded-full text-[10px] font-bold border ${colors[type] || 'bg-slate-100 text-slate-600 border-slate-200'}`}>
      {type.toUpperCase()}
    </span>
  );
}

function StatusDot({ ok, label }: { ok: boolean; label: string }) {
  return (
    <span className="flex items-center gap-1">
      <span className={`w-2 h-2 rounded-full ${ok ? 'bg-emerald-500' : 'bg-red-400'}`} />
      <span className="text-[10px] text-slate-500">{label}</span>
    </span>
  );
}

export function StudioLiveScreen() {
  const [status, setStatus] = useState<StudioStatus | null>(null);
  const [files, setFiles] = useState<StudioFile[]>([]);
  const [centers, setCenters] = useState<StudioCenter[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [lastRefresh, setLastRefresh] = useState<Date | null>(null);

  // Token Modal State
  const [tokenModalOpen, setTokenModalOpen] = useState(false);
  const [tokenInput, setTokenInput] = useState('');
  const [tokenSaving, setTokenSaving] = useState(false);
  const [tokenFeedback, setTokenFeedback] = useState<{ ok: boolean; msg: string } | null>(null);

  // Filters
  const [selectedCenter, setSelectedCenter] = useState('');
  const [selectedRoom, setSelectedRoom] = useState('');
  const [selectedFileType, setSelectedFileType] = useState('');
  const [selectedDate, setSelectedDate] = useState(() => {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  });

  const availableRooms = useMemo(() => {
    const center = centers.find((c) => c.center === selectedCenter);
    return center?.rooms.sort() ?? [];
  }, [centers, selectedCenter]);

  const loadData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [statusRes, centersRes] = await Promise.all([
        api.fetchStudioStatus().catch(() => null),
        api.fetchStudioCenters().catch(() => null),
      ]);
      if (statusRes) setStatus(statusRes);
      if (centersRes) setCenters(centersRes.centers);

      const dateObj = new Date(selectedDate);
      const from = new Date(dateObj); from.setHours(0, 0, 0, 0);
      const to = new Date(dateObj); to.setHours(23, 59, 59, 999);

      const filesRes = await api.fetchStudioFiles({
        center: selectedCenter || undefined,
        room: selectedRoom || undefined,
        from: from.toISOString(),
        to: to.toISOString(),
        fileType: selectedFileType || undefined,
      });
      setFiles(filesRes.files);
      setLastRefresh(new Date());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load Studio data');
    } finally {
      setLoading(false);
    }
  }, [selectedCenter, selectedRoom, selectedFileType, selectedDate]);

  useEffect(() => { loadData(); }, [loadData]);

  // Auto-refresh every 30s
  useEffect(() => {
    const interval = setInterval(() => {
      if (document.visibilityState === 'visible') loadData();
    }, 30000);
    return () => clearInterval(interval);
  }, [loadData]);

  const handleSaveToken = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!tokenInput.trim()) return;

    setTokenSaving(true);
    setTokenFeedback(null);
    try {
      const res = await api.updateStudioToken(tokenInput.trim());
      if (res.connected) {
        setTokenFeedback({ ok: true, msg: '✅ Connected to PenPencil Studio API successfully!' });
        setTimeout(() => {
          setTokenModalOpen(false);
          setTokenFeedback(null);
          setTokenInput('');
          loadData();
        }, 1200);
      } else {
        setTokenFeedback({ ok: false, msg: '❌ Token accepted but ping test failed. Check if token is valid.' });
      }
    } catch (err) {
      setTokenFeedback({ ok: false, msg: `❌ Error: ${err instanceof Error ? err.message : 'Failed to update token'}` });
    } finally {
      setTokenSaving(false);
    }
  };

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Activity className="w-5 h-5 text-cyan-500" />
          <h2 className="text-base font-bold text-slate-900">Studio Live</h2>
          {status && (
            <span className={`flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold border ${
              status.connected
                ? 'bg-emerald-50 border-emerald-200 text-emerald-700'
                : 'bg-red-50 border-red-200 text-red-700'
            }`}>
              {status.connected ? <Cloud className="w-3 h-3" /> : <CloudOff className="w-3 h-3" />}
              {status.connected ? 'Connected' : 'Disconnected'}
            </span>
          )}
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={() => setTokenModalOpen(true)}
            className="flex items-center gap-1 px-2.5 py-1.5 rounded-xl bg-cyan-50 border border-cyan-200 text-cyan-700 hover:bg-cyan-100 text-xs font-semibold shadow-xs active:scale-95 transition"
            title="Update PW Token"
          >
            <Key className="w-3.5 h-3.5" />
            <span>Update Token</span>
          </button>
          <button
            onClick={loadData}
            disabled={loading}
            className="p-2 rounded-xl bg-white border border-slate-200 text-slate-500 hover:text-slate-900 shadow-xs active:scale-95 transition"
          >
            <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin text-cyan-500' : ''}`} />
          </button>
        </div>
      </div>

      {/* Filters */}
      <div className="bg-white rounded-2xl border border-slate-200 p-3 space-y-2 shadow-xs">
        <div className="flex items-center gap-1.5 text-xs text-slate-500 font-semibold">
          <Filter className="w-3.5 h-3.5" /> Filters
        </div>
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-2">
          <select
            value={selectedCenter}
            onChange={(e) => { setSelectedCenter(e.target.value); setSelectedRoom(''); }}
            className="text-xs rounded-xl border border-slate-200 px-2.5 py-2 bg-slate-50 focus:ring-2 focus:ring-cyan-500/30 text-slate-800"
          >
            <option value="">All Centers</option>
            {centers.map((c) => (
              <option key={c.center} value={c.center}>{c.center}</option>
            ))}
          </select>
          <select
            value={selectedRoom}
            onChange={(e) => setSelectedRoom(e.target.value)}
            className="text-xs rounded-xl border border-slate-200 px-2.5 py-2 bg-slate-50 focus:ring-2 focus:ring-cyan-500/30 text-slate-800"
          >
            <option value="">All Rooms</option>
            {availableRooms.map((r) => (
              <option key={r} value={r}>{r}</option>
            ))}
          </select>
          <select
            value={selectedFileType}
            onChange={(e) => setSelectedFileType(e.target.value)}
            className="text-xs rounded-xl border border-slate-200 px-2.5 py-2 bg-slate-50 focus:ring-2 focus:ring-cyan-500/30 text-slate-800"
          >
            <option value="">All Types</option>
            <option value="video">Video</option>
            <option value="notes">Notes</option>
            <option value="demo">Demo</option>
          </select>
          <input
            type="date"
            value={selectedDate}
            onChange={(e) => setSelectedDate(e.target.value)}
            className="text-xs rounded-xl border border-slate-200 px-2.5 py-2 bg-slate-50 focus:ring-2 focus:ring-cyan-500/30 text-slate-800"
          />
        </div>
      </div>

      {/* Error */}
      {error && (
        <div className="bg-red-50 border border-red-200 rounded-xl p-3 text-xs text-red-700 space-y-2 shadow-xs">
          <div className="font-semibold flex items-center gap-1.5">
            <span>⚠️ {error}</span>
          </div>
          {error.includes('403') && (
            <div className="flex items-center justify-between pt-1 border-t border-red-200/60">
              <span className="text-[11px] text-red-600">PW Google ID Token expires in 1 hour.</span>
              <button
                onClick={() => setTokenModalOpen(true)}
                className="px-2.5 py-1 bg-red-600 hover:bg-red-700 text-white rounded-lg text-xs font-bold shadow-xs active:scale-95 transition flex items-center gap-1"
              >
                <Key className="w-3 h-3" /> Enter New Token
              </button>
            </div>
          )}
        </div>
      )}

      {/* Stats Bar */}
      <div className="flex items-center justify-between text-[11px] text-slate-500">
        <span>{files.length} file{files.length !== 1 ? 's' : ''} found</span>
        {lastRefresh && <span>Updated: {lastRefresh.toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit' })}</span>}
      </div>

      {/* File Cards */}
      {loading && files.length === 0 ? (
        <div className="flex items-center justify-center py-12 text-slate-400 text-xs">
          <RefreshCw className="w-4 h-4 animate-spin mr-2" /> Loading Studio data...
        </div>
      ) : files.length === 0 ? (
        <div className="text-center py-12 text-slate-400 text-xs">
          No files found for the selected filters.
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-3">
          {files.map((file) => (
            <div
              key={file._id}
              className="bg-white rounded-xl border border-slate-200 p-3 shadow-xs space-y-1.5"
            >
              <div className="flex items-start justify-between gap-2">
                <div className="flex items-center gap-2 min-w-0">
                  {file.fileType === 'video' ? (
                    <FileVideo className="w-4 h-4 text-purple-500 shrink-0" />
                  ) : (
                    <FileText className="w-4 h-4 text-blue-500 shrink-0" />
                  )}
                  <span className="text-xs font-semibold text-slate-800 truncate">{file.fileName}</span>
                </div>
                <FileTypeBadge type={file.fileType} />
              </div>

              <div className="flex items-center gap-3 text-[11px] text-slate-500">
                <span className="font-medium text-slate-700">{file.center}</span>
                <span className="text-slate-300">|</span>
                <span>Room {file.room}</span>
              </div>

              <div className="flex items-center justify-between">
                <div className="text-[11px] text-slate-500">
                  🕐 {formatTime(file.startTime)} — {formatTime(file.stopTime)}
                  <span className="ml-1.5 text-slate-400">{formatDate(file.startTime)}</span>
                </div>
                <div className="flex items-center gap-2">
                  <StatusDot ok={file.uploaded} label="U" />
                  <StatusDot ok={file.scheduled} label="S" />
                  <StatusDot ok={file.isProcessed} label="P" />
                </div>
              </div>

              {file.batchName && (
                <div className="text-[10px] text-slate-400">
                  Batch: <span className="text-slate-600 font-medium">{file.batchName}</span>
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      {/* Update Token Modal */}
      <Modal
        open={tokenModalOpen}
        title="Update PW Studio Token"
        icon={<Key className="w-4 h-4 text-cyan-500" />}
        onClose={() => { setTokenModalOpen(false); setTokenFeedback(null); }}
      >
        <form onSubmit={handleSaveToken} className="space-y-4 text-xs">
          <div className="bg-slate-50 border border-slate-200 rounded-xl p-3 space-y-1.5 text-slate-600">
            <div className="font-bold text-slate-800 flex items-center gap-1.5">
              <span>💡 How to Get Your Token:</span>
            </div>
            <ol className="list-decimal list-inside space-y-1 text-[11px] text-slate-600">
              <li>Open your <strong>React App</strong> (stage tracker) tab in Chrome.</li>
              <li>Press <strong>F12</strong> on your keyboard to open Developer Tools and select the Console tab.</li>
              <li>In the Console, type: <code className="bg-white px-1.5 py-0.5 rounded border border-slate-200 text-cyan-700 font-mono">localStorage.getItem('token')</code> and press Enter.</li>
              <li>Copy the displayed token and paste it into the field below.</li>
            </ol>
          </div>

          <Field label="PW Google ID Token">
            <textarea
              rows={4}
              value={tokenInput}
              onChange={(e) => setTokenInput(e.target.value)}
              placeholder="Paste eyJhbGciOiJSUzI1Ni... token here"
              className={`${inputClass} font-mono text-[11px] resize-none`}
              required
            />
          </Field>

          {tokenFeedback && (
            <div className={`p-2.5 rounded-xl text-xs font-semibold ${
              tokenFeedback.ok
                ? 'bg-emerald-50 border-emerald-200 text-emerald-700'
                : 'bg-red-50 border-red-200 text-red-700'
            }`}>
              {tokenFeedback.msg}
            </div>
          )}

          <ActionButton
            tone="primary"
            type="submit"
            disabled={tokenSaving || !tokenInput.trim()}
            className="w-full py-2.5 text-xs font-bold"
          >
            {tokenSaving ? 'Connecting...' : 'Save & Connect'}
          </ActionButton>
        </form>
      </Modal>
    </div>
  );
}
