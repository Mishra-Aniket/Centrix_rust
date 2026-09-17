import React, { useState } from 'react';
import {
  Folder,
  Monitor,
  Cloud,
  Play,
  Building2,
  HardDrive
} from 'lucide-react';
import { generateApiKey, saveSettings, installService, pickFolder, pickFile } from '../tauri';
import { storeApiKey, setLocalAccess } from '../config';

interface SetupWizardProps {
  onComplete: () => void;
}

export function SetupWizard({ onComplete }: SetupWizardProps) {
  const [step, setStep] = useState(0);

  // Form State
  const [monitorFolder, setMonitorFolder] = useState('');
  const [centerName, setCenterName] = useState('Pune - PCMC Vidyapeeth');
  const [roomId, setRoomId] = useState('');
  const [uploadToDrive, setUploadToDrive] = useState(true);
  const [driveFolder, setDriveFolder] = useState('');
  const [credentialsPath, setCredentialsPath] = useState('');
  
  // Summary State
  const [doInstallService, setDoInstallService] = useState(true);
  const [openDashboard, setOpenDashboard] = useState(true);
  
  const [isFinishing, setIsFinishing] = useState(false);

  const handleNext = () => setStep(s => Math.min(s + 1, 4));
  const handlePrev = () => setStep(s => Math.max(s - 1, 0));

  const handlePickMonitorFolder = async () => {
    const path = await pickFolder();
    if (path) setMonitorFolder(path);
  };

  const handlePickCredentials = async () => {
    const path = await pickFile([{ name: 'JSON', extensions: ['json'] }]);
    if (path) setCredentialsPath(path);
  };

  const handleFinish = async () => {
    setIsFinishing(true);
    try {
      const apiKey = await generateApiKey();
      const deviceId = `PC-ROOM-${roomId || 'UNKNOWN'}`;
      
      const settings = {
        Auth: { ApiKey: apiKey, Enabled: true },
        Agent: { CenterId: centerName, RoomId: roomId, DeviceId: deviceId },
        FileWatcher: { MonitorFolder: monitorFolder, EnableFileWatcher: true },
        GoogleDrive: { 
          Enabled: uploadToDrive, 
          CredentialsPath: credentialsPath, 
          RootFolderPath: driveFolder 
        },
        Kestrel: { Endpoints: { Http: { Url: "http://0.0.0.0:5200" } } },
        Database: { SqlitePath: "centrix.db" }
      };

      await saveSettings(settings);
      storeApiKey(apiKey);
      setLocalAccess();
      
      if (doInstallService) {
        try {
          await installService(5200);
        } catch (serviceErr) {
          console.warn('Background service skipped on this platform:', serviceErr);
        }
      }
      
      onComplete();
    } catch (e) {
      console.error(e);
      alert('Failed to complete setup: ' + String(e));
    } finally {
      setIsFinishing(false);
    }
  };

  const btnPrimary = "px-6 py-2.5 bg-[var(--ink)] hover:opacity-90 text-[var(--cream)] font-mono text-xs uppercase tracking-wider transition-all disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer flex items-center justify-center gap-2";
  const btnSecondary = "px-6 py-2.5 bg-transparent hover:bg-[var(--paper)] text-[var(--ink)] font-mono text-xs uppercase tracking-wider border border-[var(--rule)] transition-all disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer flex items-center justify-center gap-2";

  const steps = [
    // Step 0: Welcome
    <div key="welcome" className="flex flex-col items-center justify-center text-center space-y-6 animate-in fade-in duration-300">
      <div className="w-20 h-20 bg-[var(--paper)] border border-[var(--rule)] flex items-center justify-center p-3">
        <img src="/logo.png" alt="Centrix Logo" className="w-full h-full object-contain" />
      </div>
      
      <div className="space-y-2">
        <div className="font-mono text-[10px] text-[var(--stone)] uppercase tracking-wider">Installer & Daemon Setup</div>
        <h1 className="font-serif text-3xl font-normal text-[var(--ink)] tracking-tight">Centrix Setup</h1>
        <p className="text-[var(--stone)] text-xs font-mono max-w-sm mx-auto">Turn this PC into an automatic lecture capture and synchronization node.</p>
      </div>

      <div className="bg-[var(--paper)] p-6 border border-[var(--rule)] text-left w-full max-w-md space-y-3">
        <Feature icon={<Folder className="text-[var(--stone)]" />} text="Watches classroom recording folders automatically" />
        <Feature icon={<HardDrive className="text-[var(--stone)]" />} text="Offline-first resilience: recordings buffer safely locally" />
        <Feature icon={<Cloud className="text-[var(--stone)]" />} text="Auto-syncs lectures to Google Drive" />
        <Feature icon={<Monitor className="text-[var(--stone)]" />} text="Live local dashboard on PC/phone/tablet" />
        <Feature icon={<Play className="text-[var(--stone)]" />} text="Runs 24×7 as silent background service" />
      </div>

      <button 
        type="button"
        onClick={handleNext} 
        className="w-full max-w-md py-3.5 bg-[var(--ink)] hover:opacity-90 text-[var(--cream)] font-mono text-xs uppercase tracking-wider cursor-pointer flex items-center justify-center gap-2"
      >
        <span>Get Started</span>
        <span>→</span>
      </button>
    </div>,

    // Step 1: Monitor Folder
    <div key="folder" className="animate-in fade-in slide-in-from-right-8 duration-300 space-y-6 max-w-md mx-auto w-full">
      <div className="space-y-1">
        <h2 className="text-xl font-bold text-slate-900">Where do class recordings appear?</h2>
        <p className="text-slate-500 text-sm">Select the folder where OBS or your camera saves video files.</p>
      </div>

      <div className="space-y-2">
        <label className="text-sm font-semibold text-slate-700 block">Monitor Folder</label>
        <div className="flex gap-2">
          <input 
            type="text" 
            value={monitorFolder} 
            onChange={(e) => setMonitorFolder(e.target.value)}
            placeholder="e.g. /Users/aniketmishra/Movies or C:\Recordings" 
            className="flex-1 px-3 py-2 bg-slate-50 border border-slate-200 rounded-xl focus:ring-2 focus:ring-cyan-500/20 focus:border-cyan-500 outline-none transition font-mono text-xs text-slate-900"
          />
          <button 
            type="button"
            onClick={handlePickMonitorFolder}
            className="px-4 py-2 bg-white border border-slate-300 text-slate-700 rounded-xl hover:bg-slate-50 font-medium transition shadow-sm shrink-0 active:scale-95 cursor-pointer"
          >
            Browse...
          </button>
        </div>
        <p className="text-xs text-[#78718d] mt-2 bg-indigo-50/50 p-3 rounded-lg border border-indigo-100">
          Note: You can click <b>Browse...</b> to choose your folder or type/paste your directory path directly above. All subfolders are watched.
        </p>
      </div>

      <div className="flex justify-between pt-4">
        <button type="button" onClick={handlePrev} className={btnSecondary}>Back</button>
        <button type="button" onClick={handleNext} disabled={!monitorFolder.trim()} className={btnPrimary}>Next</button>
      </div>
    </div>,

    // Step 2: Room Identity
    <div key="identity" className="animate-in fade-in slide-in-from-right-8 duration-300 space-y-6 max-w-md mx-auto w-full">
      <div className="space-y-1">
        <h2 className="text-xl font-bold text-slate-900">Which room is this PC in?</h2>
        <p className="text-[#78718d] text-sm">This helps identify the lectures coming from this machine.</p>
      </div>

      <div className="space-y-4">
        <div className="space-y-1.5">
          <label className="text-sm font-semibold text-slate-700 block">Center Name</label>
          <div className="relative">
            <Building2 className="absolute left-3 top-2.5 w-5 h-5 text-slate-400" />
            <input 
              type="text" 
              value={centerName}
              onChange={(e) => setCenterName(e.target.value)}
              className="w-full pl-10 pr-3 py-2.5 bg-slate-50 border border-slate-200 rounded-xl focus:ring-2 focus:ring-cyan-500/20 focus:border-cyan-500 outline-none transition"
            />
          </div>
        </div>

        <div className="space-y-1.5">
          <label className="text-sm font-semibold text-slate-700 block">Room ID</label>
          <div className="relative">
            <Monitor className="absolute left-3 top-2.5 w-5 h-5 text-slate-400" />
            <input 
              type="text" 
              value={roomId}
              onChange={(e) => setRoomId(e.target.value)}
              placeholder="e.g. 603"
              className="w-full pl-10 pr-3 py-2.5 bg-slate-50 border border-slate-200 rounded-xl focus:ring-2 focus:ring-cyan-500/20 focus:border-cyan-500 outline-none transition font-mono uppercase"
            />
          </div>
        </div>

        <div className="bg-slate-900 p-4 rounded-xl text-center space-y-1 shadow-inner">
          <p className="text-slate-400 text-xs uppercase tracking-wider font-semibold">Device ID Preview</p>
          <p className="text-cyan-400 font-mono font-bold text-lg">PC-ROOM-{roomId || 'XXX'}</p>
        </div>
      </div>

      <div className="flex justify-between pt-4">
        <button type="button" onClick={handlePrev} className={btnSecondary}>Back</button>
        <button type="button" onClick={handleNext} disabled={!roomId.trim()} className={btnPrimary}>Next</button>
      </div>
    </div>,

    // Step 3: Google Drive
    <div key="drive" className="animate-in fade-in slide-in-from-right-8 duration-300 space-y-6 max-w-md mx-auto w-full">
      <div className="space-y-1">
        <h2 className="text-xl font-bold text-slate-900">Connect Google Drive</h2>
        <p className="text-slate-500 text-sm">Where should the recordings be uploaded?</p>
      </div>

      <div className="space-y-5">
        <label className="flex items-center gap-3 p-3 border border-slate-200 rounded-xl bg-white hover:bg-slate-50 cursor-pointer transition">
          <input 
            type="checkbox" 
            checked={uploadToDrive} 
            onChange={(e) => setUploadToDrive(e.target.checked)}
            className="w-5 h-5 text-cyan-600 rounded focus:ring-cyan-500" 
          />
          <span className="font-medium text-slate-800">Upload recordings to Google Drive</span>
        </label>

        {uploadToDrive && (
          <div className="space-y-4 animate-in slide-in-from-top-2 fade-in duration-200">
            <div className="space-y-1.5">
              <label className="text-sm font-semibold text-slate-700 block">Root Folder Name</label>
              <input 
                type="text" 
                value={driveFolder}
                onChange={(e) => setDriveFolder(e.target.value)}
                placeholder="Leave blank for Drive root (recommended if batch folders are at root)"
                className="w-full px-3 py-2 bg-slate-50 border border-slate-200 rounded-xl focus:ring-2 focus:ring-cyan-500/20 focus:border-cyan-500 outline-none transition text-slate-900"
              />
            </div>

            <div className="space-y-1.5">
              <label className="text-sm font-semibold text-slate-700 block">Credentials File (credentials.json)</label>
              <div className="flex gap-2">
                <input 
                  type="text" 
                  value={credentialsPath} 
                  readOnly
                  placeholder="Select file..." 
                  className="flex-1 px-3 py-2 bg-slate-100 border border-slate-200 rounded-xl outline-none text-slate-700 text-xs font-mono"
                />
                <button 
                  type="button"
                  onClick={handlePickCredentials}
                  className="px-4 py-2 bg-white border border-slate-300 text-slate-700 rounded-xl hover:bg-slate-50 font-medium transition shadow-sm shrink-0 active:scale-95 cursor-pointer"
                >
                  Browse...
                </button>
              </div>
              <p className="text-xs text-[#78718d] mt-1">Leave empty if already installed or authenticated.</p>
            </div>
            
            <button type="button" className="w-full py-2.5 bg-white border-2 border-slate-200 rounded-xl font-medium text-slate-700 flex items-center justify-center gap-2 hover:bg-slate-50 transition active:scale-[0.98] cursor-pointer">
              <Cloud className="w-4 h-4" />
              Sign in to Google now...
            </button>
          </div>
        )}
      </div>

      <div className="flex justify-between pt-4">
        <button type="button" onClick={handlePrev} className={btnSecondary}>Back</button>
        <button type="button" onClick={handleNext} className={btnPrimary}>Next</button>
      </div>
    </div>,

    // Step 4: Summary
    <div key="summary" className="animate-in fade-in slide-in-from-right-8 duration-300 space-y-6 max-w-md mx-auto w-full">
      <div className="space-y-1">
        <h2 className="text-xl font-bold text-slate-900">Ready to set up</h2>
        <p className="text-slate-500 text-sm">Review your settings before completing.</p>
      </div>

      <div className="bg-slate-900 p-4 rounded-xl shadow-inner overflow-hidden">
        <pre className="text-[10px] sm:text-xs text-slate-300 font-mono whitespace-pre-wrap leading-relaxed">
{JSON.stringify({
  Center: centerName,
  Room: roomId,
  MonitorFolder: monitorFolder,
  GoogleDrive: uploadToDrive ? 'Enabled' : 'Disabled',
  DriveFolder: driveFolder
}, null, 2)}
        </pre>
      </div>

      <div className="space-y-3">
        <label className="flex items-center gap-3 p-3 border border-slate-200 rounded-xl bg-white hover:bg-slate-50 cursor-pointer transition">
          <input 
            type="checkbox" 
            checked={doInstallService} 
            onChange={(e) => setDoInstallService(e.target.checked)}
            className="w-5 h-5 text-cyan-600 rounded focus:ring-cyan-500" 
          />
          <span className="font-medium text-slate-800">Install the background service</span>
        </label>
        
        <label className="flex items-center gap-3 p-3 border border-slate-200 rounded-xl bg-white hover:bg-slate-50 cursor-pointer transition">
          <input 
            type="checkbox" 
            checked={openDashboard} 
            onChange={(e) => setOpenDashboard(e.target.checked)}
            className="w-5 h-5 text-cyan-600 rounded focus:ring-cyan-500" 
          />
          <span className="font-medium text-slate-800">Open the dashboard when finished</span>
        </label>
      </div>

      <div className="flex justify-between pt-4">
        <button type="button" onClick={handlePrev} disabled={isFinishing} className={btnSecondary}>Back</button>
        <button 
          type="button"
          onClick={handleFinish} 
          disabled={isFinishing}
          className={`${btnPrimary} px-8`}
        >
          {isFinishing ? 'Setting up...' : 'Set up & start'}
        </button>
      </div>
    </div>
  ];

  return (
    <div className="min-h-screen bg-[var(--cream)] text-[var(--ink)] flex flex-col font-sans">
      {/* 2px progress bar */}
      <div className="h-[2px] w-full bg-[var(--rule)]">
        <div className="h-full bg-[var(--ink)] transition-all duration-300" style={{ width: `${((step + 1) / 5) * 100}%` }} />
      </div>
      
      {/* Header */}
      <header className="bg-[var(--paper)] border-b border-[var(--rule)] px-6 py-4 flex items-center justify-between z-10">
        <div className="flex items-center gap-3">
          <div className="w-8 h-8 border border-[var(--rule)] bg-[var(--cream)] flex items-center justify-center p-1">
            <img src="/logo.png" alt="Logo" className="w-full h-full object-contain" />
          </div>
          <span className="font-serif text-lg tracking-tight text-[var(--ink)]">Centrix Setup</span>
        </div>
        
        {step > 0 && (
          <div className="flex gap-2 items-center font-mono text-xs uppercase tracking-wider text-[var(--stone)]">
            <span>Step {step} / 4</span>
          </div>
        )}
      </header>

      {/* Main Content */}
      <main className="flex-1 flex items-center justify-center p-6 relative">
        <div className="w-full relative z-10">
          {steps[step]}
        </div>
      </main>
    </div>
  );
}

function Feature({ icon, text }: { icon: React.ReactNode, text: string }) {
  return (
    <div className="flex items-center gap-3 text-sm font-medium text-slate-700">
      <div className="w-6 h-6 flex items-center justify-center shrink-0">
        {icon}
      </div>
      <span>{text}</span>
    </div>
  );
}
