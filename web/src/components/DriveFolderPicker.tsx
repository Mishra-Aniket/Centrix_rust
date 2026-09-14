import { useEffect, useState } from 'react';
import { Check, ChevronDown, Folder, HardDrive, Loader2, RefreshCw, Search } from 'lucide-react';
import * as api from '../api';

interface DriveFolderPickerProps {
  value: string;
  onChange: (folder: string) => void;
  placeholder?: string;
  autoFocus?: boolean;
}

export function DriveFolderPicker({
  value,
  onChange,
  placeholder = 'Select or type Google Drive folder...',
  autoFocus = false,
}: DriveFolderPickerProps) {
  const [folders, setFolders] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [isOpen, setIsOpen] = useState(false);
  const [searchTerm, setSearchTerm] = useState('');

  const loadFolders = async () => {
    setLoading(true);
    try {
      const items = await api.fetchDriveFolders();
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

  const filteredFolders = folders.filter((f) =>
    f.toLowerCase().includes(searchTerm.toLowerCase())
  );

  return (
    <div className="space-y-1.5 relative">
      <div className="flex items-center justify-between text-[11px] text-slate-600 font-semibold">
        <span className="flex items-center gap-1">
          <HardDrive className="w-3.5 h-3.5 text-cyan-600" />
          Google Drive Target Folder
        </span>
        <button
          type="button"
          onClick={loadFolders}
          disabled={loading}
          className="text-[10px] text-cyan-600 hover:text-cyan-700 flex items-center gap-1 active:scale-95 transition"
        >
          <RefreshCw className={`w-3 h-3 ${loading ? 'animate-spin' : ''}`} />
          Refresh Drive
        </button>
      </div>

      <div className="relative">
        <input
          type="text"
          value={value}
          onChange={(e) => {
            onChange(e.target.value);
            setSearchTerm(e.target.value);
          }}
          onFocus={() => setIsOpen(true)}
          placeholder={placeholder}
          autoFocus={autoFocus}
          className="w-full px-3 py-2 pr-16 bg-white border border-slate-200 rounded-xl text-xs font-mono font-medium focus:outline-none focus:ring-2 focus:ring-cyan-500/20 focus:border-cyan-500 shadow-xs"
        />

        <div className="absolute right-1.5 top-1/2 -translate-y-1/2 flex items-center gap-1">
          {loading ? (
            <Loader2 className="w-4 h-4 text-slate-400 animate-spin mr-1" />
          ) : (
            <button
              type="button"
              onClick={() => setIsOpen((prev) => !prev)}
              className="p-1 text-slate-400 hover:text-slate-600 rounded-md hover:bg-slate-100"
            >
              <ChevronDown className={`w-4 h-4 transition-transform ${isOpen ? 'rotate-180' : ''}`} />
            </button>
          )}
        </div>
      </div>

      {isOpen && (
        <>
          <div
            className="fixed inset-0 z-40"
            onClick={() => setIsOpen(false)}
          />
          <div className="absolute left-0 right-0 top-full mt-1 z-50 bg-white border border-slate-200 rounded-xl shadow-xl max-h-56 overflow-y-auto p-1 text-xs space-y-0.5">
            <div className="p-1.5 border-b border-slate-100 flex items-center gap-1.5 text-[11px] text-slate-500 sticky top-0 bg-white">
              <Search className="w-3.5 h-3.5 text-slate-400" />
              <input
                type="text"
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                placeholder="Filter Drive folders..."
                className="w-full bg-transparent outline-none text-xs"
                onClick={(e) => e.stopPropagation()}
              />
            </div>

            {filteredFolders.length === 0 ? (
              <div className="p-3 text-center text-[11px] text-slate-400">
                {loading ? 'Fetching folders from Google Drive...' : 'No folders match search'}
              </div>
            ) : (
              filteredFolders.map((f) => {
                const isSelected = value === f;
                return (
                  <button
                    key={f}
                    type="button"
                    onClick={() => {
                      onChange(f);
                      setIsOpen(false);
                    }}
                    className={`w-full flex items-center justify-between px-2.5 py-1.5 rounded-lg text-left transition ${
                      isSelected
                        ? 'bg-cyan-50 text-cyan-800 font-bold'
                        : 'hover:bg-slate-50 text-slate-700'
                    }`}
                  >
                    <span className="flex items-center gap-2 truncate">
                      <Folder className={`w-3.5 h-3.5 ${isSelected ? 'text-cyan-600' : 'text-slate-400'}`} />
                      <span className="font-mono text-xs truncate">{f}</span>
                    </span>
                    {isSelected && <Check className="w-3.5 h-3.5 text-cyan-600 shrink-0 ml-1" />}
                  </button>
                );
              })
            )}
          </div>
        </>
      )}

      {/* Quick selectable chips of available drive folders */}
      {folders.length > 0 && !isOpen && (
        <div className="pt-1">
          <span className="text-[10px] text-slate-400 block mb-1">Available on Drive:</span>
          <div className="flex flex-wrap gap-1">
            {folders.slice(0, 6).map((f) => (
              <button
                key={f}
                type="button"
                onClick={() => onChange(f)}
                className={`text-[10px] font-mono px-2 py-0.5 rounded-md border transition ${
                  value === f
                    ? 'bg-cyan-50 border-cyan-300 text-cyan-700 font-bold'
                    : 'bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100'
                }`}
              >
                {f}
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
