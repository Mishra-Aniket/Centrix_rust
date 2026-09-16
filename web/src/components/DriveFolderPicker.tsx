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
} from 'lucide-react';
import * as api from '../api';

interface DriveFolderPickerProps {
  value: string;
  onChange: (folder: string) => void;
  placeholder?: string;
  autoFocus?: boolean;
  label?: string;
}

export function DriveFolderPicker({
  value,
  onChange,
  placeholder = 'Search or type exact Google Drive folder name...',
  autoFocus = false,
  label = 'Google Drive Target Folder',
}: DriveFolderPickerProps) {
  const [folders, setFolders] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [isOpen, setIsOpen] = useState(false);
  const [searchTerm, setSearchTerm] = useState(value);
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

  const handleInputChange = (val: string) => {
    setSearchTerm(val);
    onChange(val);
    setIsOpen(true);
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

  const filteredFolders = folders.filter((f) =>
    f.toLowerCase().includes((searchTerm || '').toLowerCase())
  );

  return (
    <div className="space-y-2 relative">
      {/* Label Row */}
      <div className="flex items-center justify-between text-xs font-semibold text-slate-700">
        <span className="flex items-center gap-1.5">
          <HardDrive className="w-4 h-4 text-cyan-600" />
          {label}
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
            className="text-[10px] text-cyan-600 hover:text-cyan-800 flex items-center gap-1 active:scale-95 transition font-medium"
            title="Refresh folders from Google Drive"
          >
            <RefreshCw className={`w-3 h-3 ${loading ? 'animate-spin' : ''}`} />
            Refresh
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
          onFocus={() => setIsOpen(true)}
          placeholder={placeholder}
          autoFocus={autoFocus}
          className="w-full pl-9 pr-20 py-2.5 bg-slate-50 hover:bg-white focus:bg-white border border-slate-200 focus:border-cyan-500 rounded-xl text-xs font-mono font-bold text-slate-900 focus:outline-none focus:ring-3 focus:ring-cyan-500/20 shadow-xs transition"
        />

        <div className="absolute right-2 top-1/2 -translate-y-1/2 flex items-center gap-1">
          {loading ? (
            <Loader2 className="w-4 h-4 text-cyan-600 animate-spin mr-1" />
          ) : (
            <button
              type="button"
              onClick={() => setIsOpen((prev) => !prev)}
              className="p-1 text-slate-400 hover:text-slate-700 rounded-lg hover:bg-slate-100 transition"
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
      </div>

      {/* Target Path Breadcrumb Preview */}
      {trimmedValue && (
        <div className="bg-slate-100/70 border border-slate-200/80 rounded-lg px-2.5 py-1.5 flex items-center gap-1.5 text-[11px] font-mono text-slate-600 truncate">
          <Folder className="w-3.5 h-3.5 text-cyan-600 shrink-0" />
          <span className="text-slate-400">LectureRecordings /</span>
          <strong className="text-cyan-800 font-bold truncate">{trimmedValue}</strong>
        </div>
      )}

      {/* Dropdown Menu */}
      {isOpen && (
        <>
          <div
            className="fixed inset-0 z-40"
            onClick={() => setIsOpen(false)}
          />
          <div className="absolute left-0 right-0 top-full mt-1.5 z-50 bg-white border border-slate-200 rounded-2xl shadow-2xl max-h-64 overflow-y-auto p-1.5 text-xs space-y-1">
            {/* If typed something new, offer 1-click create */}
            {isNewFolder && (
              <button
                type="button"
                onClick={() => handleSelectFolder(trimmedValue)}
                className="w-full flex items-center gap-2 p-2 rounded-xl bg-cyan-50/80 hover:bg-cyan-100/80 text-cyan-900 border border-cyan-200 transition text-left"
              >
                <FolderPlus className="w-4 h-4 text-cyan-600 shrink-0" />
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-1 font-bold font-mono text-xs truncate">
                    <span>Use &quot;{trimmedValue}&quot;</span>
                    <span className="text-[10px] px-1.5 py-0.2 rounded bg-cyan-200/80 text-cyan-800 font-sans">
                      New Folder
                    </span>
                  </div>
                  <p className="text-[10px] text-cyan-700 font-sans">
                    Will be created inside LectureRecordings root on Google Drive
                  </p>
                </div>
              </button>
            )}

            <div className="px-2 py-1 text-[10px] font-bold uppercase tracking-wider text-slate-400">
              Existing Drive Folders ({filteredFolders.length})
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
                    className={`w-full flex items-center justify-between px-3 py-2 rounded-xl text-left transition ${
                      isSelected
                        ? 'bg-cyan-600 text-white font-bold shadow-xs'
                        : 'hover:bg-slate-50 text-slate-800'
                    }`}
                  >
                    <span className="flex items-center gap-2.5 truncate">
                      <Folder
                        className={`w-4 h-4 shrink-0 ${
                          isSelected ? 'text-white' : 'text-cyan-600'
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
        </>
      )}

      {/* Quick Select Chips */}
      {folders.length > 0 && !isOpen && (
        <div className="pt-0.5 space-y-1">
          <span className="text-[10px] font-semibold text-slate-400 uppercase tracking-wider block">
            Quick Select from Drive:
          </span>
          <div className="flex flex-wrap gap-1.5">
            {folders.slice(0, 8).map((f) => (
              <button
                key={f}
                type="button"
                onClick={() => handleSelectFolder(f)}
                className={`text-[10px] font-mono px-2.5 py-1 rounded-lg border transition active:scale-95 flex items-center gap-1 ${
                  value.toLowerCase() === f.toLowerCase()
                    ? 'bg-cyan-600 border-cyan-600 text-white font-bold shadow-2xs'
                    : 'bg-white hover:bg-slate-100 border-slate-200 text-slate-700'
                }`}
              >
                <Folder className="w-3 h-3 text-cyan-600 shrink-0" />
                <span className="truncate max-w-[140px]">{f}</span>
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
