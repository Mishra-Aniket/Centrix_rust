import { useState, useEffect, useRef, useCallback, type ReactNode } from 'react';
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
  maxWidth = 'max-w-md',
}: {
  open: boolean;
  title: string;
  icon?: ReactNode;
  onClose: () => void;
  children: ReactNode;
  maxWidth?: string;
}) {
  const [mounted, setMounted] = useState(false);
  const [animating, setAnimating] = useState(false);
  const [closing, setClosing] = useState(false);
  const sheetRef = useRef<HTMLDivElement>(null);
  const contentRef = useRef<HTMLDivElement>(null);
  const backdropRef = useRef<HTMLDivElement>(null);

  const startYRef = useRef(0);
  const currentYRef = useRef(0);
  const startTimeRef = useRef(0);
  const isDraggingRef = useRef(false);

  useEffect(() => {
    if (open) {
      setMounted(true);
      setClosing(false);
      const timer = requestAnimationFrame(() => {
        setAnimating(true);
      });
      const originalOverflow = document.body.style.overflow;
      document.body.style.overflow = 'hidden';
      return () => {
        cancelAnimationFrame(timer);
        document.body.style.overflow = originalOverflow;
      };
    } else {
      setAnimating(false);
      setMounted(false);
      setClosing(false);
    }
  }, [open]);

  const handleDismiss = useCallback(() => {
    if (closing) return;
    setClosing(true);
    setAnimating(false);
    setTimeout(() => {
      onClose();
    }, 240);
  }, [closing, onClose]);

  useEffect(() => {
    if (!open) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        handleDismiss();
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [open, handleDismiss]);

  const onDragStart = (clientY: number, target: EventTarget | null) => {
    if (target instanceof Element) {
      const isInteractive = target.closest('input, textarea, select, button, a');
      if (isInteractive && !target.closest('[data-drag-handle]')) {
        return;
      }
      if (contentRef.current && contentRef.current.scrollTop > 0 && !target.closest('[data-drag-handle]')) {
        return;
      }
    }

    isDraggingRef.current = true;
    startYRef.current = clientY;
    currentYRef.current = clientY;
    startTimeRef.current = Date.now();

    if (sheetRef.current) {
      sheetRef.current.style.transition = 'none';
    }
    if (backdropRef.current) {
      backdropRef.current.style.transition = 'none';
    }
  };

  const onDragMove = (clientY: number) => {
    if (!isDraggingRef.current || !sheetRef.current) return;
    currentYRef.current = clientY;
    const diffY = clientY - startYRef.current;

    if (diffY < 0) {
      const damped = diffY * 0.15;
      sheetRef.current.style.transform = `translate3d(0, ${damped}px, 0)`;
    } else {
      sheetRef.current.style.transform = `translate3d(0, ${diffY}px, 0)`;
      if (backdropRef.current) {
        const opacity = Math.max(0.15, 1 - diffY / 350);
        backdropRef.current.style.opacity = `${opacity}`;
      }
    }
  };

  const onDragEnd = () => {
    if (!isDraggingRef.current || !sheetRef.current) return;
    isDraggingRef.current = false;

    const diffY = currentYRef.current - startYRef.current;
    const elapsed = Math.max(1, Date.now() - startTimeRef.current);
    const velocity = diffY / elapsed;

    if (diffY > 80 || (velocity > 0.45 && diffY > 20)) {
      sheetRef.current.style.transition = 'transform 220ms cubic-bezier(0.32, 0.72, 0, 1)';
      sheetRef.current.style.transform = 'translate3d(0, 100%, 0)';
      if (backdropRef.current) {
        backdropRef.current.style.transition = 'opacity 220ms ease';
        backdropRef.current.style.opacity = '0';
      }
      setTimeout(() => {
        handleDismiss();
      }, 220);
    } else {
      sheetRef.current.style.transition = 'transform 240ms cubic-bezier(0.2, 0.8, 0.2, 1)';
      sheetRef.current.style.transform = 'translate3d(0, 0, 0)';
      if (backdropRef.current) {
        backdropRef.current.style.transition = 'opacity 240ms ease';
        backdropRef.current.style.opacity = '1';
      }
    }
  };

  if (!mounted && !open) return null;

  return (
    <div className="fixed inset-0 z-50 flex flex-col justify-end items-center sm:p-4 overflow-hidden">
      {/* Backdrop */}
      <div
        ref={backdropRef}
        onClick={handleDismiss}
        className={`fixed inset-0 bg-black/50 backdrop-blur-xs transition-opacity duration-240 ease-out ${
          animating && !closing ? 'opacity-100' : 'opacity-0'
        }`}
        aria-hidden="true"
      />

      {/* Bottom Sheet Modal */}
      <div
        ref={sheetRef}
        onTouchStart={(e) => onDragStart(e.touches[0].clientY, e.target)}
        onTouchMove={(e) => onDragMove(e.touches[0].clientY)}
        onTouchEnd={onDragEnd}
        onTouchCancel={onDragEnd}
        onPointerDown={(e) => {
          const target = e.target as HTMLElement | null;
          if (target?.closest('[data-drag-handle]')) {
            onDragStart(e.clientY, e.target);
            const onPointerMove = (moveEvent: PointerEvent) => onDragMove(moveEvent.clientY);
            const onPointerUp = () => {
              window.removeEventListener('pointermove', onPointerMove);
              window.removeEventListener('pointerup', onPointerUp);
              onDragEnd();
            };
            window.addEventListener('pointermove', onPointerMove);
            window.addEventListener('pointerup', onPointerUp);
          }
        }}
        style={{
          transform: animating && !closing ? 'translate3d(0, 0, 0)' : 'translate3d(0, 100%, 0)',
          transition: isDraggingRef.current ? 'none' : 'transform 260ms cubic-bezier(0.32, 0.72, 0, 1)',
        }}
        className={`relative z-10 w-full ${maxWidth} bg-white border-t sm:border border-slate-200/90 rounded-t-3xl sm:rounded-3xl shadow-2xl flex flex-col max-h-[90vh] sm:max-h-[85vh] overflow-hidden will-change-transform`}
      >
        {/* Drag Handle Bar */}
        <div
          data-drag-handle
          className="w-full pt-3 pb-1 flex flex-col items-center justify-center cursor-grab active:cursor-grabbing select-none touch-none"
        >
          <div className="w-12 h-1.5 bg-slate-300 rounded-full hover:bg-slate-400 transition-colors" />
        </div>

        {/* Header */}
        <div
          data-drag-handle
          className="flex items-center justify-between px-5 py-2.5 border-b border-slate-100 select-none cursor-grab active:cursor-grabbing"
        >
          <h3 className="font-bold text-sm text-slate-900 flex items-center gap-2">
            {icon}
            {title}
          </h3>
          <button
            type="button"
            onClick={handleDismiss}
            className="p-1.5 -mr-1 rounded-full text-slate-400 hover:text-slate-600 hover:bg-slate-100 active:scale-95 transition"
            aria-label="Close"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Modal Scrollable Content */}
        <div
          ref={contentRef}
          className="overflow-y-auto px-5 py-4 space-y-4 pb-[max(1.25rem,env(safe-area-inset-bottom,20px))]"
        >
          {children}
        </div>
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
