import { useMemo, useState } from 'react';
import {
  Calendar,
  CalendarDays,
  CalendarX2,
  ChevronLeft,
  ChevronRight,
  Plus,
  RefreshCw,
  Undo2,
} from 'lucide-react';
import { RoomSelector } from '../components/RoomSelector';
import type { TimetableEntry, TimetableOverride, TimetableSummary } from '../types';

interface ScheduleScreenProps {
  timetable: TimetableEntry[];
  overrides: TimetableOverride[];
  rooms: string[];
  selectedRoom: string;
  selectedDate: string;
  availableDates: string[];
  timetableSummary?: TimetableSummary | null;
  busy: boolean;
  onSelectRoom: (roomId: string) => void;
  onSelectDate: (date: string) => void;
  onOpenAddSlot: () => void;
  onCancelSlot: (slot: TimetableEntry) => void;
  onUndoOverride: (overrideId: string) => void;
  onSyncNow?: () => void;
}

function formatDisplayDate(dateStr: string): string {
  try {
    const [year, month, day] = dateStr.split('-').map(Number);
    const date = new Date(year, month - 1, day);
    return date.toLocaleDateString('en-US', {
      weekday: 'short',
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  } catch {
    return dateStr;
  }
}

function formatShortDate(dateStr: string): string {
  try {
    const [year, month, day] = dateStr.split('-').map(Number);
    const date = new Date(year, month - 1, day);
    return date.toLocaleDateString('en-US', {
      month: 'short',
      day: 'numeric',
    });
  } catch {
    return dateStr;
  }
}

function formatTime12h(timeStr: string): string {
  try {
    const [h, m] = timeStr.slice(0, 5).split(':').map(Number);
    const ampm = h >= 12 ? 'PM' : 'AM';
    const hour12 = h % 12 || 12;
    return `${hour12}:${String(m).padStart(2, '0')} ${ampm}`;
  } catch {
    return timeStr.slice(0, 5);
  }
}

function getDurationMinutes(startStr: string, endStr: string): number {
  try {
    const [sh, sm] = startStr.slice(0, 5).split(':').map(Number);
    const [eh, em] = endStr.slice(0, 5).split(':').map(Number);
    const diff = eh * 60 + em - (sh * 60 + sm);
    return diff > 0 ? diff : 90;
  } catch {
    return 90;
  }
}

function getLocalDateString(offsetDays = 0): string {
  const d = new Date();
  if (offsetDays !== 0) {
    d.setDate(d.getDate() + offsetDays);
  }
  const year = d.getFullYear();
  const month = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function getSlotTimingState(
  selectedDate: string,
  startTime: string,
  endTime: string
): 'live' | 'upcoming' | 'completed' {
  const todayStr = getLocalDateString(0);
  if (selectedDate !== todayStr) {
    return selectedDate > todayStr ? 'upcoming' : 'completed';
  }
  const now = new Date();
  const currentMinutes = now.getHours() * 60 + now.getMinutes();
  const [sh, sm] = startTime.slice(0, 5).split(':').map(Number);
  const [eh, em] = endTime.slice(0, 5).split(':').map(Number);
  const startMinutes = sh * 60 + sm;
  const endMinutes = eh * 60 + em;
  if (currentMinutes >= startMinutes && currentMinutes <= endMinutes) return 'live';
  if (currentMinutes < startMinutes) return 'upcoming';
  return 'completed';
}

function getWeekDays(referenceDateStr: string) {
  try {
    const [y, m, d] = referenceDateStr.split('-').map(Number);
    const ref = new Date(y, m - 1, d);
    const dayOfWeek = ref.getDay();
    const diff = dayOfWeek === 0 ? -6 : 1 - dayOfWeek; // Monday start
    const monday = new Date(ref);
    monday.setDate(ref.getDate() + diff);

    const days = [];
    const dayNames = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
    for (let i = 0; i < 7; i++) {
      const cur = new Date(monday);
      cur.setDate(monday.getDate() + i);
      const year = cur.getFullYear();
      const month = String(cur.getMonth() + 1).padStart(2, '0');
      const dayNum = String(cur.getDate()).padStart(2, '0');
      const dateStr = `${year}-${month}-${dayNum}`;
      days.push({
        dateStr,
        dayName: dayNames[i],
        dayNum: cur.getDate(),
        isToday: dateStr === getLocalDateString(0),
      });
    }
    return days;
  } catch {
    return [];
  }
}

export function ScheduleScreen({
  timetable,
  overrides,
  rooms,
  selectedRoom,
  selectedDate,
  availableDates,
  timetableSummary,
  busy,
  onSelectRoom,
  onSelectDate,
  onOpenAddSlot,
  onCancelSlot,
  onUndoOverride,
  onSyncNow,
}: ScheduleScreenProps) {
  const [syncing, setSyncing] = useState(false);

  const todayStr = getLocalDateString(0);
  const isToday = selectedDate === todayStr;
  const weekDays = useMemo(() => getWeekDays(selectedDate), [selectedDate]);

  const navigateDay = (offset: number) => {
    try {
      const [y, m, d] = selectedDate.split('-').map(Number);
      const cur = new Date(y, m - 1, d);
      cur.setDate(cur.getDate() + offset);
      const newYear = cur.getFullYear();
      const newMonth = String(cur.getMonth() + 1).padStart(2, '0');
      const newDay = String(cur.getDate()).padStart(2, '0');
      onSelectDate(`${newYear}-${newMonth}-${newDay}`);
    } catch {
      onSelectDate(todayStr);
    }
  };

  const handleSyncClick = async () => {
    if (!onSyncNow || syncing) return;
    setSyncing(true);
    try {
      await onSyncNow();
    } finally {
      setTimeout(() => setSyncing(false), 800);
    }
  };

  // Filter available dates that are within this week (never old past dates like Aug 31)
  const currentWeekScheduledDates = useMemo(() => {
    const weekDateSet = new Set(weekDays.map((w) => w.dateStr));
    return availableDates.filter((d) => weekDateSet.has(d) && d !== selectedDate);
  }, [availableDates, weekDays, selectedDate]);

  // Next active day this week or upcoming
  const nextActiveDay = useMemo(() => {
    return (
      currentWeekScheduledDates.find((d) => d > selectedDate) ||
      availableDates.find((d) => d >= todayStr && d !== selectedDate) ||
      currentWeekScheduledDates[0]
    );
  }, [currentWeekScheduledDates, availableDates, selectedDate, todayStr]);

  return (
    <div className="space-y-3.5">
      {/* Header with Title and Sync Action */}
      <div className="flex items-center justify-between px-1">
        <div>
          <h2 className="text-sm font-bold text-slate-900">Class Timetable</h2>
          <p className="text-[11px] text-slate-500">
            {timetableSummary?.totalRooms
              ? `Full week · ${timetableSummary.totalRooms} rooms · ${timetableSummary.totalLectures} lectures`
              : `${timetable.length} lecture${timetable.length !== 1 ? 's' : ''} on ${formatDisplayDate(selectedDate)}`}
          </p>
        </div>
        {onSyncNow && (
          <button
            onClick={handleSyncClick}
            disabled={busy || syncing}
            title="Sync latest timetable from Google Sheet"
            className="flex items-center gap-1.5 px-2.5 py-1.5 text-[11px] font-semibold rounded-xl bg-white text-slate-700 border border-slate-200 hover:bg-slate-50 active:scale-95 transition disabled:opacity-40 shadow-xs"
          >
            <RefreshCw className={`w-3.5 h-3.5 text-cyan-600 ${syncing ? 'animate-spin' : ''}`} />
            <span>{syncing ? 'Syncing...' : 'Sync Sheet'}</span>
          </button>
        )}
      </div>

      {/* Date Navigator Bar with Native Date Picker */}
      <div className="bg-white border border-slate-200/90 rounded-2xl p-2.5 shadow-xs space-y-2">
        <div className="flex items-center justify-between gap-2">
          {/* Previous Day */}
          <button
            onClick={() => navigateDay(-1)}
            disabled={busy}
            title="Previous Day"
            className="p-2 rounded-xl bg-slate-50 hover:bg-slate-100 active:scale-95 border border-slate-200/80 text-slate-700 transition disabled:opacity-40 shrink-0"
          >
            <ChevronLeft className="w-4 h-4" />
          </button>

          {/* Date Display (Tap to open Native Calendar) */}
          <label className="relative flex-1 flex items-center justify-center gap-2 py-1 px-2 rounded-xl hover:bg-slate-50 cursor-pointer transition select-none">
            <CalendarDays className="w-4 h-4 text-cyan-600 shrink-0" />
            <div className="text-center min-w-0">
              <div className="flex items-center justify-center gap-1.5 flex-wrap">
                <span className="text-xs font-bold text-slate-900 truncate">
                  {formatDisplayDate(selectedDate)}
                </span>
                {isToday && (
                  <span className="px-1.5 py-0.5 text-[9px] font-extrabold uppercase rounded-md bg-cyan-100 text-cyan-700 tracking-wider">
                    Today
                  </span>
                )}
              </div>
              <span className="text-[10px] text-slate-400 font-medium block">
                Tap anywhere to pick date 📅
              </span>
            </div>

            {/* Hidden native date input that covers the container */}
            <input
              type="date"
              value={selectedDate}
              onChange={(e) => e.target.value && onSelectDate(e.target.value)}
              className="absolute inset-0 opacity-0 cursor-pointer w-full h-full"
            />
          </label>

          {/* Next Day */}
          <button
            onClick={() => navigateDay(1)}
            disabled={busy}
            title="Next Day"
            className="p-2 rounded-xl bg-slate-50 hover:bg-slate-100 active:scale-95 border border-slate-200/80 text-slate-700 transition disabled:opacity-40 shrink-0"
          >
            <ChevronRight className="w-4 h-4" />
          </button>
        </div>

        {/* 7-Day Week Strip */}
        <div className="grid grid-cols-7 gap-1 pt-1.5 border-t border-slate-100">
          {weekDays.map((day) => {
            const isSelected = day.dateStr === selectedDate;
            const dayCount = timetableSummary?.dayCounts[day.dateStr];
            const hasSlots = availableDates.includes(day.dateStr) || (dayCount !== undefined && dayCount > 0);

            return (
              <button
                key={day.dateStr}
                onClick={() => onSelectDate(day.dateStr)}
                className={`py-1.5 px-0.5 rounded-xl text-center transition active:scale-95 flex flex-col items-center justify-center relative ${
                  isSelected
                    ? 'bg-cyan-600 text-white shadow-sm'
                    : day.isToday
                    ? 'bg-cyan-50 text-cyan-800 border border-cyan-200'
                    : 'bg-slate-50 hover:bg-slate-100 text-slate-600'
                }`}
              >
                <span className="text-[10px] font-medium leading-none opacity-80">
                  {day.dayName}
                </span>
                <span className={`text-xs font-bold mt-1 leading-none ${isSelected ? 'text-white' : 'text-slate-800'}`}>
                  {day.dayNum}
                </span>

                {/* Count badge or dot indicator */}
                {dayCount !== undefined && dayCount > 0 ? (
                  <span
                    className={`text-[9px] font-mono px-1 rounded-full mt-0.5 ${
                      isSelected ? 'bg-cyan-700 text-white' : 'bg-cyan-100 text-cyan-800'
                    }`}
                  >
                    {dayCount}
                  </span>
                ) : hasSlots ? (
                  <span
                    className={`w-1 h-1 rounded-full mt-1 ${
                      isSelected ? 'bg-white' : 'bg-cyan-500'
                    }`}
                  />
                ) : null}
              </button>
            );
          })}
        </div>

        {/* Quick Date Presets & Active Days in this Week */}
        <div className="flex items-center gap-1.5 pt-1 overflow-x-auto no-scrollbar">
          <button
            onClick={() => onSelectDate(todayStr)}
            className={`px-2.5 py-1 text-[11px] font-semibold rounded-lg transition active:scale-95 shrink-0 ${
              isToday
                ? 'bg-cyan-600 text-white shadow-xs'
                : 'bg-cyan-50 hover:bg-cyan-100 text-cyan-700'
            }`}
          >
            Go to Today
          </button>

          {/* Jump pills to active days in this week only */}
          {currentWeekScheduledDates.map((dateStr) => {
            const count = timetableSummary?.dayCounts[dateStr];
            return (
              <button
                key={dateStr}
                onClick={() => onSelectDate(dateStr)}
                className="px-2.5 py-1 text-[11px] font-medium rounded-lg bg-slate-100 hover:bg-slate-200/80 text-slate-700 transition active:scale-95 shrink-0 flex items-center gap-1.5"
              >
                <Calendar className="w-3 h-3 text-slate-400" />
                <span>{formatShortDate(dateStr)}</span>
                {count !== undefined && count > 0 && (
                  <span className="text-[9px] px-1 rounded-full bg-slate-200 text-slate-700 font-mono">
                    {count}
                  </span>
                )}
              </button>
            );
          })}
        </div>
      </div>

      {/* Room Selector Dropdown */}
      <RoomSelector
        rooms={rooms}
        selectedRoom={selectedRoom}
        onSelectRoom={onSelectRoom}
        roomCounts={timetableSummary?.roomCounts}
        totalCount={timetableSummary?.totalLectures ?? timetable.length}
      />

      {/* Add Extra Slot Button */}
      <button
        onClick={onOpenAddSlot}
        className="w-full py-2.5 px-4 rounded-xl bg-cyan-50 hover:bg-cyan-100 border border-cyan-200 text-cyan-700 text-xs font-semibold flex items-center justify-center gap-2 active:scale-95 transition shadow-xs"
      >
        <Plus className="w-4 h-4" />
        Add Extra Slot for {formatShortDate(selectedDate)}
      </button>

      {/* Overrides for this Date */}
      {overrides.length > 0 && (
        <div className="space-y-1.5">
          <h3 className="text-[10px] font-bold uppercase tracking-wider text-amber-700 px-1 flex items-center gap-1.5">
            <CalendarX2 className="w-3.5 h-3.5" />
            Overrides on {formatShortDate(selectedDate)} ({overrides.length})
          </h3>
          {overrides.map((override) => (
            <div
              key={override.overrideId}
              className="bg-amber-50 border border-amber-200 rounded-xl p-3 flex items-center justify-between gap-2 shadow-xs"
            >
              <div className="min-w-0">
                <p className="text-xs font-semibold text-amber-900">
                  {override.overrideType === 'Cancelled'
                    ? 'Slot Cancelled'
                    : `Override: ${override.overrideType}`}
                  {override.originalSlotId && (
                    <span className="text-amber-700 font-mono text-[10px]">
                      {' '}
                      · {override.originalSlotId}
                    </span>
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

      {/* Slots Timeline List */}
      <div className="space-y-2.5">
        {timetable.length === 0 ? (
          <div className="bg-white border border-slate-200/90 rounded-2xl p-6 text-center shadow-sm space-y-3">
            <div className="w-10 h-10 rounded-full bg-slate-100 flex items-center justify-center mx-auto text-slate-400">
              <Calendar className="w-5 h-5" />
            </div>
            <div>
              <h4 className="text-xs font-bold text-slate-800">
                No classes scheduled for {formatDisplayDate(selectedDate)}
              </h4>
              <p className="text-[11px] text-slate-500 mt-1 max-w-xs mx-auto">
                Room {selectedRoom} has no timetable entries on this date.
              </p>
            </div>

            {/* Quick jump to active classes this week */}
            {nextActiveDay && (
              <div className="pt-1">
                <button
                  onClick={() => onSelectDate(nextActiveDay)}
                  className="inline-flex items-center gap-2 px-4 py-2 rounded-xl bg-cyan-600 hover:bg-cyan-700 text-white text-xs font-semibold shadow-xs transition active:scale-95"
                >
                  <span>View Next Class: {formatDisplayDate(nextActiveDay)}</span>
                  {timetableSummary?.dayCounts[nextActiveDay] && (
                    <span className="px-1.5 py-0.5 rounded-full bg-cyan-700 text-[10px]">
                      {timetableSummary.dayCounts[nextActiveDay]} slots
                    </span>
                  )}
                </button>
              </div>
            )}

            {currentWeekScheduledDates.length > 0 && (
              <div className="pt-2 border-t border-slate-100">
                <p className="text-[10px] font-semibold text-slate-500 uppercase tracking-wider mb-2">
                  Other scheduled days this week:
                </p>
                <div className="flex items-center justify-center gap-1.5 flex-wrap">
                  {currentWeekScheduledDates.map((d) => {
                    const count = timetableSummary?.dayCounts[d];
                    return (
                      <button
                        key={d}
                        onClick={() => onSelectDate(d)}
                        className="px-2.5 py-1 rounded-lg bg-cyan-50 hover:bg-cyan-100 border border-cyan-200 text-cyan-700 text-[11px] font-semibold transition active:scale-95 flex items-center gap-1.5"
                      >
                        <span>{formatDisplayDate(d)}</span>
                        {count !== undefined && count > 0 && (
                          <span className="text-[9px] px-1.5 py-0.2 rounded-full bg-cyan-200/70 text-cyan-800 font-mono">
                            {count}
                          </span>
                        )}
                      </button>
                    );
                  })}
                </div>
              </div>
            )}
          </div>
        ) : (
          timetable.map((slot) => {
            const timingState = getSlotTimingState(
              selectedDate,
              slot.slotStartTime,
              slot.slotEndTime
            );
            const duration = getDurationMinutes(slot.slotStartTime, slot.slotEndTime);

            return (
              <div
                key={slot.timetableEntryId}
                className="bg-white border border-slate-200/90 rounded-2xl p-3.5 flex items-start justify-between gap-3 shadow-sm hover:border-slate-300 transition"
              >
                <div className="flex items-start gap-3 min-w-0">
                  {/* Time box */}
                  <div className="px-2.5 py-2 rounded-xl bg-slate-50 border border-slate-200 text-center shrink-0 min-w-[70px]">
                    <span className="text-[11px] font-bold text-slate-800 block font-mono">
                      {formatTime12h(slot.slotStartTime)}
                    </span>
                    <span className="text-[9px] text-slate-400 block font-medium">to</span>
                    <span className="text-[11px] font-bold text-cyan-700 block font-mono">
                      {formatTime12h(slot.slotEndTime)}
                    </span>
                    <span className="text-[9px] text-slate-400 font-mono block mt-0.5">
                      {duration}m
                    </span>
                  </div>

                  {/* Slot Details */}
                  <div className="min-w-0 space-y-1">
                    <div className="flex items-center gap-1.5 flex-wrap">
                      <h4 className="text-xs font-bold text-slate-900 truncate">
                        {slot.batchId}
                      </h4>
                      {timingState === 'live' && (
                        <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[9px] font-extrabold bg-emerald-100 text-emerald-800 animate-pulse">
                          <span className="w-1.5 h-1.5 rounded-full bg-emerald-500"></span>
                          LIVE NOW
                        </span>
                      )}
                      {timingState === 'upcoming' && isToday && (
                        <span className="px-1.5 py-0.5 rounded text-[9px] font-medium bg-slate-100 text-slate-600">
                          Upcoming
                        </span>
                      )}
                    </div>

                    <p className="text-[11px] font-semibold text-cyan-700 truncate">
                      {slot.subjectId}
                      {slot.teacherId && (
                        <span className="text-slate-500 font-normal ml-1">
                          • {slot.teacherId}
                        </span>
                      )}
                    </p>

                    <div className="flex items-center gap-2 text-[10px] text-slate-400 font-mono">
                      <span>Room {slot.roomId}</span>
                      <span>•</span>
                      <span className="truncate">{slot.slotId}</span>
                    </div>
                  </div>
                </div>

                {/* Cancel action button */}
                <button
                  onClick={() => onCancelSlot(slot)}
                  disabled={busy}
                  title="Cancel this slot for this date"
                  className="p-2 rounded-xl bg-red-50 border border-red-200 text-red-600 hover:bg-red-100 active:scale-95 transition disabled:opacity-40 shrink-0 shadow-xs"
                >
                  <CalendarX2 className="w-3.5 h-3.5" />
                </button>
              </div>
            );
          })
        )}
      </div>

      {/* Info Footnote */}
      <p className="text-[11px] text-slate-500 px-1 flex items-center gap-1.5">
        <Calendar className="w-3.5 h-3.5 shrink-0 text-slate-400" />
        Cancelling creates an override for {formatShortDate(selectedDate)} only — master timetable stays untouched.
      </p>
    </div>
  );
}
