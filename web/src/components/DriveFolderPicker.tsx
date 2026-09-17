import { useEffect, useRef, useState } from 'react';
import {
  Check,
  ChevronDown,
  Folder,
  FolderPlus,
  HardDrive,
  Loader2,
  RefreshCw,
  Search,
  Sparkles,
  X,
} from 'lucide-react';
import * as api from '../api';

interface DriveFolderPickerProps {
  value: string;
  onChange: (folder: string) => void;
  placeholder?: string;
  autoFocus?: boolean;
  label?: string;
  showBreadcrumb?: boolean;
}

export function DriveFolderPicker({
  value,
  onChange,
  placeholder = 'Search or type exact Google Drive folder name...',
  autoFocus = false,
  label = 'Batch / Google Drive Folder',
  showBreadcrumb = false,
}: DriveFolderPickerProps) {
  const [folders, setFolders] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [isOpen, setIsOpen] = useState(false);
  const [searchTerm, setSearchTerm] = useState(value);
  const [showAllChips, setShowAllChips] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const loadFolders = async (query?: string) => {
    setLoading(true);
    try {
      const items = await api.fetchDriveFolders(query);
      setFolders(items);
    } catch {
      // ignore
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadFolders();
  }, []);

  useEffect(() => {
    setSearchTerm(value);
  }, [value]);

  // Click outside to close dropdown without any full-screen click-stealing overlay
  useEffect(() => {
    if (!isOpen) return;
    const handleClickOutside = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setIsOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [isOpen]);

  const handleInputChange = (val: string) => {
    setSearchTerm(val);
    onChange(val);
  };

  const handleClear = () => {
    setSearchTerm('');
    onChange('');
    inputRef.current?.focus();
  };

  const handleSelectFolder = (folderName: string) => {
    onChange(folderName);
    setSearchTerm(folderName);
    setIsOpen(false);
  };

  const trimmedValue = value.trim();
  const exactMatch = folders.some(
    (f) => f.toLowerCase() === trimmedValue.toLowerCase()
  );
  const isNewFolder = trimmedValue.length > 0 && !exactMatch;

  // Filter folders matching current search or input
  const query = (searchTerm || '').trim().toLowerCase();
  const filteredFolders = query
    ? folders.filter((f) => f.toLowerCase().includes(query))
    : folders;

  // Quick select chips: ALWAYS RENDERED to keep layout stable and prevent squeezing
  const chipsToDisplay = filteredFolders.length > 0 ? filteredFolders : folders;
  const maxInitialChips = 8;
  const visibleChips = showAllChips
    ? chipsToDisplay
    : chipsToDisplay.slice(0, maxInitialChips);
  const hasMoreChips = chipsToDisplay.length > maxInitialChips;

  return (
    <div ref={containerRef} className="space-y-2 relative">
      {/* Label Row */}
      <div className="flex items-center justify-between text-xs font-semibold text-slate-700">
        <span className="flex items-center gap-1.5">
          <HardDrive className="w-4 h-4 text-cyan-600" />
          <span>{label}</span>
        </span>
        <div className="flex items-center gap-2">
          {trimmedValue && (
            <span
              className={`text-[10px] font-semibold px-2 py-0.5 rounded-full flex items-center gap-1 ${
                exactMatch
                  ? 'bg-emerald-50 text-emerald-700 border border-emerald-200'
                  : 'bg-cyan-50 text-cyan-700 border border-cyan-200'
              }`}
            >
              {exactMatch ? (
                <>
                  <Check className="w-3 h-3 text-emerald-600" />
                  <span>Existing Drive Folder</span>
                </>
              ) : (
                <>
                  <Sparkles className="w-3 h-3 text-cyan-600" />
                  <span>New Folder (Auto-Created)</span>
                </>
              )}
            </span>
          )}
          <button
            type="button"
            onClick={() => loadFolders(searchTerm)}
            disabled={loading}
            className="text-[10px] text-cyan-600 hover:text-cyan-800 flex items-center gap-1 active:scale-95 transition font-medium cursor-pointer"
            title="Refresh folders from Google Drive"
          >
            <RefreshCw className={`w-3 h-3 ${loading ? 'animate-spin' : ''}`} />
            <span>Refresh</span>
          </button>
        </div>
      </div>

      {/* Omni-Input Box */}
      <div className="relative">
        <div className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400 pointer-events-none">
          <Search className="w-4 h-4" />
        </div>
        <input
          ref={inputRef}
          type="text"
          value={searchTerm}
          onChange={(e) => handleInputChange(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Escape') {
              setIsOpen(false);
            } else if (e.key === 'Enter') {
              e.preventDefault();
              if (filteredFolders.length > 0) {
                handleSelectFolder(filteredFolders[0]);
              }
            }
          }}
          placeholder={placeholder}
          autoFocus={autoFocus}
          className="w-full pl-9 pr-20 py-2.5 bg-slate-50 hover:bg-white focus:bg-white border border-slate-200 focus:border-cyan-500 rounded-xl text-xs font-mono font-bold text-slate-900 focus:outline-none focus:ring-3 focus:ring-cyan-500/20 shadow-xs transition"
        />

        <div className="absolute right-2 top-1/2 -translate-y-1/2 flex items-center gap-1">
          {searchTerm && (
            <button
              type="button"
              onClick={handleClear}
              className="p-1 text-slate-400 hover:text-slate-600 rounded-md hover:bg-slate-100 transition cursor-pointer"
              title="Clear folder name"
            >
              <X className="w-3.5 h-3.5" />
            </button>
          )}

          {loading ? (
            <Loader2 className="w-4 h-4 text-cyan-600 animate-spin mr-1" />
          ) : (
            <button
              type="button"
              onClick={() => setIsOpen((prev) => !prev)}
              className="p-1 text-slate-400 hover:text-slate-700 rounded-lg hover:bg-slate-100 transition cursor-pointer"
              title="Browse Google Drive Folders"
            >
              <ChevronDown
                className={`w-4 h-4 transition-transform duration-200 ${
                  isOpen ? 'rotate-180' : ''
                }`}
              />
            </button>
          )}
        </div>

        {/* Dropdown Menu (Anchored directly under input, non-blocking click-outside) */}
        {isOpen && (
          <div className="absolute left-0 right-0 top-full mt-1.5 z-40 bg-white border border-slate-200/90 rounded-2xl shadow-xl max-h-56 overflow-y-auto p-1.5 text-xs space-y-1">
            {isNewFolder && (
              <button
                type="button"
                onClick={() => handleSelectFolder(trimmedValue)}
                className="w-full flex items-center gap-2 p-2 rounded-xl bg-cyan-50/80 hover:bg-cyan-100/80 text-cyan-900 border border-cyan-200 transition text-left cursor-pointer"
              >
                <FolderPlus className="w-4 h-4 text-cyan-600 shrink-0" />
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-1 font-bold font-mono text-xs truncate">
                    <span>Use &quot;{trimmedValue}&quot;</span>
                    <span className="text-[10px] px-1.5 py-0.2 rounded bg-cyan-200/80 text-cyan-800 font-sans font-semibold">
                      New Folder
                    </span>
                  </div>
                  <p className="text-[10px] text-cyan-700 font-sans">
                    Will be created on Google Drive
                  </p>
                </div>
              </button>
            )}

            <div className="px-2 py-1 text-[10px] font-bold uppercase tracking-wider text-slate-400 flex items-center justify-between">
              <span>Existing Drive Folders ({filteredFolders.length})</span>
              <span className="text-[9px] text-slate-400 font-normal">Click to select</span>
            </div>

            {filteredFolders.length === 0 && !isNewFolder ? (
              <div className="p-4 text-center text-slate-400 text-xs">
                {loading ? 'Searching Google Drive...' : 'No folders match search'}
              </div>
            ) : (
              filteredFolders.map((f) => {
                const isSelected = value.toLowerCase() === f.toLowerCase();
                return (
                  <button
                    key={f}
                    type="button"
                    onClick={() => handleSelectFolder(f)}
                    className={`w-full flex items-center justify-between px-3 py-2 rounded-xl text-left transition cursor-pointer ${
                      isSelected
                        ? 'bg-slate-900 text-white font-bold shadow-xs'
                        : 'hover:bg-slate-50 text-slate-800'
                    }`}
                  >
                    <span className="flex items-center gap-2.5 truncate">
                      <Folder
                        className={`w-4 h-4 shrink-0 ${
                          isSelected ? 'text-cyan-400' : 'text-cyan-600'
                        }`}
                      />
                      <span className="font-mono text-xs truncate">{f}</span>
                    </span>
                    {isSelected && (
                      <Check className="w-4 h-4 text-white shrink-0 ml-1" />
                    )}
                  </button>
                );
              })
            )}
          </div>
        )}
      </div>

      {/* Target Path Breadcrumb Preview (Optional internal breadcrumb) */}
      {showBreadcrumb && trimmedValue && (
        <div className="bg-slate-100/70 border border-slate-200/80 rounded-lg px-2.5 py-1.5 flex items-center gap-1.5 text-[11px] font-mono text-slate-600 truncate">
          <Folder className="w-3.5 h-3.5 text-cyan-600 shrink-0" />
          <span className="text-slate-400">Google Drive /</span>
          <strong className="text-slate-900 font-bold truncate">{trimmedValue}</strong>
        </div>
      )}

      {/* Quick Select Chips - ALWAYS VISIBLE TO PREVENT SQUEEZING */}
      {folders.length > 0 && (
        <div className="pt-0.5 space-y-1.5">
          <div className="flex items-center justify-between">
            <span className="text-[10px] font-bold text-slate-400 uppercase tracking-wider block">
              {searchTerm.trim() && filteredFolders.length < folders.length
                ? `Matching Drive Folders (${filteredFolders.length}):`
                : 'Quick Select from Drive:'}
            </span>
            {hasMoreChips && (
              <button
                type="button"
                onClick={() => setShowAllChips((prev) => !prev)}
                className="text-[10px] font-semibold text-cyan-700 hover:text-cyan-800 transition cursor-pointer"
              >
                {showAllChips ? 'Show Top' : `View All (${chipsToDisplay.length})`}
              </button>
            )}
          </div>

          <div className="flex flex-wrap gap-1.5">
            {isNewFolder && (
              <button
                type="button"
                onClick={() => handleSelectFolder(trimmedValue)}
                className="text-[10px] font-mono px-2.5 py-1 rounded-lg border border-cyan-300 bg-cyan-50 hover:bg-cyan-100 text-cyan-900 font-bold flex items-center gap-1.5 active:scale-95 transition shadow-2xs cursor-pointer"
              >
                <Sparkles className="w-3 h-3 text-cyan-600 shrink-0" />
                <span>+ Use "{trimmedValue}"</span>
              </button>
            )}

            {visibleChips.map((f) => {
              const isSelected = value.toLowerCase() === f.toLowerCase();
              return (
                <button
                  key={f}
                  type="button"
                  onClick={() => handleSelectFolder(f)}
                  className={`text-[10px] font-mono px-2.5 py-1 rounded-lg border transition-all active:scale-95 flex items-center gap-1.5 cursor-pointer ${
                    isSelected
                      ? 'bg-slate-900 border-slate-900 text-white font-bold shadow-xs'
                      : 'bg-white hover:bg-slate-100 border-slate-200 text-slate-700 hover:text-slate-900 font-medium'
                  }`}
                >
                  <Folder
                    className={`w-3 h-3 shrink-0 ${
                      isSelected ? 'text-cyan-400' : 'text-cyan-600'
                    }`}
                  />
                  <span className="truncate max-w-[140px]">{f}</span>
                  {isSelected && <Check className="w-3 h-3 text-white shrink-0 ml-0.5" />}
                </button>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
