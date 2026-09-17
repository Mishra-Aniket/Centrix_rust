import { Building2 } from 'lucide-react';
import { SearchableRoomSelect } from './SearchableRoomSelect';

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
    <div className="bg-[var(--paper)] border border-[var(--rule)] p-3 flex items-center justify-between gap-3">
      <div className="flex items-center gap-2.5 min-w-0">
        <div className="w-8 h-8 border border-[var(--rule)] bg-[var(--cream)] text-[var(--ink)] flex items-center justify-center shrink-0">
          <Building2 className="w-4 h-4" />
        </div>
        <div className="min-w-0">
          <label className="text-[10px] font-mono uppercase tracking-widest text-[var(--stone)] block truncate">
            {label}
          </label>
          <div className="flex items-center gap-2 mt-0.5">
            <span className="font-serif text-sm font-normal text-[var(--ink)] truncate">
              {isAll ? 'All Rooms (Center-wide)' : `Room ${selectedRoom}`}
            </span>
            {selectedRoom === localRoomId && (
              <span className="text-[9px] font-mono px-1.5 py-0.2 border border-[var(--rule)] bg-[var(--cream)] text-[var(--stone)] shrink-0 font-medium">
                THIS PC
              </span>
            )}
          </div>
        </div>
      </div>

      <div className="shrink-0 min-w-[190px]">
        <SearchableRoomSelect
          rooms={rooms}
          selectedRoom={selectedRoom}
          onSelectRoom={onSelectRoom}
          localRoomId={localRoomId}
          roomCounts={roomCounts}
          totalCount={totalCount}
          compact={true}
        />
      </div>
    </div>
  );
}
