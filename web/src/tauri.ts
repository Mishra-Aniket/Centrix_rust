import { invoke } from '@tauri-apps/api/core';
import { open } from '@tauri-apps/plugin-dialog';
import {
  isPermissionGranted,
  requestPermission,
  sendNotification,
} from '@tauri-apps/plugin-notification';
import {
  enable as enableAutostart,
  disable as disableAutostart,
  isEnabled as isAutostartEnabled,
} from '@tauri-apps/plugin-autostart';

// Detect if running inside Tauri
export function isTauri(): boolean {
  return '__TAURI_INTERNALS__' in window;
}

export interface ServiceStatus {
  state: 'Running' | 'Stopped' | 'Starting' | 'Stopping' | 'NotInstalled';
}

export interface AppPaths {
  sharedRoot: string;
  settingsFile: string;
  dataDirectory: string;
  agentExecutable: string;
}

export interface SystemHealth {
  cpuUsagePercent: number;
  ramTotalBytes: number;
  ramUsedBytes: number;
  ramAvailableBytes: number;
  diskTotalBytes: number;
  diskFreeBytes: number;
  diskUsagePercent: number;
  diskMountPoint: string;
  uptimeSeconds: number;
}

export interface DiagnosticsResult {
  savedPath: string;
  fileCount: number;
  sizeBytes: number;
}

export interface ThumbnailResult {
  success: boolean;
  dataUrl?: string | null;
  cachedPath?: string | null;
  width: number;
  height: number;
  source: string;
}

export interface FileValidationReport {
  filePath: string;
  fileSizeBytes: number;
  fileFormat: string;
  isValid: boolean;
  isLocked: boolean;
  status: 'Healthy' | 'Corrupted' | 'RecordingInProgress' | 'ZeroByte' | 'MissingMoov' | 'Truncated' | 'NotFound' | 'Locked';
  errorMessage?: string | null;
  recommendation: string;
}

export interface BandwidthSettings {
  maxUploadSpeedMbps: number;
  offPeakEnabled: boolean;
  offPeakStart: string;
  offPeakEnd: string;
  pauseDuringClassHours: boolean;
  currentStatus?: string | null;
}

// ── Service Management ──

export async function getServiceStatus(): Promise<ServiceStatus> {
  if (!isTauri()) return { state: 'NotInstalled' };
  try {
    return await invoke('get_service_status');
  } catch {
    return { state: 'NotInstalled' };
  }
}

export async function startService(): Promise<void> {
  if (!isTauri()) return;
  return invoke('start_service');
}

export async function stopService(): Promise<void> {
  if (!isTauri()) return;
  return invoke('stop_service');
}

export async function restartService(): Promise<void> {
  if (!isTauri()) return;
  return invoke('restart_service');
}

export async function installService(dashboardPort: number): Promise<void> {
  if (!isTauri()) return;
  return invoke('install_service', { dashboardPort });
}

// ── Settings ──

export async function readSettings(): Promise<Record<string, unknown>> {
  if (!isTauri()) return {};
  try {
    return await invoke('read_settings');
  } catch {
    return {};
  }
}

export async function saveSettings(settings: Record<string, unknown>): Promise<void> {
  if (!isTauri()) return;
  return invoke('save_settings', { settings });
}

export async function generateApiKey(): Promise<string> {
  if (!isTauri()) return 'browser-dev-key';
  return invoke('generate_api_key');
}

export async function getAppPaths(): Promise<AppPaths> {
  if (!isTauri()) {
    return {
      sharedRoot: '',
      settingsFile: '',
      dataDirectory: '',
      agentExecutable: ''
    };
  }
  return invoke('get_app_paths');
}

// ── System Health Watchdog ──

export async function getSystemHealth(monitorFolder?: string): Promise<SystemHealth | null> {
  if (!isTauri()) return null;
  try {
    return await invoke('get_system_health', { monitorFolder: monitorFolder ?? null });
  } catch {
    return null;
  }
}

// ── Diagnostics Export ──

export async function exportDiagnostics(): Promise<DiagnosticsResult | null> {
  if (!isTauri()) return null;
  try {
    return await invoke('export_diagnostics');
  } catch (err) {
    throw new Error(err instanceof Error ? err.message : String(err));
  }
}

// ── Native Notifications ──

export async function notify(title: string, body: string): Promise<void> {
  if (!isTauri()) return;
  try {
    let granted = await isPermissionGranted();
    if (!granted) {
      const permission = await requestPermission();
      granted = permission === 'granted';
    }
    if (granted) {
      sendNotification({ title, body });
    }
  } catch {
    // Notifications not supported or permission denied — silently ignore
  }
}

// ── Auto-Start on Boot ──

export async function isAutoStartEnabled(): Promise<boolean> {
  if (!isTauri()) return false;
  try {
    return await isAutostartEnabled();
  } catch {
    return false;
  }
}

export async function enableAutoStart(): Promise<void> {
  if (!isTauri()) return;
  return enableAutostart();
}

export async function disableAutoStart(): Promise<void> {
  if (!isTauri()) return;
  return disableAutostart();
}

// ── Native File/Folder Pickers ──

export async function pickFolder(defaultPath?: string): Promise<string | null> {
  if (!isTauri()) return null;
  try {
    const selected = await invoke<string | null>('pick_folder_native', { defaultPath });
    if (selected) return selected;
  } catch (err) {
    console.warn('Native picker fallback to dialog plugin:', err);
  }
  try {
    const selected = await open({
      directory: true,
      multiple: false,
      defaultPath
    });
    return selected ? (selected as string) : null;
  } catch (err) {
    console.error('Failed to open folder picker:', err);
    return null;
  }
}

export async function pickFile(filters?: { name: string; extensions: string[] }[]): Promise<string | null> {
  if (!isTauri()) return null;
  try {
    const filter = filters?.[0];
    const selected = await invoke<string | null>('pick_file_native', {
      extensionName: filter?.name,
      extensions: filter?.extensions
    });
    if (selected) return selected;
  } catch (err) {
    console.warn('Native picker fallback to dialog plugin:', err);
  }
  try {
    const selected = await open({
      directory: false,
      multiple: false,
      filters
    });
    return selected ? (selected as string) : null;
  } catch (err) {
    console.error('Failed to open file picker:', err);
    return null;
  }
}

// ── Video Thumbnail Extraction ──

export async function getVideoThumbnail(filePath: string): Promise<ThumbnailResult> {
  if (!isTauri()) {
    return {
      success: false,
      width: 320,
      height: 180,
      source: 'web_fallback'
    };
  }
  try {
    return await invoke<ThumbnailResult>('get_video_thumbnail', { filePath });
  } catch {
    return {
      success: false,
      width: 320,
      height: 180,
      source: 'error_fallback'
    };
  }
}

// ── Video & File Integrity Validator ──

export async function validateRecordingFile(filePath: string): Promise<FileValidationReport> {
  if (!isTauri()) {
    return {
      filePath,
      fileSizeBytes: 0,
      fileFormat: 'Unknown',
      isValid: true,
      isLocked: false,
      status: 'Healthy',
      recommendation: 'Running in web mode; skipping native file system checks'
    };
  }
  try {
    return await invoke<FileValidationReport>('validate_recording_file', { filePath });
  } catch (err) {
    return {
      filePath,
      fileSizeBytes: 0,
      fileFormat: 'Unknown',
      isValid: false,
      isLocked: false,
      status: 'Corrupted',
      errorMessage: err instanceof Error ? err.message : String(err),
      recommendation: 'Unable to validate file'
    };
  }
}

// ── Bandwidth & Schedule Management ──

export async function getBandwidthSettings(): Promise<BandwidthSettings> {
  if (!isTauri()) {
    return {
      maxUploadSpeedMbps: 0,
      offPeakEnabled: false,
      offPeakStart: '20:00',
      offPeakEnd: '08:00',
      pauseDuringClassHours: false,
      currentStatus: 'Unrestricted'
    };
  }
  try {
    return await invoke<BandwidthSettings>('get_bandwidth_settings');
  } catch {
    return {
      maxUploadSpeedMbps: 0,
      offPeakEnabled: false,
      offPeakStart: '20:00',
      offPeakEnd: '08:00',
      pauseDuringClassHours: false,
      currentStatus: 'Unrestricted'
    };
  }
}

export async function saveBandwidthSettings(settings: BandwidthSettings): Promise<void> {
  if (!isTauri()) return;
  return invoke('save_bandwidth_settings', { settings });
}

