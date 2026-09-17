import { useEffect, useRef, useState } from 'react';
import QRCode from 'qrcode';
import {
  AlertCircle,
  AlertTriangle,
  CalendarClock,
  Check,
  CheckCircle2,
  Clock,
  CloudUpload,
  Copy,
  Cpu,
  Download,
  ExternalLink,
  FileCheck2,
  FileText,
  FileUp,
  FolderOpen,
  FolderSearch,
  Gauge,
  Globe,
  HardDrive,
  KeyRound,
  Loader2,
  LogOut,
  MemoryStick,
  MonitorPause,
  PauseCircle,
  Play,
  PlayCircle,
  Power,
  QrCode,
  RefreshCw,
  Save,
  Server,
  Smartphone,
  Sparkles,
  UploadCloud,
  Video,
  Wifi,
} from 'lucide-react';
import * as api from '../api';
import type { AgentInfo, ControlState, HealthStatus, MonitorSnapshot, LectureSession } from '../types';
import { STANDARD_SUBJECTS } from '../types';
import { ActionButton, Card, Pill } from '../ui';
import { MediaPreviewModal } from '../components/MediaPreviewModal';

import {
  isTauri,
  getSystemHealth,
  exportDiagnostics,
  isAutoStartEnabled,
  enableAutoStart,
  disableAutoStart,
  pickFolder,
  getBandwidthSettings,
  saveBandwidthSettings,
  type SystemHealth,
  type BandwidthSettings,
} from '../tauri';

interface ControlsScreenProps {
  agentInfo: AgentInfo | null;
  controlState: ControlState | null;
  health: HealthStatus | null;
  snapshot: MonitorSnapshot | null;
  rooms?: string[];
  currentRoom?: string;
  busy: boolean;
  onToggleUploads: () => void;
  onToggleMonitoring: () => void;
  onToggleSync: () => void;
  onRescan: () => void;
  onRetryAllFailed: () => void;
  onSyncNow: () => void;
  onLogout: () => void;
  onFolderUpdated?: () => void;
  onFileUploaded?: () => void;
}

function formatBytes(bytes: number): string {
  if (!bytes || bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${(bytes / Math.pow(k, i)).toFixed(1)} ${sizes[i]}`;
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
    <div className="border-b border-slate-100 pb-3 mb-3 last:border-b-0 last:pb-0 last:mb-0">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-start gap-2.5 min-w-0">
          <div className="mt-0.5 shrink-0">{icon}</div>
          <div className="min-w-0">
            <div className="flex items-center gap-1.5">
              <span className="text-xs font-bold text-slate-900">{title}</span>
              <Pill tone={running ? 'emerald' : 'amber'}>{running ? 'Running' : 'Paused'}</Pill>
            </div>
            <p className="text-[11px] text-slate-500 leading-relaxed mt-0.5">{description}</p>
          </div>
        </div>
        <button
          onClick={onToggle}
          disabled={busy}
          className={`p-2.5 rounded-xl border active:scale-95 transition disabled:opacity-40 shrink-0 ${
            running
              ? 'bg-slate-100 hover:bg-slate-200 border-slate-200 text-slate-700'
              : 'bg-emerald-50 border-emerald-200 text-emerald-700'
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
  rooms = [],
  currentRoom = '',
  busy,
  onToggleUploads,
  onToggleMonitoring,
  onToggleSync,
  onRescan,
  onRetryAllFailed,
  onSyncNow,
  onLogout,
  onFolderUpdated,
  onFileUploaded,
}: ControlsScreenProps) {
  // --- Folder Management State ---
  const [folderPathInput, setFolderPathInput] = useState<string>(agentInfo?.monitorFolder || '');
  const [activeWatchedPath, setActiveWatchedPath] = useState<string>(agentInfo?.monitorFolder || '');
  const [folderExists, setFolderExists] = useState<boolean>(true);
  const [isWatcherRunning, setIsWatcherRunning] = useState<boolean>(true);
  const [savingFolder, setSavingFolder] = useState<boolean>(false);
  const [folderMsg, setFolderMsg] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  // --- Manual Upload State ---
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [isDragging, setIsDragging] = useState<boolean>(false);
  const [targetRoom, setTargetRoom] = useState<string>(currentRoom && currentRoom !== 'ALL' ? currentRoom : (agentInfo?.roomId || '603'));
  const [customBatch, setCustomBatch] = useState<string>('');
  const [customSubject, setCustomSubject] = useState<string>('');
  const [isUploading, setIsUploading] = useState<boolean>(false);
  const [copiedIp, setCopiedIp] = useState<string | null>(null);
  const [uploadMsg, setUploadMsg] = useState<{
    type: 'success' | 'error';
    text: string;
    lectureId?: string;
    batch?: string;
  } | null>(null);

  // --- Preview & YouTube Studio State ---
  const [previewFile, setPreviewFile] = useState<File | null>(null);
  const [previewLecture, setPreviewLecture] = useState<LectureSession | null>(null);
  const [loadingPreviewLecture, setLoadingPreviewLecture] = useState<boolean>(false);


  const fileInputRef = useRef<HTMLInputElement>(null);

  // --- System Health Watchdog State ---
  const [systemHealth, setSystemHealth] = useState<SystemHealth | null>(null);

  // --- Auto-Start State ---
  const [autoStartEnabled, setAutoStartEnabled] = useState<boolean>(false);
  const [autoStartLoading, setAutoStartLoading] = useState<boolean>(false);

  // --- Diagnostics State ---
  const [diagExporting, setDiagExporting] = useState<boolean>(false);
  const [diagResult, setDiagResult] = useState<{ path: string; size: number } | null>(null);
  const [diagError, setDiagError] = useState<string | null>(null);

  // --- Bandwidth & Schedule State ---
  const [bandwidthSettings, setBandwidthSettings] = useState<BandwidthSettings>({
    maxUploadSpeedMbps: 0,
    offPeakEnabled: false,
    offPeakStart: '20:00',
    offPeakEnd: '08:00',
    pauseDuringClassHours: false,
    currentStatus: 'Unrestricted',
  });
  const [savingBandwidth, setSavingBandwidth] = useState<boolean>(false);

  // --- Mobile Cloud Tunnel State ---
  const [tunnelStatus, setTunnelStatus] = useState<api.TunnelStatus | null>(null);
  const [tunnelLoading, setTunnelLoading] = useState<boolean>(false);
  const [tunnelQrCode, setTunnelQrCode] = useState<string | null>(null);
  const [copiedTunnelUrl, setCopiedTunnelUrl] = useState<boolean>(false);

  useEffect(() => {
    let mounted = true;
    api.fetchTunnelStatus().then(async (status) => {
      if (!mounted) return;
      setTunnelStatus(status);
      if (status.active && status.url) {
        try {
          const qr = await QRCode.toDataURL(status.url, { width: 240, margin: 1 });
          if (mounted) setTunnelQrCode(qr);
        } catch {}
      }
    }).catch(() => {});

    return () => {
      mounted = false;
    };
  }, []);

  const handleToggleTunnel = async () => {
    setTunnelLoading(true);
    try {
      if (tunnelStatus?.active) {
        const res = await api.stopTunnel();
        setTunnelStatus(res);
        setTunnelQrCode(null);
      } else {
        const res = await api.startTunnel();
        setTunnelStatus(res);
        if (res.active && res.url) {
          const qr = await QRCode.toDataURL(res.url, { width: 240, margin: 1 });
          setTunnelQrCode(qr);
        }
      }
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : 'Tunnel connection failed');
    } finally {
      setTunnelLoading(false);
    }
  };

  const copyTunnel = (url: string) => {
    navigator.clipboard.writeText(url);
    setCopiedTunnelUrl(true);
    setTimeout(() => setCopiedTunnelUrl(false), 2000);
  };

  // Poll system health every 5 seconds (only in Tauri mode)
  useEffect(() => {
    if (!isTauri()) return;
    const poll = async () => {
      const h = await getSystemHealth(agentInfo?.monitorFolder);
      if (h) setSystemHealth(h);
    };
    poll();
    const interval = setInterval(poll, 5000);
    return () => clearInterval(interval);
  }, [agentInfo?.monitorFolder]);

  // Check autostart status on mount
  useEffect(() => {
    if (!isTauri()) return;
    isAutoStartEnabled().then(setAutoStartEnabled);
  }, []);

  const handleToggleAutoStart = async () => {
    setAutoStartLoading(true);
    try {
      if (autoStartEnabled) {
        await disableAutoStart();
        setAutoStartEnabled(false);
      } else {
        await enableAutoStart();
        setAutoStartEnabled(true);
      }
    } catch {
      // silently ignore
    } finally {
      setAutoStartLoading(false);
    }
  };

  const handleExportDiagnostics = async () => {
    setDiagExporting(true);
    setDiagResult(null);
    setDiagError(null);
    try {
      const result = await exportDiagnostics();
      if (result) {
        setDiagResult({ path: result.savedPath, size: result.sizeBytes });
      }
    } catch (err) {
      setDiagError(err instanceof Error ? err.message : 'Export failed');
    } finally {
      setDiagExporting(false);
    }
  };

  // Fetch bandwidth settings on mount
  useEffect(() => {
    if (!isTauri()) return;
    getBandwidthSettings().then(setBandwidthSettings);
  }, []);

  const handleUpdateBandwidth = async (partial: Partial<BandwidthSettings>) => {
    const updated = { ...bandwidthSettings, ...partial };
    setBandwidthSettings(updated);
    setSavingBandwidth(true);
    try {
      await saveBandwidthSettings(updated);
    } finally {
      setSavingBandwidth(false);
    }
  };

  // Sync folder path on mount or agentInfo change
  useEffect(() => {
    api.fetchMonitoredFolder()
      .then((data) => {
        if (data.folderPath) {
          setFolderPathInput(data.folderPath);
          setActiveWatchedPath(data.folderPath);
          setFolderExists(data.exists);
          setIsWatcherRunning(data.isRunning);
        }
      })
      .catch(() => {
        if (agentInfo?.monitorFolder) {
          setFolderPathInput(agentInfo.monitorFolder);
          setActiveWatchedPath(agentInfo.monitorFolder);
        }
      });
  }, [agentInfo?.monitorFolder]);

  useEffect(() => {
    if (currentRoom && currentRoom !== 'ALL') {
      setTargetRoom(currentRoom);
    }
  }, [currentRoom]);

  const copy = async (text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      setCopiedIp(text);
      setTimeout(() => setCopiedIp(null), 2000);
    } catch {
      // Clipboard permission denied on older browsers
    }
  };

  const handleSaveFolder = async () => {
    const trimmed = folderPathInput.trim();
    if (!trimmed) return;

    setSavingFolder(true);
    setFolderMsg(null);
    try {
      const res = await api.updateMonitoredFolder(trimmed);
      setActiveWatchedPath(res.folderPath);
      setFolderExists(true);
      setIsWatcherRunning(res.isRunning);
      setFolderMsg({
        type: 'success',
        text: `Now watching "${res.folderPath}" (${res.newlyTracked} new files tracked)`,
      });
      if (onFolderUpdated) onFolderUpdated();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to update folder';
      setFolderMsg({ type: 'error', text: msg });
    } finally {
      setSavingFolder(false);
    }
  };

  const detectSubjectFromFilename = (filename: string) => {
    const lower = filename.toLowerCase();
    const found = STANDARD_SUBJECTS.find((sub) => lower.includes(sub.toLowerCase()));
    if (found && !customSubject) {
      setCustomSubject(found);
    }
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      const file = e.target.files[0];
      setSelectedFile(file);
      setUploadMsg(null);
      detectSubjectFromFilename(file.name);
    }
  };

  const handleWatchUploaded = async (lectureId: string) => {
    setLoadingPreviewLecture(true);
    try {
      const lecture = await api.fetchLectureById(lectureId);
      if (lecture) {
        setPreviewLecture(lecture);
      }
    } catch {
      setPreviewLecture({
        lectureSessionId: lectureId,
        date: new Date().toISOString().split('T')[0],
        batchId: uploadMsg?.batch || 'Uploaded Batch',
        subject: customSubject || 'Lecture',
        hasVideo: true,
        driveVideoUrl: null,
        youtubeVideoId: null,
        youtubeUrl: null,
        youtubeStatus: 'Disabled',
        youtubePublishedAt: null,
      } as unknown as LectureSession);
    } finally {
      setLoadingPreviewLecture(false);
    }
  };

  const handleUploadSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedFile) return;

    setIsUploading(true);
    setUploadMsg(null);

    try {
      const formData = new FormData();
      formData.append('file', selectedFile);
      if (targetRoom && targetRoom !== 'ALL') {
        formData.append('roomId', targetRoom);
      }
      if (customBatch.trim()) {
        formData.append('batchId', customBatch.trim());
      }
      if (customSubject.trim()) {
        formData.append('subjectId', customSubject.trim());
      }

      const res = await api.uploadLectureFile(formData);
      setUploadMsg({
        type: 'success',
        text: res.message || 'File uploaded and processed successfully!',
        lectureId: res.lecture?.lectureSessionId,
        batch: res.matchedBatch || res.lecture?.batchId,
      });

      // Clear selection
      setSelectedFile(null);
      if (fileInputRef.current) fileInputRef.current.value = '';
      if (onFileUploaded) onFileUploaded();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'File upload failed';
      setUploadMsg({ type: 'error', text: msg });
    } finally {
      setIsUploading(false);
    }
  };

  const failedCount = snapshot?.summary.failed ?? 0;

  return (
    <div className="space-y-4">
      {/* Hero Operations Center */}
      <div className="bg-[var(--paper)] border border-[var(--rule)] p-5 space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div>
            <div className="flex items-center gap-2">
              <div className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
              <h2 className="font-serif text-lg font-normal text-[var(--ink)]">Control Hub & Live Operations</h2>
            </div>
            <p className="text-xs font-mono text-[var(--stone)] mt-0.5">
              Live ingest pipelines, recording directory sync, and automated cloud distribution
            </p>
          </div>

          {/* Service Live Badges */}
          <div className="flex items-center gap-2 flex-wrap font-mono text-[10px] uppercase tracking-wider">
            <span
              className={`px-2.5 py-1 border flex items-center gap-1.5 ${
                !controlState?.uploadsPaused
                  ? 'bg-emerald-950/20 text-emerald-500 border-emerald-700/40'
                  : 'bg-amber-950/20 text-amber-500 border-amber-700/40'
              }`}
            >
              <span className={`w-1.5 h-1.5 rounded-full ${!controlState?.uploadsPaused ? 'bg-emerald-500' : 'bg-amber-500'}`} />
              Drive Uploads: {!controlState?.uploadsPaused ? 'Active' : 'Paused'}
            </span>

            <span
              className={`px-2.5 py-1 border flex items-center gap-1.5 ${
                isWatcherRunning && folderExists
                  ? 'bg-[var(--cream)] text-[var(--ink)] border-[var(--rule)]'
                  : 'bg-amber-950/20 text-amber-500 border-amber-700/40'
              }`}
            >
              <span className={`w-1.5 h-1.5 rounded-full ${isWatcherRunning && folderExists ? 'bg-emerald-500' : 'bg-amber-500'}`} />
              Watcher: {isWatcherRunning && folderExists ? 'Active' : 'Paused'}
            </span>
          </div>
        </div>

        {/* Quick Actions Bar */}
        <div className="pt-3 border-t border-[var(--rule)] flex items-center justify-between flex-wrap gap-2 font-mono">
          <div className="flex items-center gap-2">
            <button
              onClick={onRescan}
              disabled={busy}
              className="px-3 py-1.5 text-xs uppercase tracking-wider border border-[var(--rule)] bg-[var(--cream)] hover:bg-[var(--paper)] text-[var(--ink)] transition cursor-pointer flex items-center gap-1.5 disabled:opacity-50"
            >
              <FolderSearch className="w-3.5 h-3.5 text-[var(--stone)]" />
              <span>Rescan Recordings Folder</span>
            </button>
            <button
              onClick={onSyncNow}
              disabled={busy || controlState?.timetableSyncPaused}
              className="px-3 py-1.5 text-xs uppercase tracking-wider border border-[var(--rule)] bg-[var(--cream)] hover:bg-[var(--paper)] text-[var(--ink)] transition cursor-pointer flex items-center gap-1.5 disabled:opacity-50"
            >
              <CloudUpload className="w-3.5 h-3.5 text-[var(--stone)]" />
              <span>Sync Timetable Sheet</span>
            </button>
            {failedCount > 0 && (
              <button
                onClick={onRetryAllFailed}
                disabled={busy}
                className="px-3 py-1.5 text-xs uppercase tracking-wider border border-red-700/40 bg-red-950/20 text-red-400 transition cursor-pointer flex items-center gap-1.5 disabled:opacity-50"
              >
                <RefreshCw className="w-3.5 h-3.5" />
                <span>Retry Failed ({failedCount})</span>
              </button>
            )}
          </div>
        </div>
      </div>

      {/* Desktop 2-Column Grid */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 items-start">
        {/* Left Column: Folders and Manual Upload */}
        <div className="space-y-6">
          {/* 1. Monitored Directory Configuration */}
          <Card className="space-y-3 font-mono">
        <div className="flex items-center justify-between border-b border-[var(--rule)] pb-2.5">
          <div className="flex items-center gap-2">
            <div className="w-7 h-7 border border-[var(--rule)] bg-[var(--cream)] flex items-center justify-center text-[var(--ink)]">
              <FolderOpen className="w-4 h-4" />
            </div>
            <div>
              <h3 className="font-serif text-sm font-normal text-[var(--ink)]">Recording Source Folder</h3>
              <p className="text-[10px] text-[var(--stone)]">Directory watched by the agent for new lecture videos and notes</p>
            </div>
          </div>
          <Pill tone={isWatcherRunning && folderExists ? 'emerald' : 'amber'}>
            {isWatcherRunning ? (folderExists ? 'Watching' : 'Folder Missing') : 'Watcher Paused'}
          </Pill>
        </div>

        <div className="space-y-2">
          <div className="text-[11px] text-[var(--stone)] flex items-center justify-between">
            <span>Current Monitored Path:</span>
            <span className="text-[10px] text-[var(--ink)] border border-[var(--rule)] bg-[var(--cream)] px-2 py-0.5 truncate max-w-[280px]">
              {activeWatchedPath || 'Not configured'}
            </span>
          </div>

          <div className="flex gap-2">
            <input
              type="text"
              value={folderPathInput}
              onChange={(e) => setFolderPathInput(e.target.value)}
              placeholder="e.g. D:\Recordings or /Users/.../Documents/Recordings"
              className="flex-1 text-xs font-mono px-3 py-2 bg-[var(--cream)] border border-[var(--rule)] text-[var(--ink)] focus:outline-none"
            />
            {isTauri() && (
              <button
                type="button"
                onClick={async () => {
                  const picked = await pickFolder(folderPathInput || undefined);
                  if (picked) setFolderPathInput(picked);
                }}
                className="px-3 py-2 text-xs font-mono uppercase tracking-wider border border-[var(--rule)] bg-[var(--cream)] hover:bg-[var(--paper)] text-[var(--ink)] transition cursor-pointer shrink-0"
                title="Browse folder on your PC"
              >
                Browse...
              </button>
            )}
            <button
              onClick={handleSaveFolder}
              disabled={savingFolder || !folderPathInput.trim() || folderPathInput.trim() === activeWatchedPath}
              className="px-3.5 py-2 text-xs font-mono uppercase tracking-wider bg-[var(--ink)] hover:opacity-90 text-[var(--cream)] transition disabled:opacity-40 flex items-center gap-1.5 shrink-0 cursor-pointer"
            >
              {savingFolder ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Save className="w-3.5 h-3.5" />}
              <span>Update Folder</span>
            </button>
          </div>

          {/* Quick Rescan & Folder Status */}
          <div className="flex items-center justify-between pt-1">
            <button
              onClick={onRescan}
              disabled={busy}
              className="text-[11px] text-cyan-600 hover:text-cyan-700 font-semibold flex items-center gap-1.5 transition active:scale-95"
            >
              <FolderSearch className="w-3.5 h-3.5" />
              <span>Rescan folder for new files now</span>
            </button>
            <span className="text-[10px] text-slate-400">Subdirectories auto-scanned</span>
          </div>

          {folderMsg && (
            <div
              className={`p-2.5 rounded-xl text-xs flex items-center gap-2 ${
                folderMsg.type === 'success'
                  ? 'bg-emerald-50 text-emerald-800 border border-emerald-200'
                  : 'bg-rose-50 text-rose-800 border border-rose-200'
              }`}
            >
              {folderMsg.type === 'success' ? (
                <CheckCircle2 className="w-4 h-4 text-emerald-600 shrink-0" />
              ) : (
                <AlertCircle className="w-4 h-4 text-rose-600 shrink-0" />
              )}
              <span className="text-[11px] font-medium">{folderMsg.text}</span>
            </div>
          )}
        </div>
      </Card>

      {/* 2. Manual File Ingestion / Upload */}
      <Card className="space-y-3 font-mono">
        <div className="flex items-center justify-between border-b border-[var(--rule)] pb-2.5">
          <div className="flex items-center gap-2">
            <div className="w-7 h-7 border border-[var(--rule)] bg-[var(--cream)] flex items-center justify-center text-[var(--ink)]">
              <FileUp className="w-4 h-4" />
            </div>
            <div>
              <h3 className="font-serif text-sm font-normal text-[var(--ink)]">Manual File Ingestion / Upload</h3>
              <p className="text-[10px] text-[var(--stone)]">Pick any lecture recording (.mp4, .mkv) or notes (.pdf) from this device</p>
            </div>
          </div>
          <Pill tone="cyan">Zero-Touch Match</Pill>
        </div>

        <form onSubmit={handleUploadSubmit} className="space-y-3">
          {/* File Picker with Native Drag-and-Drop */}
          <div
            onClick={() => fileInputRef.current?.click()}
            onDragOver={(e) => {
              e.preventDefault();
              e.stopPropagation();
              setIsDragging(true);
            }}
            onDragLeave={(e) => {
              e.preventDefault();
              e.stopPropagation();
              setIsDragging(false);
            }}
            onDrop={(e) => {
              e.preventDefault();
              e.stopPropagation();
              setIsDragging(false);
              if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
                const file = e.dataTransfer.files[0];
                setSelectedFile(file);
                setUploadMsg(null);
                detectSubjectFromFilename(file.name);
              }
            }}
            className={`border border-dashed p-6 text-center cursor-pointer transition ${
              isDragging
                ? 'border-[var(--ink)] bg-[var(--cream)]'
                : selectedFile
                ? 'border-[var(--ink)] bg-[var(--cream)]'
                : 'border-[var(--rule)] hover:border-[var(--stone)] bg-[var(--cream)]'
            }`}
          >
            <input
              ref={fileInputRef}
              type="file"
              accept="video/*,.mp4,.mkv,.webm,.avi,application/pdf,.pdf"
              onChange={handleFileChange}
              className="hidden"
            />
            {isDragging ? (
              <div className="space-y-1 py-1 font-mono">
                <UploadCloud className="w-6 h-6 text-[var(--ink)] mx-auto animate-bounce" />
                <p className="text-xs text-[var(--ink)]">Drop recording or notes file here</p>
              </div>
            ) : selectedFile ? (
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 text-left font-mono">
                <div className="flex items-center gap-3 min-w-0">
                  <div className="w-8 h-8 border border-[var(--rule)] bg-[var(--paper)] flex items-center justify-center text-[var(--stone)] shrink-0">
                    {selectedFile.name.toLowerCase().endsWith('.pdf') ? (
                      <FileText className="w-4 h-4" />
                    ) : (
                      <Video className="w-4 h-4" />
                    )}
                  </div>
                  <div className="min-w-0">
                    <p className="text-xs text-[var(--ink)] truncate max-w-[220px]">{selectedFile.name}</p>
                    <p className="text-[10px] text-[var(--stone)]">
                      {formatBytes(selectedFile.size)} · Click to replace
                    </p>
                  </div>
                </div>

                <div className="flex items-center gap-2 shrink-0" onClick={(e) => e.stopPropagation()}>
                  <button
                    type="button"
                    onClick={() => setPreviewFile(selectedFile)}
                    className="px-3 py-1.5 border border-[var(--rule)] bg-[var(--paper)] text-[var(--ink)] text-xs uppercase tracking-wider flex items-center gap-1.5 transition cursor-pointer"
                    title="Preview video playback or PDF notes before uploading"
                  >
                    <Play className="w-3 h-3 fill-current" />
                    <span>Preview</span>
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      setSelectedFile(null);
                      if (fileInputRef.current) fileInputRef.current.value = '';
                    }}
                    className="px-2.5 py-1.5 border border-[var(--rule)] bg-[var(--cream)] text-[var(--stone)] hover:text-red-500 text-xs uppercase tracking-wider transition cursor-pointer"
                    title="Clear file"
                  >
                    ✕ Clear
                  </button>
                </div>
              </div>
            ) : (
              <div className="space-y-1 font-mono">
                <UploadCloud className="w-6 h-6 text-[var(--stone)] mx-auto" />
                <p className="text-xs text-[var(--ink)]">Click or drag & drop recording / notes file</p>
                <p className="text-[10px] text-[var(--stone)]">Supports MP4, MKV, WEBM, PDF (up to 10 GB)</p>
              </div>
            )}
          </div>

          {/* Optional Meta Filters */}
          <div className="grid grid-cols-3 gap-2 font-mono">
            <div>
              <label className="text-[10px] uppercase text-[var(--stone)] block mb-1">Target Room</label>
              <select
                value={targetRoom}
                onChange={(e) => setTargetRoom(e.target.value)}
                className="w-full text-xs px-2.5 py-1.5 bg-[var(--cream)] border border-[var(--rule)] text-[var(--ink)] focus:outline-none"
              >
                {rooms.length > 0 ? (
                  rooms.map((r) => (
                    <option key={r} value={r}>
                      Room {r}
                    </option>
                  ))
                ) : (
                  <option value={agentInfo?.roomId || '603'}>Room {agentInfo?.roomId || '603'}</option>
                )}
              </select>
            </div>

            <div>
              <label className="text-[10px] uppercase text-[var(--stone)] block mb-1">Batch (Optional)</label>
              <input
                type="text"
                value={customBatch}
                onChange={(e) => setCustomBatch(e.target.value)}
                placeholder="e.g. 11TH-JEE-A"
                className="w-full text-xs px-2.5 py-1.5 bg-[var(--cream)] border border-[var(--rule)] text-[var(--ink)] focus:outline-none"
              />
            </div>

            <div>
              <label className="text-[10px] uppercase text-[var(--stone)] block mb-1">Subject (Optional)</label>
              <input
                type="text"
                value={customSubject}
                onChange={(e) => setCustomSubject(e.target.value)}
                placeholder="e.g. Physics"
                className="w-full text-xs px-2.5 py-1.5 bg-[var(--cream)] border border-[var(--rule)] text-[var(--ink)] focus:outline-none"
              />
            </div>
          </div>

          {/* Quick Subject Selection Chips (8 Standard Timetable Subjects) */}
          <div className="space-y-1.5 pt-0.5 font-mono">
            <span className="text-[10px] uppercase tracking-wider text-[var(--stone)] block">
              Quick Subject Select:
            </span>
            <div className="flex flex-wrap gap-1.5">
              {STANDARD_SUBJECTS.map((subj) => (
                <button
                  key={subj}
                  type="button"
                  onClick={() => setCustomSubject(subj)}
                  className={`px-2 py-0.5 text-[10px] uppercase tracking-wider border transition cursor-pointer ${
                    customSubject.toLowerCase() === subj.toLowerCase()
                      ? 'bg-[var(--ink)] text-[var(--cream)] border-[var(--ink)]'
                      : 'bg-[var(--cream)] hover:bg-[var(--paper)] text-[var(--ink)] border-[var(--rule)]'
                  }`}
                >
                  {subj}
                </button>
              ))}
            </div>
          </div>

          <div className="flex items-center justify-between pt-1 font-mono">
            <p className="text-[10px] text-[var(--stone)]">
              {customBatch.trim()
                ? 'Batch is specified; direct Drive queueing will be used.'
                : 'Empty batch = Auto-detected via OCR & timetable matching engine.'}
            </p>
            <button
              type="submit"
              disabled={!selectedFile || isUploading}
              className="px-4 py-2 text-xs uppercase tracking-wider bg-[var(--ink)] hover:opacity-90 text-[var(--cream)] transition disabled:opacity-40 flex items-center gap-1.5 shrink-0 cursor-pointer"
            >
              {isUploading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <FileCheck2 className="w-3.5 h-3.5" />}
              <span>{isUploading ? 'Ingesting File...' : 'Upload & Process'}</span>
            </button>
          </div>

          {uploadMsg && (
            <div
              className={`p-3 rounded-xl text-xs flex items-center justify-between gap-2.5 ${
                uploadMsg.type === 'success'
                  ? 'bg-emerald-50 text-emerald-800 border border-emerald-200'
                  : 'bg-rose-50 text-rose-800 border border-rose-200'
              }`}
            >
              <div className="flex items-start gap-2.5 min-w-0">
                {uploadMsg.type === 'success' ? (
                  <CheckCircle2 className="w-4 h-4 text-emerald-600 shrink-0 mt-0.5" />
                ) : (
                  <AlertCircle className="w-4 h-4 text-rose-600 shrink-0 mt-0.5" />
                )}
                <div className="space-y-0.5 min-w-0">
                  <p className="font-semibold text-xs">{uploadMsg.text}</p>
                  {uploadMsg.lectureId && (
                    <p className="text-[10px] font-mono text-emerald-700">
                      Session ID: {uploadMsg.lectureId} {uploadMsg.batch ? `· Batch: ${uploadMsg.batch}` : ''}
                    </p>
                  )}
                </div>
              </div>

              {uploadMsg.lectureId && (
                <button
                  type="button"
                  onClick={() => handleWatchUploaded(uploadMsg.lectureId!)}
                  disabled={loadingPreviewLecture}
                  className="px-3 py-1.5 rounded-xl bg-emerald-600 hover:bg-emerald-700 text-white font-semibold text-xs flex items-center gap-1.5 shadow-2xs shrink-0 active:scale-95 transition"
                >
                  {loadingPreviewLecture ? (
                    <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  ) : (
                    <Play className="w-3.5 h-3.5 fill-current" />
                  )}
                  <span>Watch Lecture</span>
                </button>
              )}
            </div>
          )}
        </form>
      </Card>
    </div>

    {/* Right Column: Runtime Switches, Diagnostics, Session */}
    <div className="space-y-6">

      {/* System Health Watchdog */}
      {isTauri() && systemHealth && (() => {
        const raw = systemHealth as any;
        const cpuUsage = Number(systemHealth.cpuUsagePercent ?? raw.cpu_usage_percent ?? 0);
        const ramUsed = Number(systemHealth.ramUsedBytes ?? raw.ram_used_bytes ?? 0);
        const ramTotal = Number(systemHealth.ramTotalBytes ?? raw.ram_total_bytes ?? 1);
        const diskFree = Number(systemHealth.diskFreeBytes ?? raw.disk_free_bytes ?? 0);
        const diskTotal = Number(systemHealth.diskTotalBytes ?? raw.disk_total_bytes ?? 1);
        const diskUsage = Number(systemHealth.diskUsagePercent ?? raw.disk_usage_percent ?? 0);
        const diskMount = String(systemHealth.diskMountPoint ?? raw.disk_mount_point ?? '/');
        const uptimeSecs = Number(systemHealth.uptimeSeconds ?? raw.uptime_seconds ?? 0);
        const isLowDisk = diskFree < 20 * 1_073_741_824;

        return (
          <Card className="space-y-3">
            <div className="flex items-center justify-between border-b border-slate-100 pb-2.5">
              <div className="flex items-center gap-2">
                <div className="p-1.5 rounded-lg bg-emerald-50 text-emerald-600">
                  <Cpu className="w-4 h-4" />
                </div>
                <div>
                  <h3 className="text-xs font-bold text-slate-900">System Health</h3>
                  <p className="text-[10px] text-slate-500">Live CPU, RAM & recording drive metrics</p>
                </div>
              </div>
              <Pill tone={isLowDisk ? 'red' : 'emerald'}>
                {isLowDisk ? 'Low Disk' : 'Healthy'}
              </Pill>
            </div>

            {/* Low Disk Alert */}
            {isLowDisk && (
              <div className="p-2.5 rounded-xl bg-red-50 border border-red-200 flex items-center gap-2">
                <AlertTriangle className="w-4 h-4 text-red-600 shrink-0" />
                <p className="text-[11px] font-semibold text-red-800">
                  Low Disk Space! Only {(diskFree / 1_073_741_824).toFixed(1)} GB free. Clean up old recordings to prevent OBS failures.
                </p>
              </div>
            )}

            {/* CPU */}
            <div className="space-y-1">
              <div className="flex items-center justify-between text-[11px]">
                <span className="text-slate-600 flex items-center gap-1.5"><Cpu className="w-3 h-3 text-slate-400" /> CPU</span>
                <span className="font-bold text-slate-800">{cpuUsage.toFixed(0)}%</span>
              </div>
              <div className="h-1.5 bg-slate-100 rounded-full overflow-hidden">
                <div
                  className={`h-full rounded-full transition-all duration-500 ${cpuUsage > 85 ? 'bg-red-500' : cpuUsage > 60 ? 'bg-amber-500' : 'bg-emerald-500'}`}
                  style={{ width: `${Math.min(cpuUsage, 100)}%` }}
                />
              </div>
            </div>

            {/* RAM */}
            <div className="space-y-1">
              <div className="flex items-center justify-between text-[11px]">
                <span className="text-slate-600 flex items-center gap-1.5"><MemoryStick className="w-3 h-3 text-slate-400" /> RAM</span>
                <span className="font-bold text-slate-800">
                  {(ramUsed / 1_073_741_824).toFixed(1)} / {(ramTotal / 1_073_741_824).toFixed(1)} GB
                </span>
              </div>
              <div className="h-1.5 bg-slate-100 rounded-full overflow-hidden">
                <div
                  className={`h-full rounded-full transition-all duration-500 ${ramUsed / ramTotal > 0.85 ? 'bg-red-500' : ramUsed / ramTotal > 0.7 ? 'bg-amber-500' : 'bg-cyan-500'}`}
                  style={{ width: `${((ramUsed / ramTotal) * 100).toFixed(0)}%` }}
                />
              </div>
            </div>

            {/* Disk */}
            <div className="space-y-1">
              <div className="flex items-center justify-between text-[11px]">
                <span className="text-slate-600 flex items-center gap-1.5">
                  <HardDrive className="w-3 h-3 text-slate-400" /> Disk
                  <span className="text-[9px] text-slate-400 font-mono">({diskMount})</span>
                </span>
                <span className="font-bold text-slate-800">
                  {(diskFree / 1_073_741_824).toFixed(0)} GB free / {(diskTotal / 1_073_741_824).toFixed(0)} GB
                </span>
              </div>
              <div className="h-1.5 bg-slate-100 rounded-full overflow-hidden">
                <div
                  className={`h-full rounded-full transition-all duration-500 ${diskUsage > 90 ? 'bg-red-500' : diskUsage > 75 ? 'bg-amber-500' : 'bg-indigo-500'}`}
                  style={{ width: `${diskUsage.toFixed(0)}%` }}
                />
              </div>
            </div>

            {/* Uptime */}
            <div className="flex items-center justify-between text-[10px] text-slate-400 pt-1 border-t border-slate-100">
              <span>System Uptime</span>
              <span className="font-mono">
                {Math.floor(uptimeSecs / 3600)}h {Math.floor((uptimeSecs % 3600) / 60)}m
              </span>
            </div>
          </Card>
        );
      })()}

      {/* 3. Runtime Switches */}
      <Card className="space-y-0">
        <ControlRow
          icon={<UploadCloud className="w-4 h-4 text-cyan-600" />}
          title="Upload Engine"
          description="Sends queued recordings to Google Drive (up to 3 concurrent). Pausing does not stop an upload already in flight."
          running={!controlState?.uploadsPaused}
          onToggle={onToggleUploads}
          busy={busy}
        >
          {failedCount > 0 ? (
            <ActionButton tone="danger" onClick={onRetryAllFailed} disabled={busy} className="flex-1">
              <RefreshCw className="w-3.5 h-3.5" />
              Retry all failed ({failedCount})
            </ActionButton>
          ) : (
            <div className="text-[11px] text-slate-400 flex items-center gap-1.5 py-1">
              <CheckCircle2 className="w-3.5 h-3.5 text-emerald-500" />
              <span>Queue healthy · 0 failed transfers</span>
            </div>
          )}
        </ControlRow>

        <ControlRow
          icon={<MonitorPause className="w-4 h-4 text-indigo-600" />}
          title="File Monitoring"
          description="Watches the recording folder for new files. While paused, new files are ignored."
          running={!controlState?.monitoringPaused}
          onToggle={onToggleMonitoring}
          busy={busy}
        >
          <ActionButton tone="slate" onClick={onRescan} disabled={busy} className="flex-1">
            <FolderSearch className="w-3.5 h-3.5 text-slate-500" />
            Rescan folder now
          </ActionButton>
        </ControlRow>

        <ControlRow
          icon={<CalendarClock className="w-4 h-4 text-amber-600" />}
          title="Timetable Sheet Sync"
          description={`Pulls timetable from Google Sheets every ${agentInfo?.sheetSyncIntervalMinutes ?? 5} min and updates the schedule.`}
          running={!controlState?.timetableSyncPaused}
          onToggle={onToggleSync}
          busy={busy}
        >
          <ActionButton tone="slate" onClick={onSyncNow} disabled={busy || controlState?.timetableSyncPaused} className="flex-1">
            <CloudUpload className="w-3.5 h-3.5 text-slate-500" />
            Sync from sheet now
          </ActionButton>
        </ControlRow>
      </Card>

      {/* 4. Agent Information */}
      <Card className="space-y-2.5 text-xs">
        <div className="flex items-center justify-between border-b border-slate-100 pb-2">
          <h4 className="font-bold text-slate-900 flex items-center gap-1.5">
            <Server className="w-3.5 h-3.5 text-cyan-600" />
            Agent Info
          </h4>
          <span className="text-[10px] font-mono text-slate-400">V{agentInfo?.agentVersion || '1.0.0'}</span>
        </div>
        <div className="flex justify-between py-0.5">
          <span className="text-slate-500">Center / Room</span>
          <span className="font-semibold text-slate-800">
            {agentInfo?.centerId} · Room {agentInfo?.roomId}
          </span>
        </div>
        <div className="flex justify-between py-0.5">
          <span className="text-slate-500">Host</span>
          <span className="font-mono text-slate-700">{agentInfo?.machineName ?? '...'}</span>
        </div>
        <div className="flex justify-between py-0.5">
          <span className="text-slate-500">Monitored Folder</span>
          <span className="font-mono text-cyan-700 truncate max-w-[200px]" title={agentInfo?.monitorFolder}>
            {agentInfo?.monitorFolder ?? '...'}
          </span>
        </div>
        <div className="flex justify-between py-0.5">
          <span className="text-slate-500">Google Drive</span>
          <span className="font-semibold text-slate-800">
            {agentInfo?.googleDriveEnabled ? `Live → ${agentInfo.driveRootFolder}` : 'Mock mode'}
          </span>
        </div>
        <div className="flex justify-between py-0.5">
          <span className="text-slate-500">Database</span>
          <span className="font-semibold text-emerald-700">{health?.database ?? '...'}</span>
        </div>
      </Card>

      {/* 5. Mobile & Tablet Access (Cloud Tunnel + WiFi) */}
      <Card className="space-y-4 text-xs">
        <div className="flex items-center justify-between border-b border-slate-100 pb-2.5">
          <div className="flex items-center gap-2">
            <div className="p-1.5 rounded-lg bg-cyan-50 text-cyan-700">
              <Smartphone className="w-4 h-4" />
            </div>
            <div>
              <h4 className="font-bold text-slate-900">Mobile & Tablet Access</h4>
              <p className="text-[10px] text-slate-500">Live remote control from any mobile or classroom device</p>
            </div>
          </div>
          <span
            className={`px-2 py-0.5 rounded-full text-[10px] font-bold border flex items-center gap-1.5 ${
              tunnelStatus?.active
                ? 'bg-emerald-50 text-emerald-700 border-emerald-200'
                : 'bg-slate-100 text-slate-600 border-slate-200'
            }`}
          >
            <span
              className={`w-1.5 h-1.5 rounded-full ${
                tunnelStatus?.active ? 'bg-emerald-500 animate-pulse' : 'bg-slate-400'
              }`}
            />
            {tunnelStatus?.active ? 'Tunnel Live' : 'Offline'}
          </span>
        </div>

        {/* 1-Click Cloud Tunnel Section */}
        <div className="bg-gradient-to-br from-slate-900 via-slate-800 to-cyan-950 rounded-2xl p-4 text-white shadow-md relative overflow-hidden">
          <div className="flex items-start justify-between gap-3 relative z-10">
            <div>
              <div className="flex items-center gap-2">
                <Globe className="w-4 h-4 text-cyan-400" />
                <h5 className="font-bold text-sm tracking-tight text-white">1-Click Cloud Access (Any Network)</h5>
              </div>
              <p className="text-[11px] text-slate-300 mt-1 max-w-sm">
                Connect your phone from anywhere (4G, 5G, mobile data, or classroom Wi-Fi) with zero configuration.
              </p>
            </div>
            <button
              onClick={handleToggleTunnel}
              disabled={tunnelLoading}
              className={`px-3.5 py-1.5 rounded-xl font-bold text-xs flex items-center gap-1.5 transition shadow-sm shrink-0 active:scale-95 disabled:opacity-50 ${
                tunnelStatus?.active
                  ? 'bg-rose-500 hover:bg-rose-600 text-white'
                  : 'bg-cyan-500 hover:bg-cyan-400 text-slate-950 font-black'
              }`}
            >
              {tunnelLoading ? (
                <>
                  <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  <span>Connecting...</span>
                </>
              ) : tunnelStatus?.active ? (
                <>
                  <Power className="w-3.5 h-3.5" />
                  <span>Stop Tunnel</span>
                </>
              ) : (
                <>
                  <Sparkles className="w-3.5 h-3.5" />
                  <span>Enable Remote Access</span>
                </>
              )}
            </button>
          </div>

          {tunnelStatus?.active && tunnelStatus.url && (
            <div className="mt-4 pt-3.5 border-t border-slate-700/60 flex flex-col sm:flex-row items-center gap-4 relative z-10">
              {/* QR Code Container */}
              {tunnelQrCode && (
                <div className="bg-white p-2 rounded-xl shadow-lg shrink-0 flex flex-col items-center">
                  <img src={tunnelQrCode} alt="Scan QR for Mobile Access" className="w-32 h-32 rounded-lg" />
                  <span className="text-[9px] font-bold text-slate-600 mt-1 flex items-center gap-1">
                    <QrCode className="w-3 h-3 text-cyan-600" /> Scan with Phone
                  </span>
                </div>
              )}

              {/* URL & Action buttons */}
              <div className="space-y-2.5 flex-1 min-w-0 w-full text-center sm:text-left">
                <div>
                  <span className="text-[10px] uppercase font-bold tracking-wider text-cyan-300">Secure Live URL</span>
                  <div className="mt-1 bg-slate-950/80 border border-cyan-500/30 rounded-xl px-3 py-2 font-mono text-xs text-cyan-200 truncate select-all">
                    {tunnelStatus.url}
                  </div>
                </div>
                <div className="flex flex-wrap items-center gap-2 justify-center sm:justify-start">
                  <button
                    onClick={() => copyTunnel(tunnelStatus.url!)}
                    className="flex items-center gap-1 text-[11px] font-semibold py-1.5 px-3 rounded-lg bg-white/10 hover:bg-white/20 border border-white/20 text-white active:scale-95 transition"
                  >
                    {copiedTunnelUrl ? (
                      <>
                        <Check className="w-3.5 h-3.5 text-emerald-400" />
                        <span className="text-emerald-300 font-bold">Copied!</span>
                      </>
                    ) : (
                      <>
                        <Copy className="w-3.5 h-3.5 text-cyan-300" />
                        <span>Copy URL</span>
                      </>
                    )}
                  </button>
                  <a
                    href={tunnelStatus.url}
                    target="_blank"
                    rel="noreferrer"
                    className="flex items-center gap-1 text-[11px] font-semibold py-1.5 px-3 rounded-lg bg-cyan-600 hover:bg-cyan-500 text-white active:scale-95 transition"
                  >
                    <ExternalLink className="w-3.5 h-3.5" />
                    <span>Open Live App</span>
                  </a>
                </div>
                <p className="text-[10px] text-slate-400">
                  Tip: Point your phone camera at the QR code to open the dashboard immediately.
                </p>
              </div>
            </div>
          )}

          {tunnelStatus?.message && !tunnelStatus.active && (
            <div className="mt-2 text-[11px] text-amber-300 bg-amber-950/40 border border-amber-500/30 rounded-lg p-2 flex items-center gap-2">
              <AlertCircle className="w-3.5 h-3.5 shrink-0" />
              <span>{tunnelStatus.message}</span>
            </div>
          )}
        </div>

        {/* Same WiFi (LAN) Section as alternative */}
        <div className="space-y-2 pt-1">
          <div className="flex items-center justify-between text-[11px] font-semibold text-slate-700">
            <span className="flex items-center gap-1.5">
              <Wifi className="w-3.5 h-3.5 text-slate-400" />
              <span>Local Wi-Fi Network (Same Router Only)</span>
            </span>
            <span className="font-mono text-[10px] text-slate-500">Port {agentInfo?.httpPort || 5200}</span>
          </div>

          {(agentInfo?.lanIpv4Addresses.length ?? 0) === 0 ? (
            <p className="text-[11px] text-slate-500">No LAN IP detected.</p>
          ) : (
            <div className="space-y-1.5">
              {agentInfo!.lanIpv4Addresses.map((ip) => {
                const url = `http://${ip}:${agentInfo!.httpPort}/`;
                return (
                  <div
                    key={ip}
                    className="flex items-center justify-between gap-2 bg-slate-50 border border-slate-200/90 rounded-xl px-3 py-1.5 hover:border-slate-300 transition"
                  >
                    <div className="flex items-center gap-2 min-w-0">
                      <Smartphone className="w-3.5 h-3.5 text-slate-400 shrink-0" />
                      <span className="font-mono text-xs font-semibold text-slate-700 truncate">{url}</span>
                    </div>
                    <button
                      onClick={() => copy(url)}
                      className="flex items-center gap-1 text-[11px] font-semibold py-1 px-2.5 rounded-lg bg-white border border-slate-200 hover:bg-slate-50 text-slate-700 active:scale-95 transition shrink-0 shadow-2xs"
                    >
                      {copiedIp === url ? (
                        <>
                          <Check className="w-3 h-3 text-emerald-600" />
                          <span className="text-emerald-700 font-bold">Copied!</span>
                        </>
                      ) : (
                        <>
                          <Copy className="w-3 h-3 text-slate-400" />
                          <span>Copy</span>
                        </>
                      )}
                    </button>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </Card>

      {/* Bandwidth & Upload Schedule */}
      {isTauri() && (
        <Card className="space-y-3.5">
          <div className="flex items-center justify-between border-b border-slate-100 pb-2.5">
            <div className="flex items-center gap-2">
              <div className="p-1.5 rounded-lg bg-blue-50 text-blue-600">
                <Gauge className="w-4 h-4" />
              </div>
              <div>
                <h3 className="text-xs font-bold text-slate-900">Bandwidth & Upload Schedule</h3>
                <p className="text-[10px] text-slate-500">Prevent classroom streaming lag during lecture hours</p>
              </div>
            </div>
            <Pill tone={bandwidthSettings.currentStatus === 'Throttled' ? 'amber' : bandwidthSettings.currentStatus === 'PeakHoursPaused' ? 'red' : 'emerald'}>
              {bandwidthSettings.currentStatus || 'Unrestricted'}
            </Pill>
          </div>

          {/* Speed Limiter Selector */}
          <div className="space-y-1.5">
            <label className="text-[11px] font-semibold text-slate-700 flex items-center justify-between">
              <span>Upload Speed Cap:</span>
              <span className="font-mono text-cyan-700 text-[10px]">
                {bandwidthSettings.maxUploadSpeedMbps === 0 ? 'Unlimited (Max Network Speed)' : `${bandwidthSettings.maxUploadSpeedMbps} Mbps`}
              </span>
            </label>
            <div className="grid grid-cols-4 gap-1.5">
              {[
                { label: 'Unlimited', val: 0 },
                { label: '5 Mbps', val: 5 },
                { label: '10 Mbps', val: 10 },
                { label: '20 Mbps', val: 20 },
              ].map((opt) => (
                <button
                  key={opt.val}
                  type="button"
                  disabled={savingBandwidth}
                  onClick={() => handleUpdateBandwidth({ maxUploadSpeedMbps: opt.val })}
                  className={`py-1.5 px-2 rounded-xl text-xs font-semibold border transition active:scale-95 ${
                    bandwidthSettings.maxUploadSpeedMbps === opt.val
                      ? 'bg-blue-600 text-white border-blue-600 shadow-2xs'
                      : 'bg-slate-50 hover:bg-slate-100 text-slate-700 border-slate-200'
                  }`}
                >
                  {opt.label}
                </button>
              ))}
            </div>
          </div>

          {/* Off-Peak Schedule Toggle */}
          <div className="pt-2 border-t border-slate-100 space-y-2">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-1.5">
                <Clock className="w-3.5 h-3.5 text-slate-500" />
                <span className="text-xs font-semibold text-slate-800">Off-Peak Overnight Mode</span>
              </div>
              <button
                type="button"
                disabled={savingBandwidth}
                onClick={() => handleUpdateBandwidth({ offPeakEnabled: !bandwidthSettings.offPeakEnabled, pauseDuringClassHours: !bandwidthSettings.offPeakEnabled })}
                className={`p-1.5 rounded-lg border text-xs font-semibold flex items-center gap-1 transition ${
                  bandwidthSettings.offPeakEnabled
                    ? 'bg-emerald-50 border-emerald-300 text-emerald-700'
                    : 'bg-slate-100 border-slate-200 text-slate-600'
                }`}
              >
                {bandwidthSettings.offPeakEnabled ? 'Active (8 PM – 8 AM)' : 'Disabled'}
              </button>
            </div>
            <p className="text-[10px] text-slate-500 leading-relaxed">
              When active, heavy video uploads pause during daytime class hours (8:00 AM – 8:00 PM) to ensure classroom WiFi remains fast, and automatically resume at night.
            </p>
          </div>
        </Card>
      )}

      {/* Auto-Start on Boot */}
      {isTauri() && (
        <Card className="space-y-3">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <div className="p-1.5 rounded-lg bg-violet-50 text-violet-600">
                <Power className="w-4 h-4" />
              </div>
              <div>
                <h3 className="text-xs font-bold text-slate-900">Auto-Start on Boot</h3>
                <p className="text-[10px] text-slate-500">Launch Centrix silently in system tray when Windows starts</p>
              </div>
            </div>
            <div className="flex items-center gap-2">
              <Pill tone={autoStartEnabled ? 'emerald' : 'slate'}>
                {autoStartEnabled ? 'Enabled' : 'Disabled'}
              </Pill>
              <button
                onClick={handleToggleAutoStart}
                disabled={autoStartLoading}
                className={`p-2.5 rounded-xl border active:scale-95 transition disabled:opacity-40 shrink-0 ${
                  autoStartEnabled
                    ? 'bg-slate-100 hover:bg-slate-200 border-slate-200 text-slate-700'
                    : 'bg-emerald-50 border-emerald-200 text-emerald-700'
                }`}
                title={autoStartEnabled ? 'Disable auto-start' : 'Enable auto-start'}
              >
                {autoStartLoading
                  ? <Loader2 className="w-4 h-4 animate-spin" />
                  : autoStartEnabled
                    ? <PauseCircle className="w-4 h-4" />
                    : <PlayCircle className="w-4 h-4" />
                }
              </button>
            </div>
          </div>
        </Card>
      )}

      {/* Support & Diagnostics */}
      {isTauri() && (
        <Card className="space-y-3">
          <div className="flex items-center justify-between border-b border-slate-100 pb-2.5">
            <div className="flex items-center gap-2">
              <div className="p-1.5 rounded-lg bg-amber-50 text-amber-600">
                <Download className="w-4 h-4" />
              </div>
              <div>
                <h3 className="text-xs font-bold text-slate-900">Support & Diagnostics</h3>
                <p className="text-[10px] text-slate-500">Collect logs, settings & system info into a ZIP for remote support</p>
              </div>
            </div>
          </div>

          <ActionButton
            tone="primary"
            onClick={handleExportDiagnostics}
            disabled={diagExporting}
            className="w-full"
          >
            {diagExporting ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Download className="w-3.5 h-3.5" />}
            {diagExporting ? 'Collecting diagnostics...' : 'Export Diagnostics ZIP'}
          </ActionButton>

          {diagResult && (
            <div className="p-2.5 rounded-xl bg-emerald-50 border border-emerald-200 space-y-1.5">
              <div className="flex items-center gap-2">
                <CheckCircle2 className="w-4 h-4 text-emerald-600 shrink-0" />
                <span className="text-[11px] font-semibold text-emerald-800">
                  Diagnostics saved ({(diagResult.size / 1024).toFixed(0)} KB)
                </span>
              </div>
              <div className="flex items-center gap-2">
                <span className="text-[10px] font-mono text-emerald-700 truncate flex-1" title={diagResult.path}>
                  {diagResult.path}
                </span>
                <button
                  onClick={() => navigator.clipboard.writeText(diagResult.path)}
                  className="flex items-center gap-1 text-[10px] font-semibold py-0.5 px-2 rounded-lg bg-white border border-emerald-200 hover:bg-emerald-50 text-emerald-700 active:scale-95 transition shrink-0"
                >
                  <Copy className="w-2.5 h-2.5" /> Copy
                </button>
              </div>
            </div>
          )}

          {diagError && (
            <div className="p-2.5 rounded-xl bg-rose-50 border border-rose-200 flex items-center gap-2">
              <AlertCircle className="w-4 h-4 text-rose-600 shrink-0" />
              <span className="text-[11px] font-semibold text-rose-800">{diagError}</span>
            </div>
          )}
        </Card>
      )}

      {/* 6. Sign out */}
      <Card className="space-y-3">
        <div className="flex items-center justify-between">
          <h4 className="font-bold text-slate-900 flex items-center gap-1.5 text-xs">
            <KeyRound className="w-3.5 h-3.5 text-slate-500" />
            Session / Connection
          </h4>
          <Pill tone="emerald">Connected</Pill>
        </div>
        <p className="text-[11px] text-slate-500 leading-relaxed">
          Logged in on this device. Sign out if you want to switch accounts or change the agent address.
        </p>
        <ActionButton tone="danger" onClick={onLogout} disabled={busy} className="w-full">
          <LogOut className="w-3.5 h-3.5" />
          Sign out
        </ActionButton>
      </Card>
    </div>
  </div>

  {/* Media Preview Modal (File & Lecture Video/PDF) */}
  <MediaPreviewModal
    open={Boolean(previewFile || previewLecture)}
    onClose={() => {
      setPreviewFile(null);
      setPreviewLecture(null);
    }}
    file={previewFile || undefined}
    lecture={previewLecture || undefined}
  />

</div>
  );
}
