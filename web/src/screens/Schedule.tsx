import { Calendar, CalendarX2, Plus, Undo2 } from 'lucide-react';
import type { AgentInfo, TimetableEntry, TimetableOverride } from '../types';

interface ScheduleScreenProps {
  timetable: TimetableEntry[];
  overrides: TimetableOverride[];
  rooms: string[];
  selectedRoom: string;
  agentInfo: AgentInfo | null;
  busy: boolean;
  onSelectRoom: (roomId: string) => void;
  onOpenAddSlot: () => void;
  onCancelSlot: (slot: TimetableEntry) => void;
  onUndoOverride: (overrideId: string) => void;
}

export function ScheduleScreen({
  timetable,
  overrides,
  rooms,
  selectedRoom,
  agentInfo,
  busy,
  onSelectRoom,
  onOpenAddSlot,
  onCancelSlot,
  onUndoOverride,
}: ScheduleScreenProps) {
  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between px-1">
        <div>
          <h2 className="text-sm font-bold text-white">Class Timetable</h2>
          <p className="text-[11px] text-slate-400">Today's schedule for the matching engine</p>
        </div>
      </div>

      {/* Room selector — rooms come from the agent config and today's lectures */}
      <div className="flex items-center gap-1.5 flex-wrap">
        {rooms.map((room) => (
          <button
            key={room}
            onClick={() => onSelectRoom(room)}
            className={`px-3 py-1.5 text-xs rounded-xl font-semibold border transition active:scale-95 ${
              selectedRoom === room
                ? 'bg-cyan-500 text-white border-cyan-400'
                : 'bg-slate-900 text-slate-400 border-slate-800 hover:text-white'
            }`}
          >
            Room {room}
          </button>
        ))}
      </div>

      <button
        onClick={onOpenAddSlot}
        className="w-full py-2.5 px-4 rounded-xl bg-cyan-600/20 hover:bg-cyan-600/30 border border-cyan-500/40 text-cyan-300 text-xs font-semibold flex items-center justify-center gap-2 active:scale-95 transition"
      >
        <Plus className="w-4 h-4" />
        Add Extra Slot / Emergency Lecture
      </button>

      {overrides.length > 0 && (
        <div className="space-y-1.5">
          <h3 className="text-[10px] font-bold uppercase tracking-wider text-amber-400 px-1 flex items-center gap-1.5">
            <CalendarX2 className="w-3.5 h-3.5" />
            Today's Overrides ({overrides.length})
          </h3>
          {overrides.map((override) => (
            <div key={override.overrideId} className="bg-amber-500/5 border border-amber-500/30 rounded-xl p-2.5 flex items-center justify-between gap-2">
              <div className="min-w-0">
                <p className="text-[11px] font-semibold text-amber-200">
                  {override.overrideType === 'Cancelled' ? 'Slot cancelled' : `Override: ${override.overrideType}`}
                  {override.originalSlotId && (
                    <span className="text-amber-400/70 font-mono"> · {override.originalSlotId}</span>
                  )}
                </p>
                {override.newBatchId && (
                  <p className="text-[10px] text-amber-300/80">
                    → {override.newBatchId} / {override.newSubjectId}
                  </p>
                )}
              </div>
              <button
                onClick={() => onUndoOverride(override.overrideId)}
                disabled={busy}
                className="flex items-center gap-1 text-[10px] font-bold px-2 py-1 rounded-lg bg-slate-800 text-slate-300 border border-slate-700 active:scale-95 transition disabled:opacity-40 shrink-0"
              >
                <Undo2 className="w-3 h-3" /> Undo
              </button>
            </div>
          ))}
        </div>
      )}

      {/* Slots timeline */}
      <div className="space-y-2.5">
        {timetable.length === 0 ? (
          <div className="bg-slate-900/40 border border-slate-800/60 rounded-xl p-6 text-center text-slate-500 text-xs">
            No slots for today in Room {selectedRoom}. Add one above
            {agentInfo?.sheetSyncIntervalMinutes ? (
              <> or wait for the Google Sheet sync (every {agentInfo.sheetSyncIntervalMinutes} min)</>
            ) : null}
            .
          </div>
        ) : (
          timetable.map((slot) => (
            <div key={slot.timetableEntryId} className="bg-slate-900/80 border border-slate-800 rounded-xl p-3 flex items-start justify-between gap-3">
              <div className="flex items-start gap-3 min-w-0">
                <div className="px-2 py-1.5 rounded-lg bg-slate-800 border border-slate-700 text-center shrink-0">
                  <span className="text-[10px] text-slate-400 block font-mono">{slot.slotStartTime.slice(0, 5)}</span>
                  <span className="text-[9px] text-slate-500 block">to</span>
                  <span className="text-[10px] text-cyan-400 block font-mono font-bold">{slot.slotEndTime.slice(0, 5)}</span>
                </div>
                <div className="min-w-0">
                  <h4 className="text-xs font-bold text-white truncate">{slot.batchId}</h4>
                  <p className="text-[11px] text-cyan-300 truncate">
                    {slot.subjectId} {slot.teacherId && `• ${slot.teacherId}`}
                  </p>
                  <span className="text-[10px] text-slate-500 font-mono">Slot ID: {slot.slotId}</span>
                </div>
              </div>

              <button
                onClick={() => onCancelSlot(slot)}
                disabled={busy}
                title="Cancel this slot for today"
                className="p-1.5 rounded-lg bg-red-500/10 border border-red-500/30 text-red-300 active:scale-95 transition disabled:opacity-40 shrink-0"
              >
                <CalendarX2 className="w-3.5 h-3.5" />
              </button>
            </div>
          ))
        )}
      </div>

      <p className="text-[10px] text-slate-500 px-1 flex items-center gap-1.5">
        <Calendar className="w-3 h-3 shrink-0" />
        Cancelling creates an override for today only — the sheet data stays untouched.
      </p>
    </div>
  );
}
