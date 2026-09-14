import type { ReactNode } from 'react';
import { X } from 'lucide-react';

export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return (
    <div className={`bg-white border border-slate-200/90 rounded-2xl p-4 shadow-sm ${className}`}>
      {children}
    </div>
  );
}

export type PillTone = 'cyan' | 'emerald' | 'amber' | 'red' | 'slate';

const pillTones: Record<PillTone, string> = {
  cyan: 'bg-cyan-50 text-cyan-700 border-cyan-200',
  emerald: 'bg-emerald-50 text-emerald-700 border-emerald-200',
  amber: 'bg-amber-50 text-amber-700 border-amber-200',
  red: 'bg-red-50 text-red-700 border-red-200',
  slate: 'bg-slate-100 text-slate-700 border-slate-200',
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
        <h2 className="text-sm font-bold text-slate-900">{title}</h2>
        {subtitle && <p className="text-[11px] text-slate-500">{subtitle}</p>}
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
    primary: 'bg-cyan-600 hover:bg-cyan-700 text-white shadow-sm',
    danger: 'bg-red-50 hover:bg-red-100 border border-red-200 text-red-700',
    emerald: 'bg-emerald-600 hover:bg-emerald-700 border border-emerald-500 text-white shadow-sm',
    slate: 'bg-slate-100 hover:bg-slate-200 border border-slate-200 text-slate-700',
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
    <div className="fixed inset-0 z-50 bg-black/40 backdrop-blur-sm flex items-end sm:items-center justify-center p-4">
      <div className="bg-white border border-slate-200 rounded-3xl w-full max-w-sm p-5 space-y-4 shadow-2xl">
        <div className="flex items-center justify-between border-b border-slate-200 pb-2">
          <h3 className="font-bold text-sm text-slate-900 flex items-center gap-1.5">
            {icon}
            {title}
          </h3>
          <button onClick={onClose} className="p-1 text-slate-400 hover:text-slate-600" aria-label="Close">
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
      <label className="block text-slate-600 mb-1 text-xs font-medium">{label}</label>
      {children}
    </div>
  );
}

export const inputClass =
  'w-full bg-white border border-slate-300 rounded-xl px-3 py-2 text-slate-900 text-xs font-medium focus:outline-none focus:border-cyan-500 focus:ring-1 focus:ring-cyan-500';
