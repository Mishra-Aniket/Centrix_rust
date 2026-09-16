import { useState } from 'react';
import { Building2, Plus, RefreshCw, Trash2, AlertCircle, PlayCircle } from 'lucide-react';
import type { RoomOverview } from '../types';
import { ActionButton, Card, Field, Modal, Pill, inputClass } from '../ui';

interface CenterScreenProps {
  overview: RoomOverview[];
  busy: boolean;
  loading: boolean;
  onRefresh: () => void;
  onSaveRoom: (room: { name: string; url: string; apiKey: string; roomId: string }) => void;
  onDeleteRoom: (name: string) => void;
}

export function CenterScreen({ overview, busy, loading, onRefresh, onSaveRoom, onDeleteRoom }: CenterScreenProps) {
  const [addOpen, setAddOpen] = useState(false);
  const [form, setForm] = useState({ name: '', url: '', apiKey: '', roomId: '' });

  const liveCount = overview.filter((r) => r.live).length;
  const totalUploading = overview.reduce((sum, r) => sum + r.uploading, 0);
  const totalFailed = overview.reduce((sum, r) => sum + r.failed, 0);
  const totalMissing = overview.reduce((sum, r) => sum + r.missingCount, 0);

  const handleSave = () => {
    if (!form.name.trim() || !form.url.trim()) return;
    onSaveRoom({
      name: form.name.trim(),
      url: form.url.trim(),
      apiKey: form.apiKey.trim(),
      roomId: form.roomId.trim()
    });
    setForm({ name: '', url: '', apiKey: '', roomId: '' });
    setAddOpen(false);
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between px-1">
        <div>
          <h2 className="text-sm font-bold text-slate-900">Center Overview</h2>
          <p className="text-[11px] text-slate-500">All room agents on one screen</p>
        </div>
        <button
          onClick={onRefresh}
          disabled={loading}
          className="p-2 rounded-xl bg-white border border-slate-200 text-slate-600 hover:text-slate-900 active:scale-95 transition disabled:opacity-40 shadow-xs"
          title="Refresh"
        >
          <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin text-cyan-600' : ''}`} />
        </button>
      </div>

      {/* Center-wide totals */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3.5">
        <div className="bg-white border border-slate-200/90 rounded-2xl p-3.5 text-center shadow-xs">
          <p className="text-xs text-slate-500 font-medium">Live</p>
          <p className={`text-xl font-bold ${liveCount === overview.length && overview.length > 0 ? 'text-emerald-700' : 'text-amber-600'}`}>
            {liveCount}/{overview.length}
          </p>
        </div>
        <div className="bg-white border border-slate-200/90 rounded-2xl p-3.5 text-center shadow-xs">
          <p className="text-xs text-slate-500 font-medium">Uploading</p>
          <p className="text-xl font-bold text-cyan-700">{totalUploading}</p>
        </div>
        <div className="bg-white border border-slate-200/90 rounded-2xl p-3.5 text-center shadow-xs">
          <p className="text-xs text-slate-500 font-medium">Failed</p>
          <p className={`text-xl font-bold ${totalFailed > 0 ? 'text-red-600' : 'text-slate-800'}`}>{totalFailed}</p>
        </div>
        <div className="bg-white border border-slate-200/90 rounded-2xl p-3.5 text-center shadow-xs">
          <p className="text-xs text-slate-500 font-medium">Missing</p>
          <p className={`text-xl font-bold ${totalMissing > 0 ? 'text-amber-600' : 'text-slate-800'}`}>{totalMissing}</p>
        </div>
      </div>

      <ActionButton tone="primary" onClick={() => setAddOpen(true)} className="w-full py-2.5">
        <Plus className="w-4 h-4" />
        Add a room agent
      </ActionButton>

      {overview.length === 0 ? (
        <Card className="text-center text-xs text-slate-500 space-y-2 p-8">
          <Building2 className="w-10 h-10 text-slate-400 mx-auto" />
          <p className="font-bold text-slate-800 text-sm">No other rooms linked yet</p>
          <p className="text-xs leading-relaxed text-slate-500 max-w-md mx-auto">
            Install the agent on each classroom PC (using the same package with a unique Room ID), then
            add that PC's URL and API key here — you can monitor all rooms in one place.
          </p>
        </Card>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {overview.map((room) => (
            <div key={room.name} className="bg-white border border-slate-200/90 rounded-2xl p-3.5 space-y-2 shadow-sm">
              <div className="flex items-center justify-between gap-2">
                <div className="flex items-center gap-2 min-w-0">
                  <span className={`w-2.5 h-2.5 rounded-full shrink-0 ${room.live ? 'bg-emerald-500' : 'bg-red-500'}`} />
                  <span className="text-xs font-bold text-slate-900 truncate">Room {room.roomId || room.name}</span>
                  <Pill tone={room.live ? 'emerald' : 'red'}>{room.live ? 'Live' : 'Down'}</Pill>
                </div>
                <button
                  onClick={() => onDeleteRoom(room.name)}
                  disabled={busy}
                  className="p-1.5 rounded-lg bg-red-50 border border-red-200 text-red-600 hover:bg-red-100 active:scale-95 transition disabled:opacity-40 shrink-0 shadow-xs"
                  title={`Remove ${room.name}`}
                >
                  <Trash2 className="w-3.5 h-3.5" />
                </button>
              </div>

              {room.live ? (
                <>
                  <div className="grid grid-cols-4 gap-1.5 text-center text-[10px]">
                    <div className="bg-slate-50 border border-slate-100 rounded-lg py-1.5">
                      <p className="text-slate-500">Up</p>
                      <p className="font-bold text-cyan-700">{room.uploading + room.pending}</p>
                    </div>
                    <div className="bg-slate-50 border border-slate-100 rounded-lg py-1.5">
                      <p className="text-slate-500">Done</p>
                      <p className="font-bold text-emerald-700">{room.uploaded}</p>
                    </div>
                    <div className="bg-slate-50 border border-slate-100 rounded-lg py-1.5">
                      <p className="text-slate-500">Failed</p>
                      <p className={`font-bold ${room.failed > 0 ? 'text-red-600' : 'text-slate-700'}`}>{room.failed}</p>
                    </div>
                    <div className="bg-slate-50 border border-slate-100 rounded-lg py-1.5">
                      <p className="text-slate-500">Missing</p>
                      <p className={`font-bold ${room.missingCount > 0 ? 'text-amber-600' : 'text-slate-700'}`}>{room.missingCount}</p>
                    </div>
                  </div>
                  {room.missingItems.map((m, idx) => (
                    <p key={idx} className="text-[10px] text-amber-800 flex items-center gap-1 truncate">
                      <AlertCircle className="w-3 h-3 shrink-0 text-amber-600" />
                      {m.time} · {m.batch} / {m.subject}
                    </p>
                  ))}
                </>
              ) : (
                <p className="text-[10px] text-red-600 flex items-center gap-1.5">
                  <AlertCircle className="w-3 h-3 shrink-0" />
                  {room.error || 'Agent not reachable'}
                </p>
              )}
            </div>
          ))}
        </div>
      )}

      <Modal
        open={addOpen}
        title="Add Room Agent"
        icon={<PlayCircle className="w-4 h-4 text-cyan-400" />}
        onClose={() => setAddOpen(false)}
      >
        <div className="space-y-3 text-xs">
          <Field label="Room name (e.g. Room 604)">
            <input
              type="text"
              value={form.name}
              onChange={(e) => setForm((p) => ({ ...p, name: e.target.value }))}
              placeholder="Room 604"
              className={inputClass}
            />
          </Field>
          <Field label="Agent URL">
            <input
              type="url"
              value={form.url}
              onChange={(e) => setForm((p) => ({ ...p, url: e.target.value }))}
              placeholder="http://192.168.1.50:5200"
              className={inputClass}
              autoCapitalize="off"
            />
          </Field>
          <Field label="That PC's API key (DASHBOARD-ACCESS.txt)">
            <input
              type="password"
              value={form.apiKey}
              onChange={(e) => setForm((p) => ({ ...p, apiKey: e.target.value }))}
              className={inputClass}
              autoCapitalize="off"
            />
          </Field>
          <Field label="Room ID">
            <input
              type="text"
              value={form.roomId}
              onChange={(e) => setForm((p) => ({ ...p, roomId: e.target.value }))}
              placeholder="604"
              className={inputClass}
            />
          </Field>
        </div>
        <ActionButton tone="primary" onClick={handleSave} disabled={busy || !form.name.trim() || !form.url.trim()} className="w-full py-2.5">
          Save room
        </ActionButton>
      </Modal>
    </div>
  );
}
