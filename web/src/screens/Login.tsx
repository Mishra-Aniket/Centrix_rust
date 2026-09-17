import { useEffect, useState } from 'react';
import { ArrowRight, ChevronDown, ChevronUp, KeyRound, Laptop, Loader2, LogIn, ShieldAlert } from 'lucide-react';
import { ApiError, fetchAuthConfig, pollGoogleLogin, startGoogleLogin, testConnection } from '../api';
import { getStoredAgentUrl, setLocalAccess, storeAgentUrl, storeApiKey, storeSessionToken } from '../config';

export function LoginScreen({ onConnected }: { onConnected: () => void }) {
  const [agentUrl, setAgentUrl] = useState(getStoredAgentUrl());
  const [apiKey, setApiKey] = useState('');
  const [busy, setBusy] = useState(false);
  const [googleBusy, setGoogleBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [googleEnabled, setGoogleEnabled] = useState(true);

  // Check agent auth config on mount and attempt seamless 1-click access
  useEffect(() => {
    fetchAuthConfig()
      .then((cfg) => setGoogleEnabled(cfg.googleLoginEnabled))
      .catch(() => setGoogleEnabled(true));

    const handleStorage = (e: StorageEvent) => {
      if (e.key === 'lasrs.sessionToken' && e.newValue) {
        onConnected();
      }
    };
    window.addEventListener('storage', handleStorage);

    if (sessionStorage.getItem('lasrs.loggedOut') !== 'true') {
      testConnection(agentUrl, '')
        .then(() => {
          storeAgentUrl(agentUrl);
          setLocalAccess();
          onConnected();
        })
        .catch(() => {});
    }

    return () => window.removeEventListener('storage', handleStorage);
  }, [agentUrl, onConnected]);

  // 1-Click Local access
  const handleLocalAccess = async () => {
    setError(null);
    setBusy(true);
    sessionStorage.removeItem('lasrs.loggedOut');
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
      
      const width = 500;
      const height = 650;
      const left = window.screenX + (window.outerWidth - width) / 2;
      const top = window.screenY + (window.outerHeight - height) / 2;
      const popup = window.open(
        consentUrl,
        'google_login',
        `width=${width},height=${height},left=${left},top=${top},status=no,menubar=no`
      );

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

  const [theme, setTheme] = useState<'light' | 'dark'>('light');

  const toggleTheme = () => {
    const next = theme === 'light' ? 'dark' : 'light';
    setTheme(next);
    document.documentElement.setAttribute('data-theme', next);
  };

  return (
    <div role="main" className="om-welcome min-h-screen w-full flex flex-col bg-[var(--cream)] text-[var(--ink)] antialiased select-none relative overflow-x-hidden">
      {/* Top 2px Progress Bar (exact officemobile signature) */}
      <div aria-hidden="true" className="w-full h-[2px] bg-[var(--rule)] relative z-20">
        <span className="block h-full bg-[var(--ink)] w-[18%] transition-all duration-500 ease-out" />
      </div>

      {/* Main Two-Column Stage */}
      <div className="flex-1 flex flex-col lg:flex-row items-stretch justify-start">
        {/* Left Column: Focused Login Column */}
        <section className="w-full lg:w-[440px] xl:w-[480px] shrink-0 min-h-[calc(100vh-2px)] flex flex-col justify-between border-b lg:border-b-0 lg:border-r border-[var(--rule)] bg-[var(--cream)] relative">
          
          {/* Top Hero Portion (52% height) */}
          <div className="p-8 sm:p-10 lg:p-12 border-b border-[var(--ink)] flex-1 flex flex-col justify-between">
            <div className="flex items-center justify-between">
              <div className="om-label text-[10px] tracking-[0.18em] text-[var(--stone)] flex items-center gap-2">
                <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse"></span>
                <span>CENTRIX · V2.4</span>
              </div>
              
              <button
                type="button"
                onClick={toggleTheme}
                className="om-meta text-[10px] uppercase tracking-wider text-[var(--stone)] hover:text-[var(--ink)] border border-[var(--rule)] px-2 py-0.5 transition cursor-pointer"
                title="Toggle Theme"
              >
                {theme === 'light' ? 'Dark' : 'Cream'}
              </button>
            </div>

            <div className="my-auto py-6">
              <h1 className="om-display text-4xl sm:text-5xl font-light text-[var(--ink)] leading-[1.08] tracking-tight">
                Your Lectures.<br />
                Your <em className="italic font-normal">Cloud.</em>
              </h1>

              <p className="om-meta text-xs text-[var(--stone)] mt-5 leading-relaxed">
                // auto-capture classroom lectures. sync to google drive &amp; youtube. done.
              </p>
            </div>

            <div>
              <div className="inline-flex items-center gap-2 px-3 py-1 bg-[var(--paper)] border border-[var(--rule)] text-[var(--stone)] font-mono text-[11px]">
                <span>PW Vidyapeeth</span>
                <span>·</span>
                <span className="text-[var(--ink)] font-medium">Room Operations</span>
              </div>
            </div>
          </div>

          {/* Bottom Sign-In Portion (48% height) */}
          <div className="p-8 sm:p-10 lg:p-12 space-y-4 bg-[var(--cream)]">
            <div className="flex items-center justify-between mb-1">
              <span className="om-label text-[10px] tracking-[0.16em] text-[var(--charcoal)] font-medium">
                SIGN IN
              </span>
              <span className="om-meta text-[10px] text-[var(--stone)]">PW Operator ID</span>
            </div>

            {error && (
              <div className="text-xs text-[var(--error)] bg-[var(--error)]/10 border border-[var(--error)]/30 p-3 font-mono flex items-start gap-2">
                <ShieldAlert className="w-4 h-4 shrink-0 mt-0.5 text-[var(--error)]" />
                <span>{error}</span>
              </div>
            )}

            {/* Primary Action 1: Continue with Google (Exact black ink block button from officemobile) */}
            {googleEnabled && (
              <button
                type="button"
                onClick={handleGoogleLogin}
                disabled={googleBusy || busy}
                aria-label="Continue with Google"
                className="w-full h-13 px-5 bg-[var(--ink)] hover:bg-[var(--charcoal)] text-[var(--on-ink)] font-mono text-xs uppercase tracking-wider font-medium flex items-center justify-between group active:scale-[0.99] disabled:opacity-50 cursor-pointer transition-colors"
              >
                <div className="flex items-center gap-3">
                  {googleBusy ? (
                    <Loader2 className="w-4 h-4 animate-spin text-[var(--on-ink)]" />
                  ) : (
                    <svg className="w-4 h-4 shrink-0" viewBox="0 0 24 24">
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
                  <span>{googleBusy ? 'Connecting…' : 'Continue with Google'}</span>
                </div>
                <ArrowRight className="w-4 h-4 text-[var(--on-ink)] transition-transform group-hover:translate-x-1" />
              </button>
            )}

            {/* Primary Action 2: 1-Click Local Dashboard (Outlined button) */}
            <button
              type="button"
              onClick={handleLocalAccess}
              disabled={busy || googleBusy}
              className="w-full h-11 px-5 border border-[var(--ink)] bg-transparent hover:bg-[var(--paper)] text-[var(--ink)] font-mono text-xs uppercase tracking-wider font-medium flex items-center justify-between group active:scale-[0.99] disabled:opacity-50 cursor-pointer transition-colors"
            >
              <div className="flex items-center gap-2.5">
                {busy ? <Loader2 className="w-3.5 h-3.5 animate-spin text-[var(--stone)]" /> : <Laptop className="w-3.5 h-3.5 text-[var(--stone)]" />}
                <span>Continue to Local Dashboard</span>
              </div>
              <ArrowRight className="w-3.5 h-3.5 text-[var(--ink)] transition-transform group-hover:translate-x-1" />
            </button>

            <p className="om-meta text-[11px] text-[var(--stone)] pt-1">
              we only access classroom folders you choose · nothing is stored on external servers
            </p>

            {/* Collapsible Advanced Remote / Key Login */}
            <div className="pt-2 border-t border-[var(--rule)]">
              <button
                type="button"
                onClick={() => setShowAdvanced(!showAdvanced)}
                className="w-full flex items-center justify-between py-1 text-[11px] font-mono text-[var(--stone)] hover:text-[var(--ink)] transition cursor-pointer"
              >
                <span className="flex items-center gap-1.5">
                  <KeyRound className="w-3 h-3 text-[var(--stone)]" />
                  Remote device / Manual API Key
                </span>
                {showAdvanced ? <ChevronUp className="w-3 h-3" /> : <ChevronDown className="w-3 h-3" />}
              </button>

              {showAdvanced && (
                <div className="mt-3 pt-3 border-t border-[var(--rule)] space-y-3 font-mono text-xs">
                  <div>
                    <label className="text-[10px] uppercase tracking-wider text-[var(--stone)] block mb-1">
                      Agent Address (e.g. http://192.168.1.38:5200)
                    </label>
                    <input
                      type="url"
                      value={agentUrl}
                      onChange={(e) => setAgentUrl(e.target.value)}
                      placeholder="http://192.168.1.38:5200"
                      className="w-full bg-[var(--paper)] border border-[var(--rule)] px-3 py-2 text-xs text-[var(--ink)] focus:outline-none focus:border-[var(--ink)]"
                    />
                  </div>

                  <div>
                    <label className="text-[10px] uppercase tracking-wider text-[var(--stone)] block mb-1">
                      API Key (Optional / if set on PC)
                    </label>
                    <input
                      type="password"
                      value={apiKey}
                      onChange={(e) => setApiKey(e.target.value)}
                      placeholder="Paste agent API key"
                      className="w-full bg-[var(--paper)] border border-[var(--rule)] px-3 py-2 text-xs text-[var(--ink)] focus:outline-none focus:border-[var(--ink)]"
                    />
                  </div>

                  <button
                    type="button"
                    onClick={handleManualConnect}
                    disabled={busy}
                    className="w-full py-2.5 bg-[var(--ink)] hover:bg-[var(--charcoal)] text-[var(--on-ink)] font-mono text-xs uppercase tracking-wider font-medium transition flex items-center justify-center gap-1.5 cursor-pointer"
                  >
                    <LogIn className="w-3 h-3" />
                    <span>Connect with Key</span>
                  </button>
                </div>
              )}
            </div>
          </div>

          {/* Stamp in bottom edge */}
          <div aria-hidden="true" className="hidden sm:block absolute bottom-2 right-3 font-mono text-[9px] uppercase tracking-[0.2em] text-[var(--stone)] opacity-70 pointer-events-none">
            v2.4 · oauth · no tracking
          </div>
        </section>

        {/* Right Column: Editorial Hero & Process Explanation */}
        <aside aria-label="About" className="hidden lg:flex flex-1 p-12 xl:p-16 flex-col justify-between bg-[var(--cream)]">
          <div className="max-w-2xl space-y-6">
            <div className="om-label text-[10px] tracking-[0.18em] text-[var(--stone)]">
              CENTRIX
            </div>

            <h2 className="om-display text-4xl xl:text-5xl font-light text-[var(--ink)] leading-[1.14]">
              A quiet <em className="italic font-normal">editorial</em> take on classroom lecture capture.
            </h2>

            <hr className="om-rule" />

            <p className="text-sm xl:text-base text-[var(--charcoal)] font-sans leading-relaxed">
              Watches OBS recording folders. Matches timetable schedules with video cuts.
              Uploads automatically to Google Drive batch folders and YouTube with zero manual overhead.
            </p>

            <ul className="space-y-4 pt-4 font-mono text-xs">
              <li className="flex items-center gap-4 text-[var(--charcoal)]">
                <span className="text-[var(--stone)] text-sm select-none">01</span>
                <span>Sign in with Google workspace account or 1-click local access.</span>
              </li>
              <li className="flex items-center gap-4 text-[var(--charcoal)]">
                <span className="text-[var(--stone)] text-sm select-none">02</span>
                <span>Centrix auto-detects classroom OBS recordings &amp; PDF notes.</span>
              </li>
              <li className="flex items-center gap-4 text-[var(--charcoal)]">
                <span className="text-[var(--stone)] text-sm select-none">03</span>
                <span>Lectures land automatically in Google Drive &amp; YouTube.</span>
              </li>
            </ul>
          </div>

          {/* Editorial Footer */}
          <div className="flex items-center gap-6 font-mono text-[11px] text-[var(--stone)] pt-12 border-t border-[var(--rule)]">
            <span>Centrix Operations</span>
            <span>·</span>
            <span>Centrix v2.4</span>
            <span>·</span>
            <span>MaxHub Automation</span>
            <span>·</span>
            <span>Support</span>
          </div>
        </aside>
      </div>
    </div>
  );
}
