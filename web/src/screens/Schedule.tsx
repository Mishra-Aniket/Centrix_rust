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
    <div className="space-y-4">
      <div className="flex items-center justify-between px-1">
        <div>
          <h2 className="text-sm font-bold text-slate-900">Class Timetable</h2>
          <p className="text-[11px] text-slate-500">Today's schedule for matching engine</p>
        </div>
      </div>

      {/* Room selector */}
      <div className="flex items-center gap-1.5 flex-wrap">
        {rooms.map((room) => (
          <button
            key={room}
            onClick={() => onSelectRoom(room)}
            className={`px-3 py-1.5 text-xs rounded-xl font-semibold border transition active:scale-95 ${
              selectedRoom === room
                ? 'bg-cyan-600 text-white border-cyan-600 shadow-sm'
                : 'bg-white text-slate-700 border-slate-200 hover:bg-slate-50 shadow-xs'
            }`}
          >
            Room {room}
          </button>
        ))}
      </div>

      <button
        onClick={onOpenAddSlot}
        className="w-full py-2.5 px-4 rounded-xl bg-cyan-50 hover:bg-cyan-100 border border-cyan-200 text-cyan-700 text-xs font-semibold flex items-center justify-center gap-2 active:scale-95 transition shadow-xs"
      >
        <Plus className="w-4 h-4" />
        Add Extra Slot / Emergency Lecture
      </button>

      {overrides.length > 0 && (
        <div className="space-y-1.5">
          <h3 className="text-[10px] font-bold uppercase tracking-wider text-amber-700 px-1 flex items-center gap-1.5">
            <CalendarX2 className="w-3.5 h-3.5" />
            Today's Overrides ({overrides.length})
          </h3>
          {overrides.map((override) => (
            <div key={override.overrideId} className="bg-amber-50 border border-amber-200 rounded-xl p-3 flex items-center justify-between gap-2 shadow-xs">
              <div className="min-w-0">
                <p className="text-xs font-semibold text-amber-900">
                  {override.overrideType === 'Cancelled' ? 'Slot cancelled' : `Override: ${override.overrideType}`}
                  {override.originalSlotId && (
                    <span className="text-amber-700 font-mono"> · {override.originalSlotId}</span>
                  )}
                </p>
                {override.newBatchId && (
                  <p className="text-[11px] text-amber-800">
                    → {override.newBatchId} / {override.newSubjectId}
                  </p>
                )}
              </div>
              <button
                onClick={() => onUndoOverride(override.overrideId)}
                disabled={busy}
                className="flex items-center gap-1 text-[10px] font-bold px-2.5 py-1 rounded-lg bg-white text-slate-700 border border-slate-200 hover:bg-slate-50 active:scale-95 transition disabled:opacity-40 shrink-0 shadow-xs"
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
          <div className="bg-white border border-slate-200/90 rounded-2xl p-6 text-center text-slate-500 text-xs shadow-sm">
            No slots for today in Room {selectedRoom}. Add one above
            {agentInfo?.sheetSyncIntervalMinutes ? (
              <> or wait for Google Sheet sync (every {agentInfo.sheetSyncIntervalMinutes} min)</>
            ) : null}
            .
          </div>
        ) : (
          timetable.map((slot) => (
            <div key={slot.timetableEntryId} className="bg-white border border-slate-200/90 rounded-2xl p-3.5 flex items-start justify-between gap-3 shadow-sm">
              <div className="flex items-start gap-3 min-w-0">
                <div className="px-2.5 py-2 rounded-xl bg-slate-50 border border-slate-200 text-center shrink-0">
                  <span className="text-[10px] text-slate-500 block font-mono">{slot.slotStartTime.slice(0, 5)}</span>
                  <span className="text-[9px] text-slate-400 block font-medium">to</span>
                  <span className="text-[11px] text-cyan-700 block font-mono font-bold">{slot.slotEndTime.slice(0, 5)}</span>
                </div>
                <div className="min-w-0">
                  <h4 className="text-xs font-bold text-slate-900 truncate">{slot.batchId}</h4>
                  <p className="text-[11px] text-cyan-700 font-medium truncate">
                    {slot.subjectId} {slot.teacherId && `• ${slot.teacherId}`}
                  </p>
                  <span className="text-[10px] text-slate-400 font-mono">Slot ID: {slot.slotId}</span>
                </div>
              </div>

              <button
                onClick={() => onCancelSlot(slot)}
                disabled={busy}
                title="Cancel this slot for today"
                className="p-2 rounded-xl bg-red-50 border border-red-200 text-red-600 hover:bg-red-100 active:scale-95 transition disabled:opacity-40 shrink-0 shadow-xs"
              >
                <CalendarX2 className="w-3.5 h-3.5" />
              </button>
            </div>
          ))
        )}
      </div>

      <p className="text-[11px] text-slate-500 px-1 flex items-center gap-1.5">
        <Calendar className="w-3.5 h-3.5 shrink-0 text-slate-400" />
        Cancelling creates an override for today only — sheet data stays untouched.
      </p>
    </div>
  );
}
