import { useMemo, useState } from 'react';
import {
  Calendar,
  CalendarDays,
  CalendarX2,
  ChevronLeft,
  ChevronRight,
  Clock,
  Plus,
  Radio,
  RefreshCw,
  Undo2,
  User,
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
      weekday: 'long',
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

  // Filter available dates that are within this week
  const currentWeekScheduledDates = useMemo(() => {
    const weekDateSet = new Set(weekDays.map((w) => w.dateStr));
    return availableDates.filter((d) => weekDateSet.has(d) && d !== selectedDate);
  }, [availableDates, weekDays, selectedDate]);

  // Next active day this week or upcoming
  const nextActiveDay = useMemo(() => {
    return (
      currentWeekScheduledDates.find((d) => d > selectedDate) ||
      availableDates.find((d) => d >= todayStr && d !== selectedDate) ||
      availableDates.find((d) => d !== selectedDate) ||
      currentWeekScheduledDates[0]
    );
  }, [currentWeekScheduledDates, availableDates, selectedDate, todayStr]);

  return (
    <div className="space-y-6">
      {/* Editorial Header */}
      <div className="flex flex-col lg:flex-row lg:items-end justify-between gap-4 pb-2 border-b border-[var(--rule)]">
        <div>
          <div className="flex items-center gap-2 font-mono text-[10px] uppercase tracking-widest text-[var(--stone)] mb-1">
            <span>TIMETABLE ARCHIVE</span>
            <span>·</span>
            <span>CENTRIX ACADEMIC CALENDAR</span>
          </div>
          <h1 className="font-serif text-2xl sm:text-3xl font-normal text-[var(--ink)] tracking-tight">
            Class Timetable
          </h1>
          <p className="text-xs font-mono text-[var(--stone)] mt-1">
            {timetableSummary?.totalRooms
              ? `${timetableSummary.totalRooms} classrooms · ${timetableSummary.totalLectures} scheduled sessions center-wide`
              : `${timetable.length} session${timetable.length !== 1 ? 's' : ''} found on ${formatShortDate(selectedDate)}`}
          </p>
        </div>

        {/* Top Actions */}
        <div className="flex items-center gap-2.5 flex-wrap">
          <button
            onClick={onOpenAddSlot}
            className="flex items-center gap-2 px-4 py-2 text-xs font-mono uppercase tracking-wider bg-[var(--ink)] text-[var(--cream)] hover:opacity-90 active:scale-[0.99] transition cursor-pointer"
          >
            <Plus className="w-3.5 h-3.5" />
            <span>Add Slot</span>
          </button>

          {onSyncNow && (
            <button
              onClick={handleSyncClick}
              disabled={busy || syncing}
              title="Sync latest timetable from Google Sheet"
              className="flex items-center gap-2 px-3.5 py-2 text-xs font-mono uppercase tracking-wider bg-[var(--paper)] text-[var(--ink)] border border-[var(--rule)] hover:bg-[var(--cream)] active:opacity-90 transition disabled:opacity-40 cursor-pointer"
            >
              <RefreshCw className={`w-3.5 h-3.5 ${syncing ? 'animate-spin text-[var(--ink)]' : 'text-[var(--stone)]'}`} />
              <span>{syncing ? 'Syncing...' : 'Sync Sheet'}</span>
            </button>
          )}
        </div>
      </div>

      {/* Full-Width Architectural Calendar Block */}
      <div className="bg-[var(--paper)] border border-[var(--rule)] p-4 sm:p-6 space-y-4">
        {/* Top Control Strip: Navigator & Room Selector */}
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 pb-4 border-b border-[var(--rule)]">
          {/* Date Navigator Controls */}
          <div className="flex items-center gap-2 flex-wrap sm:flex-nowrap">
            {/* Prev Day */}
            <button
              onClick={() => navigateDay(-1)}
              disabled={busy}
              title="Previous Day"
              className="p-2 border border-[var(--rule)] bg-[var(--cream)] hover:bg-[var(--paper)] text-[var(--ink)] transition disabled:opacity-40 cursor-pointer"
            >
              <ChevronLeft className="w-4 h-4" />
            </button>

            {/* Date Picker Label / Native Input */}
            <label className="relative flex items-center gap-2.5 px-3.5 py-2 border border-[var(--rule)] bg-[var(--cream)] hover:bg-[var(--paper)] cursor-pointer transition select-none">
              <CalendarDays className="w-4 h-4 text-[var(--stone)] shrink-0" />
              <div className="text-left min-w-0">
                <span className="text-xs font-serif text-[var(--ink)] font-normal block whitespace-nowrap">
                  {formatDisplayDate(selectedDate)}
                </span>
                <span className="text-[9px] font-mono uppercase tracking-widest text-[var(--stone)] block">
                  Click to select date
                </span>
              </div>
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
              className="p-2 border border-[var(--rule)] bg-[var(--cream)] hover:bg-[var(--paper)] text-[var(--ink)] transition disabled:opacity-40 cursor-pointer"
            >
              <ChevronRight className="w-4 h-4" />
            </button>

            {/* Today Jump */}
            {!isToday && (
              <button
                onClick={() => onSelectDate(todayStr)}
                className="px-3 py-2 text-xs font-mono uppercase tracking-wider border border-[var(--rule)] bg-[var(--cream)] hover:bg-[var(--paper)] text-[var(--ink)] transition cursor-pointer"
              >
                Jump to Today
              </button>
            )}
          </div>

          {/* Quick Room Filter */}
          <div className="flex items-center gap-2.5">
            <span className="text-[10px] font-mono uppercase tracking-widest text-[var(--stone)] shrink-0">
              Filter:
            </span>
            <div className="w-full sm:w-auto min-w-[220px]">
              <RoomSelector
                rooms={rooms}
                selectedRoom={selectedRoom}
                onSelectRoom={onSelectRoom}
                roomCounts={timetableSummary?.roomCounts}
                totalCount={timetableSummary?.totalLectures ?? timetable.length}
                label="Active Classroom"
              />
            </div>
          </div>
        </div>

        {/* 7-Day Horizontal Week Strip */}
        <div className="grid grid-cols-7 gap-2 sm:gap-3">
          {weekDays.map((day) => {
            const isSelected = day.dateStr === selectedDate;
            const dayCount = timetableSummary?.dayCounts[day.dateStr];
            const hasSlots = availableDates.includes(day.dateStr) || (dayCount !== undefined && dayCount > 0);

            return (
              <button
                key={day.dateStr}
                onClick={() => onSelectDate(day.dateStr)}
                className={`py-3 px-2 text-center transition cursor-pointer flex flex-col items-center justify-between min-h-[76px] border ${
                  isSelected
                    ? 'bg-[var(--ink)] text-[var(--cream)] border-[var(--ink)] shadow-sm ring-1 ring-[var(--ink)]'
                    : day.isToday
                    ? 'bg-[var(--cream)] border-[var(--ink)] text-[var(--ink)]'
                    : 'bg-[var(--cream)] hover:bg-[var(--paper)] text-[var(--stone)] border-[var(--rule)] hover:border-[var(--stone)]'
                }`}
              >
                {/* Day Header */}
                <div className="flex items-center gap-1">
                  <span className={`text-[10px] font-mono uppercase tracking-widest ${isSelected ? 'text-[var(--cream)] opacity-90' : 'text-[var(--stone)]'}`}>
                    {day.dayName}
                  </span>
                  {day.isToday && (
                    <span className={`w-1.5 h-1.5 rounded-full ${isSelected ? 'bg-[var(--cream)]' : 'bg-emerald-500'}`} />
                  )}
                </div>

                {/* Day Number */}
                <span className={`text-base sm:text-lg font-serif leading-none my-1 ${isSelected ? 'text-[var(--cream)] font-bold' : 'text-[var(--ink)]'}`}>
                  {day.dayNum}
                </span>

                {/* Day Lecture Count Chip */}
                {dayCount !== undefined && dayCount > 0 ? (
                  <span
                    className={`text-[9px] font-mono px-1.5 py-0.2 tracking-wider ${
                      isSelected
                        ? 'bg-[var(--cream)] text-[var(--ink)] font-bold'
                        : 'border border-[var(--rule)] bg-[var(--paper)] text-[var(--stone)]'
                    }`}
                  >
                    {dayCount} {dayCount === 1 ? 'class' : 'classes'}
                  </span>
                ) : hasSlots ? (
                  <span className={`w-1.5 h-1.5 rounded-full ${isSelected ? 'bg-[var(--cream)]' : 'bg-[var(--stone)]'}`} />
                ) : (
                  <span className="text-[9px] font-mono text-[var(--stone)] opacity-40">—</span>
                )}
              </button>
            );
          })}
        </div>

        {/* Quick Jump Bar for Scheduled Dates in this week */}
        {currentWeekScheduledDates.length > 0 && (
          <div className="pt-2 flex items-center gap-2 overflow-x-auto no-scrollbar text-[11px] font-mono">
            <span className="text-[10px] uppercase tracking-wider text-[var(--stone)] shrink-0">
              Active Dates This Week:
            </span>
            {currentWeekScheduledDates.map((dateStr) => {
              const count = timetableSummary?.dayCounts[dateStr];
              return (
                <button
                  key={dateStr}
                  onClick={() => onSelectDate(dateStr)}
                  className="px-2.5 py-1 uppercase tracking-wider bg-[var(--cream)] hover:bg-[var(--paper)] border border-[var(--rule)] text-[var(--stone)] hover:text-[var(--ink)] transition cursor-pointer shrink-0 flex items-center gap-1.5"
                >
                  <Calendar className="w-3 h-3 text-[var(--stone)]" />
                  <span>{formatShortDate(dateStr)}</span>
                  {count !== undefined && count > 0 && (
                    <span className="text-[9px] px-1 border border-[var(--rule)] bg-[var(--paper)] text-[var(--stone)]">
                      {count}
                    </span>
                  )}
                </button>
              );
            })}
          </div>
        )}
      </div>

      {/* Overrides for this Date (if any) */}
      {overrides.length > 0 && (
        <div className="bg-[var(--paper)] border border-amber-800/40 p-4 space-y-3">
          <div className="flex items-center justify-between gap-2">
            <h3 className="font-mono text-xs uppercase tracking-wider text-amber-500 flex items-center gap-2">
              <CalendarX2 className="w-4 h-4" />
              <span>Active Overrides for {formatShortDate(selectedDate)} ({overrides.length})</span>
            </h3>
            <span className="text-[10px] font-mono text-[var(--stone)]">
              Overrides apply to this date only
            </span>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-2.5">
            {overrides.map((override) => (
              <div
                key={override.overrideId}
                className="bg-[var(--cream)] border border-[var(--rule)] p-3 flex items-center justify-between gap-3"
              >
                <div className="min-w-0 font-mono">
                  <p className="text-xs font-serif text-[var(--ink)]">
                    {override.overrideType === 'Cancelled'
                      ? 'Slot Cancelled'
                      : `Override: ${override.overrideType}`}
                    {override.originalSlotId && (
                      <span className="text-[var(--stone)] text-[10px]">
                        {' '}· {override.originalSlotId}
                      </span>
                    )}
                  </p>
                  {override.newBatchId && (
                    <p className="text-[11px] text-[var(--stone)]">
                      → {override.newBatchId} / {override.newSubjectId}
                    </p>
                  )}
                </div>
                <button
                  onClick={() => onUndoOverride(override.overrideId)}
                  disabled={busy}
                  className="flex items-center gap-1 text-[10px] font-mono uppercase tracking-wider px-2.5 py-1.5 border border-[var(--rule)] bg-[var(--paper)] text-[var(--ink)] hover:bg-[var(--cream)] transition disabled:opacity-40 shrink-0 cursor-pointer"
                >
                  <Undo2 className="w-3 h-3" />
                  <span>Undo</span>
                </button>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Timetable List Section */}
      <div className="space-y-4">
        {/* Section Header */}
        <div className="flex items-center justify-between px-1">
          <div className="flex items-center gap-2">
            <h2 className="font-serif text-lg font-normal text-[var(--ink)]">
              Scheduled Lectures
            </h2>
            <span className="px-2 py-0.5 border border-[var(--rule)] bg-[var(--cream)] text-[var(--ink)] text-[10px] font-mono uppercase tracking-wider">
              {timetable.length} {timetable.length === 1 ? 'lecture' : 'lectures'}
            </span>
          </div>

          <span className="text-xs font-mono text-[var(--stone)]">
            {selectedRoom === 'ALL' ? 'Showing All Classrooms' : `Room ${selectedRoom}`}
          </span>
        </div>

        {/* Timetable Cards or Empty State */}
        {timetable.length === 0 ? (
          <div className="bg-[var(--paper)] border border-[var(--rule)] p-12 text-center space-y-4">
            <div className="w-12 h-12 border border-[var(--rule)] bg-[var(--cream)] flex items-center justify-center mx-auto text-[var(--stone)]">
              <Calendar className="w-6 h-6" />
            </div>
            <div className="space-y-1.5 max-w-md mx-auto">
              <h3 className="font-serif text-lg font-normal text-[var(--ink)]">
                No Classes Scheduled
              </h3>
              <p className="text-xs font-mono text-[var(--stone)] leading-relaxed">
                {selectedRoom === 'ALL'
                  ? `No lectures found across any classroom for ${formatDisplayDate(selectedDate)}.`
                  : `Room ${selectedRoom} has no timetable entries on ${formatDisplayDate(selectedDate)}.`}
              </p>
            </div>

            {/* Quick Actions */}
            <div className="flex items-center justify-center gap-3 pt-2 flex-wrap">
              {nextActiveDay && (
                <button
                  onClick={() => onSelectDate(nextActiveDay)}
                  className="inline-flex items-center gap-2 px-4 py-2 bg-[var(--ink)] hover:opacity-90 text-[var(--cream)] font-mono text-xs uppercase tracking-wider transition cursor-pointer"
                >
                  <span>Go to Next Class: {formatShortDate(nextActiveDay)} →</span>
                  {timetableSummary?.dayCounts[nextActiveDay] && (
                    <span className="px-1.5 py-0.2 border border-[var(--rule)] bg-[var(--cream)] text-[var(--ink)] text-[10px]">
                      {timetableSummary.dayCounts[nextActiveDay]}
                    </span>
                  )}
                </button>
              )}
              <button
                onClick={onOpenAddSlot}
                className="inline-flex items-center gap-2 px-4 py-2 border border-[var(--rule)] bg-[var(--cream)] hover:bg-[var(--paper)] text-[var(--ink)] font-mono text-xs uppercase tracking-wider transition cursor-pointer"
              >
                <Plus className="w-3.5 h-3.5" />
                <span>Add Extra Slot</span>
              </button>
            </div>
          </div>
        ) : (
          <div className="grid grid-cols-1 lg:grid-cols-2 2xl:grid-cols-3 gap-4">
            {timetable.map((slot) => {
              const timingState = getSlotTimingState(
                selectedDate,
                slot.slotStartTime,
                slot.slotEndTime
              );
              const duration = getDurationMinutes(slot.slotStartTime, slot.slotEndTime);

              return (
                <div
                  key={slot.timetableEntryId}
                  className={`bg-[var(--paper)] border p-5 flex flex-col justify-between transition-all ${
                    timingState === 'live'
                      ? 'border-[var(--ink)] ring-2 ring-[var(--ink)]'
                      : 'border-[var(--rule)] hover:border-[var(--ink)]'
                  }`}
                >
                  {/* Card Header: Timing + Status */}
                  <div className="flex items-center justify-between pb-3 border-b border-[var(--rule)]">
                    <div className="flex items-center gap-2">
                      <div className="flex items-center gap-1.5 text-xs font-mono font-medium text-[var(--ink)]">
                        <Clock className="w-3.5 h-3.5 text-[var(--stone)]" />
                        <span>{formatTime12h(slot.slotStartTime)} — {formatTime12h(slot.slotEndTime)}</span>
                      </div>
                      <span className="text-[10px] font-mono text-[var(--stone)] px-1.5 py-0.2 border border-[var(--rule)] bg-[var(--cream)]">
                        {duration}m
                      </span>
                    </div>

                    {timingState === 'live' ? (
                      <span className="inline-flex items-center gap-1.5 px-2 py-0.5 text-[9px] font-mono uppercase bg-emerald-950/20 text-emerald-500 border border-emerald-700/40">
                        <Radio className="w-3 h-3 animate-pulse" />
                        LIVE NOW
                      </span>
                    ) : timingState === 'upcoming' && isToday ? (
                      <span className="px-2 py-0.5 text-[9px] font-mono uppercase bg-[var(--cream)] text-[var(--stone)] border border-[var(--rule)]">
                        Upcoming
                      </span>
                    ) : (
                      <span className="text-[10px] font-mono text-[var(--stone)]">
                        Room {slot.roomId}
                      </span>
                    )}
                  </div>

                  {/* Card Body: Batch, Subject, Faculty */}
                  <div className="py-4 space-y-2.5">
                    <div>
                      <span className="text-[9px] font-mono uppercase tracking-widest text-[var(--stone)] block mb-0.5">
                        Batch
                      </span>
                      <h3 className="font-serif text-xl font-normal text-[var(--ink)] tracking-tight">
                        {slot.batchId}
                      </h3>
                    </div>

                    <div className="flex items-center gap-2 flex-wrap pt-1">
                      {/* Subject Badge */}
                      <span className="px-2.5 py-1 text-xs font-mono uppercase tracking-wider border border-[var(--rule)] bg-[var(--cream)] text-[var(--ink)]">
                        {slot.subjectId}
                      </span>

                      {/* Faculty */}
                      {slot.teacherId && (
                        <span className="text-xs font-mono text-[var(--stone)] flex items-center gap-1.5">
                          <User className="w-3.5 h-3.5 text-[var(--stone)]" />
                          <span className="text-[var(--ink)] font-serif">{slot.teacherId}</span>
                        </span>
                      )}
                    </div>
                  </div>

                  {/* Card Footer: Metadata & Override Action */}
                  <div className="pt-3 border-t border-[var(--rule)] flex items-center justify-between text-[10px] font-mono text-[var(--stone)]">
                    <div className="flex items-center gap-2">
                      <span className="px-1.5 py-0.2 border border-[var(--rule)] bg-[var(--cream)] text-[var(--ink)] font-bold">
                        R{slot.roomId}
                      </span>
                      <span className="truncate max-w-[120px]" title={slot.slotId}>
                        {slot.slotId}
                      </span>
                    </div>

                    <button
                      onClick={() => onCancelSlot(slot)}
                      disabled={busy}
                      title="Cancel this slot for this date"
                      className="flex items-center gap-1 px-2 py-1 border border-[var(--rule)] bg-[var(--cream)] hover:bg-red-950/20 text-[var(--stone)] hover:text-red-500 hover:border-red-700/40 transition disabled:opacity-40 cursor-pointer"
                    >
                      <CalendarX2 className="w-3 h-3" />
                      <span>Cancel Slot</span>
                    </button>
                  </div>
                </div>
              );
            })}
          </div>
        )}

        {/* Schedule Footnote */}
        <div className="p-3 border border-[var(--rule)] bg-[var(--cream)] flex items-center justify-between text-xs font-mono text-[var(--stone)]">
          <div className="flex items-center gap-2">
            <Calendar className="w-3.5 h-3.5 shrink-0 text-[var(--stone)]" />
            <span>Master Timetable is synced with Center Google Sheets. Cancelling an entry creates a local override for {formatShortDate(selectedDate)} only.</span>
          </div>
        </div>
      </div>
    </div>
  );
}
