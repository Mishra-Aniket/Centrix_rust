import { Building2, ChevronDown } from 'lucide-react';

interface RoomSelectorProps {
  rooms: string[];
  selectedRoom: string;
  onSelectRoom: (roomId: string) => void;
  localRoomId?: string;
  roomCounts?: Record<string, number>;
  totalCount?: number;
  label?: string;
}

export function RoomSelector({
  rooms,
  selectedRoom,
  onSelectRoom,
  localRoomId,
  roomCounts,
  totalCount,
  label = 'Select Classroom Room',
}: RoomSelectorProps) {
  const isAll = selectedRoom === 'ALL' || !selectedRoom;

  return (
    <div className="bg-white border border-slate-200/90 rounded-2xl p-2.5 shadow-xs flex items-center justify-between gap-2">
      <div className="flex items-center gap-2 min-w-0">
        <div className="p-1.5 rounded-xl bg-violet-50 text-violet-600 shrink-0">
          <Building2 className="w-4 h-4" />
        </div>
        <div className="min-w-0">
          <label className="text-[10px] font-bold uppercase tracking-wider text-slate-400 block truncate">
            {label}
          </label>
          <div className="flex items-center gap-1.5">
            <span className="text-xs font-bold text-slate-900 truncate">
              {isAll ? 'All Rooms (Center-wide)' : `Room ${selectedRoom}`}
            </span>
            {selectedRoom === localRoomId && (
              <span className="text-[9px] font-mono px-1.5 py-0.2 rounded bg-cyan-50 text-cyan-700 border border-cyan-200 shrink-0 font-semibold">
                THIS PC
              </span>
            )}
          </div>
        </div>
      </div>

      <div className="relative shrink-0">
        <select
          value={selectedRoom || 'ALL'}
          onChange={(e) => onSelectRoom(e.target.value)}
          className="appearance-none text-xs font-semibold pl-3 pr-7 py-1.5 bg-slate-50 hover:bg-slate-100 border border-slate-200 rounded-xl text-slate-800 focus:ring-2 focus:ring-violet-500/30 focus:outline-hidden cursor-pointer shadow-xs transition"
        >
          <option value="ALL">
            All Rooms {totalCount !== undefined ? `(${totalCount})` : `(${rooms.length} rooms)`}
          </option>
          {rooms.map((room) => {
            const count = roomCounts?.[room];
            const isLocal = room === localRoomId;
            return (
              <option key={room} value={room}>
                Room {room} {isLocal ? '★ THIS PC' : ''} {count !== undefined ? `(${count})` : ''}
              </option>
            );
          })}
        </select>
        <ChevronDown className="w-3.5 h-3.5 text-slate-400 pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2" />
      </div>
    </div>
  );
}
