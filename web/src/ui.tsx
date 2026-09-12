import type { ReactNode } from 'react';
import { X } from 'lucide-react';

export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return (
    <div className={`bg-slate-900/80 border border-slate-800 rounded-2xl p-4 shadow-lg ${className}`}>
      {children}
    </div>
  );
}

export type PillTone = 'cyan' | 'emerald' | 'amber' | 'red' | 'slate';

const pillTones: Record<PillTone, string> = {
  cyan: 'bg-cyan-500/10 text-cyan-400 border-cyan-500/30',
  emerald: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/30',
  amber: 'bg-amber-500/10 text-amber-400 border-amber-500/30',
  red: 'bg-red-500/10 text-red-400 border-red-500/30',
  slate: 'bg-slate-800 text-slate-300 border-slate-700',
};

export function Pill({ children, tone = 'slate', className = '' }: { children: ReactNode; tone?: PillTone; className?: string }) {
  return (
    <span className={`px-2 py-0.5 rounded-full border text-[10px] font-bold uppercase tracking-wide ${pillTones[tone]} ${className}`}>
      {children}
    </span>
  );
}

export function SectionHeader({ title, subtitle, right }: { title: string; subtitle?: string; right?: ReactNode }) {
  return (
    <div className="flex items-center justify-between px-1">
      <div>
        <h2 className="text-sm font-bold text-white">{title}</h2>
        {subtitle && <p className="text-[11px] text-slate-400">{subtitle}</p>}
      </div>
      {right}
    </div>
  );
}

export function ActionButton({
  children,
  onClick,
  tone = 'slate',
  disabled = false,
  className = '',
  type = 'button',
}: {
  children: ReactNode;
  onClick?: () => void;
  tone?: 'primary' | 'danger' | 'emerald' | 'slate';
  disabled?: boolean;
  className?: string;
  type?: 'button' | 'submit';
}) {
  const tones: Record<string, string> = {
    primary: 'bg-cyan-500/20 hover:bg-cyan-500/30 border border-cyan-500/40 text-cyan-300',
    danger: 'bg-red-500/10 hover:bg-red-500/20 border border-red-500/40 text-red-300',
    emerald: 'bg-emerald-600 hover:bg-emerald-500 border border-emerald-500 text-white shadow-md shadow-emerald-900/30',
    slate: 'bg-slate-800 hover:bg-slate-700 border border-slate-700 text-slate-200',
  };

  return (
    <button
      type={type}
      onClick={onClick}
      disabled={disabled}
      className={`py-2 px-3 rounded-xl font-semibold text-xs flex items-center justify-center gap-1.5 active:scale-95 transition disabled:opacity-40 disabled:active:scale-100 ${tones[tone]} ${className}`}
    >
      {children}
    </button>
  );
}

export function Modal({
  open,
  title,
  icon,
  onClose,
  children,
}: {
  open: boolean;
  title: string;
  icon?: ReactNode;
  onClose: () => void;
  children: ReactNode;
}) {
  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 bg-black/70 backdrop-blur-sm flex items-end sm:items-center justify-center p-4">
      <div className="bg-slate-900 border border-slate-800 rounded-3xl w-full max-w-sm p-5 space-y-4 shadow-2xl">
        <div className="flex items-center justify-between border-b border-slate-800 pb-2">
          <h3 className="font-bold text-sm text-white flex items-center gap-1.5">
            {icon}
            {title}
          </h3>
          <button onClick={onClose} className="p-1 text-slate-400 hover:text-white" aria-label="Close">
            <X className="w-5 h-5" />
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <label className="block text-slate-400 mb-1 text-xs">{label}</label>
      {children}
    </div>
  );
}

export const inputClass =
  'w-full bg-slate-800 border border-slate-700 rounded-xl px-3 py-2 text-white text-xs font-medium focus:outline-none focus:border-cyan-500';
