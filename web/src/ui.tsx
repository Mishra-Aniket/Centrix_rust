import { useState, useEffect, useRef, useCallback, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { X, GripHorizontal } from 'lucide-react';

export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return (
    <div className={`bg-[var(--paper)] border border-[var(--rule)] p-5 text-[var(--ink)] ${className}`}>
      {children}
    </div>
  );
}

export type PillTone = 'cyan' | 'emerald' | 'amber' | 'red' | 'slate';

const pillTones: Record<PillTone, string> = {
  cyan: 'bg-[var(--cream)] text-[var(--ink)] border-[var(--rule)]',
  emerald: 'bg-emerald-950/20 text-emerald-600 border-emerald-700/40',
  amber: 'bg-amber-950/20 text-amber-600 border-amber-700/40',
  red: 'bg-red-950/20 text-red-600 border-red-700/40',
  slate: 'bg-[var(--cream)] text-[var(--stone)] border-[var(--rule)]',
};

export function Pill({ children, tone = 'slate', className = '' }: { children: ReactNode; tone?: PillTone; className?: string }) {
  return (
    <span className={`px-2 py-0.5 border text-[10px] font-mono font-medium uppercase tracking-wider ${pillTones[tone]} ${className}`}>
      {children}
    </span>
  );
}

export function SectionHeader({ title, subtitle, right }: { title: string; subtitle?: string; right?: ReactNode }) {
  return (
    <div className="flex items-baseline justify-between px-1 pb-2 border-b border-[var(--rule)] mb-3">
      <div>
        <h2 className="font-serif text-lg font-normal tracking-tight text-[var(--ink)]">{title}</h2>
        {subtitle && <p className="text-[11px] font-mono text-[var(--stone)] mt-0.5">{subtitle}</p>}
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
    primary: 'bg-[var(--ink)] text-[var(--cream)] hover:opacity-90 active:opacity-100',
    danger: 'bg-red-950/20 hover:bg-red-950/30 border border-red-700/40 text-red-600',
    emerald: 'bg-emerald-950/20 hover:bg-emerald-950/30 border border-emerald-700/40 text-emerald-600',
    slate: 'bg-transparent hover:bg-[var(--cream)] border border-[var(--rule)] text-[var(--ink)]',
  };

  return (
    <button
      type={type}
      onClick={onClick}
      disabled={disabled}
      className={`py-2 px-3.5 font-mono text-xs uppercase tracking-wider flex items-center justify-center gap-1.5 transition disabled:opacity-40 cursor-pointer ${tones[tone]} ${className}`}
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
  const [visible, setVisible] = useState(false);
  const [dragOffset, setDragOffset] = useState<{ x: number; y: number }>({ x: 0, y: 0 });
  const [isDragging, setIsDragging] = useState(false);

  const sheetRef = useRef<HTMLDivElement>(null);
  const contentRef = useRef<HTMLDivElement>(null);
  const backdropRef = useRef<HTMLDivElement>(null);
  const closeTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const isDraggingDesktopRef = useRef(false);
  const dragStartRef = useRef<{ startX: number; startY: number; initX: number; initY: number }>({
    startX: 0,
    startY: 0,
    initX: 0,
    initY: 0,
  });

  // Mobile swipe-down dismiss refs
  const touchStartY = useRef(0);
  const touchDiffY = useRef(0);
  const isTouchDragging = useRef(false);

  useEffect(() => {
    if (open) {
      if (closeTimerRef.current) {
        clearTimeout(closeTimerRef.current);
        closeTimerRef.current = null;
      }
      setMounted(true);
      setDragOffset({ x: 0, y: 0 });
      touchDiffY.current = 0;

      // Allow DOM mount first, then animate in smoothly next frame
      const raf1 = requestAnimationFrame(() => {
        const raf2 = requestAnimationFrame(() => {
          setVisible(true);
        });
        return () => cancelAnimationFrame(raf2);
      });

      const originalOverflow = document.body.style.overflow;
      document.body.style.overflow = 'hidden';
      return () => {
        cancelAnimationFrame(raf1);
        document.body.style.overflow = originalOverflow;
      };
    } else {
      setVisible(false);
      const timer = setTimeout(() => {
        setMounted(false);
        setDragOffset({ x: 0, y: 0 });
      }, 190);
      return () => clearTimeout(timer);
    }
  }, [open]);

  // Smooth dismiss handler: fades & scales out in-place right where it was dragged
  const handleDismiss = useCallback(() => {
    if (!visible) return;
    setVisible(false);
    closeTimerRef.current = setTimeout(() => {
      onClose();
    }, 190);
  }, [visible, onClose]);

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

  // Desktop Header Dragging
  const onHeaderMouseDown = (e: React.MouseEvent) => {
    if ((e.target as HTMLElement).closest('button, a, input, select, textarea')) return;

    isDraggingDesktopRef.current = true;
    setIsDragging(true);
    dragStartRef.current = {
      startX: e.clientX,
      startY: e.clientY,
      initX: dragOffset.x,
      initY: dragOffset.y,
    };

    const onMouseMove = (moveEv: MouseEvent) => {
      if (!isDraggingDesktopRef.current) return;
      const dx = moveEv.clientX - dragStartRef.current.startX;
      const dy = moveEv.clientY - dragStartRef.current.startY;
      setDragOffset({
        x: dragStartRef.current.initX + dx,
        y: dragStartRef.current.initY + dy,
      });
    };

    const onMouseUp = () => {
      isDraggingDesktopRef.current = false;
      setIsDragging(false);
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
    };

    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);
  };

  // Touch Drag Handle (Mobile only)
  const onTouchStart = (e: React.TouchEvent) => {
    if ((e.target as HTMLElement).closest('input, textarea, select, button, a')) return;
    isTouchDragging.current = true;
    touchStartY.current = e.touches[0].clientY;
  };

  const onTouchMove = (e: React.TouchEvent) => {
    if (!isTouchDragging.current) return;
    const diff = e.touches[0].clientY - touchStartY.current;
    if (diff > 0) {
      touchDiffY.current = diff;
      if (sheetRef.current) {
        sheetRef.current.style.transform = `translate3d(0, ${diff}px, 0)`;
      }
    }
  };

  const onTouchEnd = () => {
    if (!isTouchDragging.current) return;
    isTouchDragging.current = false;
    if (touchDiffY.current > 90) {
      handleDismiss();
    } else {
      touchDiffY.current = 0;
      if (sheetRef.current) {
        sheetRef.current.style.transform = '';
      }
    }
  };

  if (!mounted && !open) return null;

  return createPortal(
    <div className="fixed inset-0 z-[99999] flex flex-col justify-end sm:justify-start sm:pt-[8vh] items-center p-0 sm:p-6 overflow-hidden">
      {/* SaaS Smooth Backdrop */}
      <div
        ref={backdropRef}
        onClick={handleDismiss}
        style={{
          transition: 'opacity 190ms cubic-bezier(0.16, 1, 0.3, 1)',
        }}
        className={`fixed inset-0 bg-slate-950/60 backdrop-blur-md cursor-pointer ${
          visible ? 'opacity-100' : 'opacity-0'
        }`}
        aria-hidden="true"
      />

      {/* Floating SaaS Modal Window (In-place Scale + Fade) */}
      <div
        ref={sheetRef}
        onTouchStart={onTouchStart}
        onTouchMove={onTouchMove}
        onTouchEnd={onTouchEnd}
        style={{
          transform: `translate3d(${dragOffset.x}px, ${dragOffset.y}px, 0) scale(${visible ? 1 : 0.96})`,
          opacity: visible ? 1 : 0,
          transition: isDragging
            ? 'none'
            : 'transform 190ms cubic-bezier(0.16, 1, 0.3, 1), opacity 190ms cubic-bezier(0.16, 1, 0.3, 1)',
        }}
        className={`relative z-10 w-full ${maxWidth} bg-[var(--paper)] border border-[var(--rule)] shadow-2xl flex flex-col max-h-[92vh] sm:max-h-[85vh] overflow-hidden will-change-transform`}
      >
        {/* Drag Handle Bar (Mobile only) */}
        <div
          data-drag-handle
          className="sm:hidden w-full pt-3 pb-1.5 flex flex-col items-center justify-center cursor-grab active:cursor-grabbing select-none touch-none"
        >
          <div className="w-10 h-0.5 bg-[var(--stone)]" />
        </div>

        {/* Header - Click and Drag handle on Desktop */}
        <div
          onMouseDown={onHeaderMouseDown}
          className="flex items-center justify-between px-5 sm:px-6 py-3.5 border-b border-[var(--rule)] select-none bg-[var(--cream)] cursor-grab active:cursor-grabbing"
          title="Click and drag to move modal anywhere"
        >
          <div className="flex items-center gap-2.5 min-w-0 pr-2 pointer-events-none">
            {icon ? (
              <div className="w-8 h-8 bg-[var(--paper)] border border-[var(--rule)] flex items-center justify-center text-[var(--ink)] shrink-0">
                {icon}
              </div>
            ) : (
              <div className="flex items-center text-[var(--stone)] opacity-60 shrink-0">
                <GripHorizontal className="w-4 h-4" />
              </div>
            )}
            <h3 className="font-serif text-lg text-[var(--ink)] tracking-tight truncate">
              {title}
            </h3>
          </div>
          <button
            type="button"
            onClick={handleDismiss}
            className="w-8 h-8 border border-[var(--rule)] hover:bg-[var(--paper)] text-[var(--stone)] hover:text-[var(--ink)] active:scale-95 flex items-center justify-center transition-all cursor-pointer shrink-0 z-10"
            aria-label="Close"
          >
            <X className="w-4 h-4 stroke-[1.8]" />
          </button>
        </div>

        {/* Modal Scrollable Content */}
        <div
          ref={contentRef}
          className="overflow-y-auto px-5 sm:px-6 py-5 space-y-4 pb-[max(1.25rem,env(safe-area-inset-bottom,20px))]"
        >
          {children}
        </div>
      </div>
    </div>,
    document.body
  );
}

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <label className="block text-[var(--stone)] mb-1 text-[11px] font-mono uppercase tracking-wider">{label}</label>
      {children}
    </div>
  );
}

export const inputClass =
  'w-full bg-[var(--cream)] hover:border-[var(--ink)] focus:border-[var(--ink)] border border-[var(--rule)] px-3.5 py-2 text-[var(--ink)] text-xs font-mono focus:outline-none transition rounded-none';


