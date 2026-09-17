import { Component, type ErrorInfo, type ReactNode } from 'react';
import { AlertTriangle, RefreshCw } from 'lucide-react';

interface Props {
  children: ReactNode;
  fallbackTitle?: string;
}

interface State {
  hasError: boolean;
  error: Error | null;
}

export class ErrorBoundary extends Component<Props, State> {
  public state: State = {
    hasError: false,
    error: null,
  };

  public static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error };
  }

  public componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error('Centrix Dashboard Uncaught Error:', error, errorInfo);
  }

  public render() {
    if (this.state.hasError) {
      return (
        <div className="min-h-screen bg-slate-950 text-white flex flex-col items-center justify-center p-6 text-center">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl p-6 max-w-md w-full shadow-2xl space-y-4">
            <div className="w-12 h-12 rounded-full bg-rose-500/20 border border-rose-500/30 flex items-center justify-center mx-auto text-rose-400">
              <AlertTriangle className="w-6 h-6" />
            </div>
            <div>
              <h2 className="text-sm font-bold text-slate-100">
                {this.props.fallbackTitle || 'A rendering glitch occurred'}
              </h2>
              <p className="text-xs text-slate-400 mt-1.5 font-mono bg-slate-950/60 p-2.5 rounded-lg border border-slate-800 text-left break-words">
                {this.state.error?.message || 'An unexpected error occurred in this view.'}
              </p>
            </div>
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => this.setState({ hasError: false, error: null })}
                className="flex-1 py-2 px-3 bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-semibold rounded-xl border border-slate-700 transition"
              >
                Dismiss
              </button>
              <button
                type="button"
                onClick={() => window.location.reload()}
                className="flex-1 py-2 px-3 bg-cyan-600 hover:bg-cyan-500 text-white text-xs font-bold rounded-xl flex items-center justify-center gap-1.5 transition active:scale-95"
              >
                <RefreshCw className="w-3.5 h-3.5" /> Reload
              </button>
            </div>
          </div>
        </div>
      );
    }
    return this.props.children;
  }
}
