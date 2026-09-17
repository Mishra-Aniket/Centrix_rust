import { useState, useRef, useEffect, useMemo } from 'react';
import { Search, ChevronDown, Check, Building2, X } from 'lucide-react';

interface SearchableRoomSelectProps {
  rooms: string[];
  selectedRoom: string;
  onSelectRoom: (roomId: string) => void;
  localRoomId?: string;
  roomCounts?: Record<string, number>;
  totalCount?: number;
  placeholder?: string;
  includeAllOption?: boolean;
  compact?: boolean;
  className?: string;
}

export function SearchableRoomSelect({
  rooms,
  selectedRoom,
  onSelectRoom,
  localRoomId,
  roomCounts,
  totalCount,
  placeholder = 'Select Room',
  includeAllOption = true,
  compact = false,
  className = '',
}: SearchableRoomSelectProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const dropdownRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // Close dropdown on outside click
  useEffect(() => {
    const handleOutsideClick = (e: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) {
        setIsOpen(false);
      }
    };
    if (isOpen) {
      document.addEventListener('mousedown', handleOutsideClick);
      // Auto focus search input
      setTimeout(() => inputRef.current?.focus(), 50);
    }
    return () => document.removeEventListener('mousedown', handleOutsideClick);
  }, [isOpen]);

  // Handle escape key
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && isOpen) {
        setIsOpen(false);
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen]);

  // Filtered rooms list
  const filteredRooms = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    if (!q) return rooms;
    return rooms.filter((r) =>
      r.toLowerCase().includes(q) || `room ${r}`.toLowerCase().includes(q)
    );
  }, [rooms, searchQuery]);

  const isAllSelected = selectedRoom === 'ALL' || !selectedRoom;

  const currentLabel = useMemo(() => {
    if (isAllSelected) {
      return totalCount !== undefined
        ? `All Rooms (${totalCount})`
        : `All Rooms (${rooms.length})`;
    }
    const isLocal = selectedRoom === localRoomId;
    return `Room ${selectedRoom}${isLocal ? ' (THIS PC)' : ''}`;
  }, [isAllSelected, totalCount, rooms.length, selectedRoom, localRoomId]);

  return (
    <div ref={dropdownRef} className={`relative inline-block ${compact ? '' : 'w-full'} ${className}`}>
      {/* Trigger Button */}
      <button
        type="button"
        onClick={() => setIsOpen((prev) => !prev)}
        className={`flex items-center justify-between gap-2 border border-[var(--rule)] bg-[var(--cream)] hover:bg-[var(--paper)] text-[var(--ink)] font-mono transition cursor-pointer ${
          compact
            ? 'py-1 px-2.5 text-xs uppercase tracking-wider'
            : 'w-full py-2 px-3 text-xs uppercase tracking-wider'
        } ${isOpen ? 'ring-1 ring-[var(--ink)]' : ''}`}
      >
        <div className="flex items-center gap-2 truncate">
          {!compact && <Building2 className="w-3.5 h-3.5 text-[var(--stone)] shrink-0" />}
          <span className="truncate font-medium">{currentLabel || placeholder}</span>
        </div>
        <ChevronDown
          className={`w-3.5 h-3.5 text-[var(--stone)] transition-transform duration-150 shrink-0 ${
            isOpen ? 'rotate-180 text-[var(--ink)]' : ''
          }`}
        />
      </button>

      {/* Popover Dropdown */}
      {isOpen && (
        <div
          className={`absolute left-0 mt-1 bg-[var(--paper)] border border-[var(--rule)] shadow-2xl z-50 animate-om-dropdown flex flex-col ${
            compact ? 'min-w-[240px] right-0 sm:right-auto' : 'w-full min-w-[260px]'
          }`}
          style={{ maxHeight: '360px' }}
        >
          {/* Search Header */}
          <div className="p-2 border-b border-[var(--rule)] bg-[var(--cream)] flex items-center gap-2">
            <Search className="w-3.5 h-3.5 text-[var(--stone)] shrink-0" />
            <input
              ref={inputRef}
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Search room (e.g. 601)..."
              className="w-full bg-transparent text-xs font-mono text-[var(--ink)] placeholder-[var(--stone)] focus:outline-none"
            />
            {searchQuery && (
              <button
                type="button"
                onClick={() => setSearchQuery('')}
                className="p-0.5 text-[var(--stone)] hover:text-[var(--ink)] cursor-pointer"
              >
                <X className="w-3 h-3" />
              </button>
            )}
          </div>

          {/* Options List */}
          <div className="overflow-y-auto flex-1 divide-y divide-[var(--rule)] font-mono text-xs">
            {/* "All Classrooms" Option */}
            {includeAllOption && !searchQuery && (
              <button
                type="button"
                onClick={() => {
                  onSelectRoom('ALL');
                  setIsOpen(false);
                  setSearchQuery('');
                }}
                className={`w-full px-3 py-2 text-left flex items-center justify-between transition cursor-pointer ${
                  isAllSelected
                    ? 'bg-[var(--ink)] text-[var(--cream)] font-bold'
                    : 'hover:bg-[var(--cream)] text-[var(--ink)]'
                }`}
              >
                <div className="flex items-center gap-2 truncate">
                  <span>All Classrooms</span>
                  {totalCount !== undefined && (
                    <span
                      className={`text-[9px] px-1.5 py-0.2 border ${
                        isAllSelected
                          ? 'border-[var(--cream)] bg-transparent text-[var(--cream)]'
                          : 'border-[var(--rule)] bg-[var(--paper)] text-[var(--stone)]'
                      }`}
                    >
                      {totalCount} lectures
                    </span>
                  )}
                </div>
                {isAllSelected && <Check className="w-3.5 h-3.5 shrink-0" />}
              </button>
            )}

            {/* Individual Room Options */}
            {filteredRooms.length === 0 ? (
              <div className="p-4 text-center text-xs font-mono text-[var(--stone)]">
                No classrooms matching "{searchQuery}"
              </div>
            ) : (
              filteredRooms.map((room) => {
                const isSelected = selectedRoom === room;
                const isLocal = room === localRoomId;
                const count = roomCounts?.[room];

                return (
                  <button
                    key={room}
                    type="button"
                    onClick={() => {
                      onSelectRoom(room);
                      setIsOpen(false);
                      setSearchQuery('');
                    }}
                    className={`w-full px-3 py-2 text-left flex items-center justify-between transition cursor-pointer ${
                      isSelected
                        ? 'bg-[var(--ink)] text-[var(--cream)] font-bold'
                        : 'hover:bg-[var(--cream)] text-[var(--ink)]'
                    }`}
                  >
                    <div className="flex items-center gap-2 truncate">
                      <span className="font-medium">Room {room}</span>
                      {isLocal && (
                        <span
                          className={`text-[9px] font-mono px-1 py-0.2 uppercase border ${
                            isSelected
                              ? 'border-[var(--cream)] text-[var(--cream)]'
                              : 'border-[var(--rule)] bg-[var(--cream)] text-emerald-600 font-bold'
                          }`}
                        >
                          THIS PC
                        </span>
                      )}
                      {count !== undefined && count > 0 && (
                        <span
                          className={`text-[9px] font-mono px-1 py-0.2 border ${
                            isSelected
                              ? 'border-[var(--cream)] text-[var(--cream)]'
                              : 'border-[var(--rule)] bg-[var(--paper)] text-[var(--stone)]'
                          }`}
                        >
                          {count} {count === 1 ? 'class' : 'classes'}
                        </span>
                      )}
                    </div>
                    {isSelected && <Check className="w-3.5 h-3.5 shrink-0" />}
                  </button>
                );
              })
            )}
          </div>
        </div>
      )}
    </div>
  );
}
