import { useState } from 'react';
import { KeyRound, Loader2, Wifi } from 'lucide-react';
import { ApiError, testConnection } from '../api';
import { getStoredAgentUrl, storeAgentUrl, storeApiKey } from '../config';
import { Field, inputClass } from '../ui';

export function LoginScreen({ onConnected }: { onConnected: () => void }) {
  const [agentUrl, setAgentUrl] = useState(getStoredAgentUrl());
  const [apiKey, setApiKey] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleConnect = async () => {
    setError(null);
    setBusy(true);
    try {
      await testConnection(agentUrl, apiKey.trim());
      storeAgentUrl(agentUrl);
      storeApiKey(apiKey);
      onConnected();
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.status === 0 ? 'Agent not reachable. Check the URL and WiFi.' : err.message);
      } else {
        setError('Connection failed');
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-col min-h-screen bg-[#090d16] text-slate-100 font-sans select-none">
      <div className="flex-1 flex flex-col justify-center max-w-md w-full mx-auto p-6 space-y-6">
        <div className="flex flex-col items-center text-center space-y-3">
          <div className="w-16 h-16 rounded-2xl bg-black border border-white/10 flex items-center justify-center p-1.5 shadow-xl shadow-cyan-500/10">
            <img src="/logo.png" alt="PW Logo" className="w-full h-full object-contain rounded-xl" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-white tracking-tight">Centrix</h1>
            <p className="text-xs text-slate-400 mt-1">
              PW Vidyapeeth Lecture Operations & Sync
            </p>
          </div>
        </div>

        <div className="bg-slate-900/80 border border-slate-800 rounded-2xl p-5 space-y-4">
          <Field label="Agent URL (empty = this device's address)">
            <input
              type="url"
              value={agentUrl}
              onChange={(e) => setAgentUrl(e.target.value)}
              placeholder="http://192.168.1.20:5200"
              className={inputClass}
              autoCapitalize="off"
              autoCorrect="off"
            />
          </Field>

          <Field label="API key (shown in the agent terminal)">
            <input
              type="password"
              value={apiKey}
              onChange={(e) => setApiKey(e.target.value)}
              placeholder="Paste the agent API key"
              className={inputClass}
              autoCapitalize="off"
              autoCorrect="off"
            />
          </Field>

          {error && (
            <div className="text-[11px] text-red-300 bg-red-500/10 border border-red-500/30 rounded-xl px-3 py-2 flex items-center gap-2">
              <KeyRound className="w-3.5 h-3.5 shrink-0" />
              {error}
            </div>
          )}

          <button
            onClick={handleConnect}
            disabled={busy || !apiKey.trim()}
            className="w-full py-3 rounded-xl bg-gradient-to-r from-cyan-500 to-indigo-600 font-bold text-white text-sm shadow-lg shadow-cyan-500/20 active:scale-95 transition flex items-center justify-center gap-2 disabled:opacity-40"
          >
            {busy ? <Loader2 className="w-4 h-4 animate-spin" /> : <Wifi className="w-4 h-4" />}
            {busy ? 'Connecting...' : 'Connect'}
          </button>
        </div>

        <p className="text-[10px] text-slate-500 text-center leading-relaxed">
          Run <code className="text-cyan-400">./scripts/run-agent.sh</code> on the PC — it prints
          the URL for this device and a fresh API key. The key stays in this browser only.
        </p>
      </div>
    </div>
  );
}
