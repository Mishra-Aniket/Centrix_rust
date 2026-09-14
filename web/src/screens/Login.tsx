import { useEffect, useState } from 'react';
import { ArrowRight, ChevronDown, ChevronUp, KeyRound, Laptop, Loader2, LogIn, ShieldAlert } from 'lucide-react';
import { ApiError, fetchAuthConfig, pollGoogleLogin, startGoogleLogin, testConnection } from '../api';
import { getStoredAgentUrl, setLocalAccess, storeAgentUrl, storeApiKey, storeSessionToken } from '../config';
import { Field, inputClass } from '../ui';

export function LoginScreen({ onConnected }: { onConnected: () => void }) {
  const [agentUrl, setAgentUrl] = useState(getStoredAgentUrl());
  const [apiKey, setApiKey] = useState('');
  const [busy, setBusy] = useState(false);
  const [googleBusy, setGoogleBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [googleEnabled, setGoogleEnabled] = useState(true);

  // Check agent auth config on mount
  useEffect(() => {
    fetchAuthConfig()
      .then((cfg) => setGoogleEnabled(cfg.googleLoginEnabled))
      .catch(() => setGoogleEnabled(true));

    // Listen for OAuth completion from popup / callback tab
    const handleStorage = (e: StorageEvent) => {
      if (e.key === 'lasrs.sessionToken' && e.newValue) {
        onConnected();
      }
    };
    window.addEventListener('storage', handleStorage);
    return () => window.removeEventListener('storage', handleStorage);
  }, [onConnected]);

  // 1-Click Local access
  const handleLocalAccess = async () => {
    setError(null);
    setBusy(true);
    try {
      await testConnection(agentUrl, '');
      storeAgentUrl(agentUrl);
      setLocalAccess();
      onConnected();
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        setError('Agent requires an API key or Google login. Please sign in below.');
        setShowAdvanced(true);
      } else {
        setError(err instanceof Error ? err.message : 'Connection failed');
      }
    } finally {
      setBusy(false);
    }
  };

  // Google OAuth flow
  const handleGoogleLogin = async () => {
    setError(null);
    setGoogleBusy(true);
    try {
      const { flowId, consentUrl } = await startGoogleLogin();
      
      // Open consent URL in popup or tab
      const width = 500;
      const height = 650;
      const left = window.screenX + (window.outerWidth - width) / 2;
      const top = window.screenY + (window.outerHeight - height) / 2;
      const popup = window.open(
        consentUrl,
        'google_login',
        `width=${width},height=${height},left=${left},top=${top},status=no,menubar=no`
      );

      // Poll until completed or closed
      const pollTimer = setInterval(async () => {
        try {
          const res = await pollGoogleLogin(flowId);
          if (res.status === 'ok' && res.sessionToken) {
            clearInterval(pollTimer);
            if (popup && !popup.closed) popup.close();
            storeSessionToken(res.sessionToken);
            setGoogleBusy(false);
            onConnected();
          }
        } catch {
          // ignore transient poll errors
        }
      }, 1200);

      // Timeout after 3 minutes
      setTimeout(() => {
        clearInterval(pollTimer);
        setGoogleBusy(false);
      }, 180000);
    } catch (err) {
      setGoogleBusy(false);
      setError(err instanceof Error ? err.message : 'Google login could not be started');
    }
  };

  // Manual API key connection (remote devices)
  const handleManualConnect = async () => {
    setError(null);
    setBusy(true);
    try {
      await testConnection(agentUrl, apiKey.trim());
      storeAgentUrl(agentUrl);
      storeApiKey(apiKey);
      onConnected();
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.status === 0 ? 'Agent not reachable. Check URL and network.' : err.message);
      } else {
        setError('Connection failed');
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-col min-h-screen bg-slate-50 text-slate-800 font-sans select-none">
      <div className="flex-1 flex flex-col justify-center max-w-md w-full mx-auto p-5 space-y-6">
        {/* Brand Header */}
        <div className="flex flex-col items-center text-center space-y-3">
          <div className="w-20 h-20 rounded-2xl bg-white border border-slate-200/90 shadow-md flex items-center justify-center p-2">
            <img src="/logo.png" alt="Physics Wallah" className="w-full h-full object-contain" />
          </div>
          <div>
            <div className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full bg-cyan-50 border border-cyan-200 text-cyan-700 text-[11px] font-semibold mb-1">
              Physics Wallah Vidyapeeth
            </div>
            <h1 className="text-2xl font-black text-slate-900 tracking-tight">Centrix</h1>
            <p className="text-xs text-slate-500 font-medium mt-0.5">
              Smart Lecture Sync & MaxHub Operations
            </p>
          </div>
        </div>

        {/* Action Card */}
        <div className="bg-white border border-slate-200/90 rounded-3xl p-6 shadow-xl shadow-slate-200/50 space-y-4">
          {error && (
            <div className="text-xs text-red-700 bg-red-50 border border-red-200 rounded-xl px-3.5 py-2.5 flex items-start gap-2.5">
              <ShieldAlert className="w-4 h-4 text-red-500 shrink-0 mt-0.5" />
              <span>{error}</span>
            </div>
          )}

          {/* Primary Action 1: Sign in with Google (PW ID) */}
          {googleEnabled && (
            <button
              onClick={handleGoogleLogin}
              disabled={googleBusy || busy}
              className="w-full py-3 px-4 rounded-2xl border border-slate-300 bg-white hover:bg-slate-50 text-slate-700 font-semibold text-xs shadow-xs active:scale-98 transition flex items-center justify-center gap-3 disabled:opacity-50"
            >
              {googleBusy ? (
                <Loader2 className="w-4 h-4 animate-spin text-cyan-600" />
              ) : (
                <svg className="w-4 h-4" viewBox="0 0 24 24">
                  <path
                    fill="#4285F4"
                    d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z"
                  />
                  <path
                    fill="#34A853"
                    d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z"
                  />
                  <path
                    fill="#FBBC05"
                    d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.06H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.94l2.85-2.22.81-.63z"
                  />
                  <path
                    fill="#EA4335"
                    d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.06l3.66 2.84c.87-2.6 3.3-4.52 6.16-4.52z"
                  />
                </svg>
              )}
              <span>{googleBusy ? 'Waiting for Google Sign-In...' : 'Sign in with Google (PW ID)'}</span>
            </button>
          )}

          {/* Primary Action 2: 1-Click Dashboard for Local PC */}
          <button
            onClick={handleLocalAccess}
            disabled={busy || googleBusy}
            className="w-full py-3 px-4 rounded-2xl bg-cyan-600 hover:bg-cyan-700 font-bold text-white text-xs shadow-md shadow-cyan-600/20 active:scale-98 transition flex items-center justify-center gap-2 disabled:opacity-50"
          >
            {busy ? <Loader2 className="w-4 h-4 animate-spin" /> : <Laptop className="w-4 h-4" />}
            <span>{busy ? 'Connecting to Centrix...' : 'Continue to Dashboard (Local PC)'}</span>
            <ArrowRight className="w-3.5 h-3.5 ml-0.5" />
          </button>

          {/* Advanced / Remote Settings */}
          <div className="pt-2 border-t border-slate-100">
            <button
              onClick={() => setShowAdvanced(!showAdvanced)}
              className="w-full flex items-center justify-between py-1.5 text-[11px] font-medium text-slate-500 hover:text-slate-700"
            >
              <span className="flex items-center gap-1.5">
                <KeyRound className="w-3.5 h-3.5 text-slate-400" />
                Connect from Phone / Remote Laptop
              </span>
              {showAdvanced ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}
            </button>

            {showAdvanced && (
              <div className="mt-3 pt-3 border-t border-slate-100 space-y-3">
                <Field label="Agent LAN URL (e.g. http://192.168.1.38:5200)">
                  <input
                    type="url"
                    value={agentUrl}
                    onChange={(e) => setAgentUrl(e.target.value)}
                    placeholder="http://192.168.1.38:5200"
                    className={inputClass}
                    autoCapitalize="off"
                    autoCorrect="off"
                  />
                </Field>

                <Field label="API Key (Optional / if set on PC)">
                  <input
                    type="password"
                    value={apiKey}
                    onChange={(e) => setApiKey(e.target.value)}
                    placeholder="Paste agent API key"
                    className={inputClass}
                    autoCapitalize="off"
                    autoCorrect="off"
                  />
                </Field>

                <button
                  onClick={handleManualConnect}
                  disabled={busy}
                  className="w-full py-2.5 rounded-xl bg-slate-800 hover:bg-slate-900 text-white font-semibold text-xs active:scale-98 transition flex items-center justify-center gap-1.5"
                >
                  <LogIn className="w-3.5 h-3.5" />
                  <span>Connect with Key</span>
                </button>
              </div>
            )}
          </div>
        </div>

        {/* Footer Note */}
        <div className="text-center space-y-1 text-[11px] text-slate-500">
          <p>Physics Wallah Central Operations Platform</p>
          <p className="text-[10px] text-slate-400">Centrix Agent &bull; Auto-Sync &bull; MaxHub &bull; Google Drive</p>
        </div>
      </div>
    </div>
  );
}
