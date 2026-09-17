import {
  CheckCircle2,
  ShieldCheck,
  Sparkles,
} from 'lucide-react';
import { Modal } from '../ui';

function YoutubeIcon({ className = 'w-5 h-5' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="currentColor">
      <path d="M23.498 6.186a3.016 3.016 0 0 0-2.122-2.136C19.505 3.545 12 3.545 12 3.545s-7.505 0-9.377.505A3.017 3.017 0 0 0 .502 6.186C0 8.07 0 12 0 12s0 3.93.502 5.814a3.016 3.016 0 0 0 2.122 2.136c1.871.505 9.376.505 9.376.505s7.505 0 9.377-.505a3.015 3.015 0 0 0 2.122-2.136C24 15.93 24 12 24 12s0-3.93-.502-5.814zM9.545 15.568V8.432L15.818 12l-6.273 3.568z" />
    </svg>
  );
}

export function YouTubeGuideModal({
  open,
  onClose,
}: {
  open: boolean;
  onClose: () => void;
}) {
  return (
    <Modal
      open={open}
      title="Direct YouTube Publishing — How It Works"
      icon={<YoutubeIcon className="w-5 h-5 text-rose-600" />}
      maxWidth="max-w-2xl"
      onClose={onClose}
    >
      <div className="space-y-4 text-xs">
        {/* Banner */}
        <div className="p-4 rounded-2xl bg-gradient-to-r from-rose-50 to-red-50/60 border border-rose-200/80 space-y-1.5">
          <div className="flex items-center gap-2 text-rose-700 font-bold text-sm">
            <Sparkles className="w-4 h-4 text-rose-600" />
            <span>1-Click Direct YouTube Publishing for Lectures</span>
          </div>
          <p className="text-slate-600 text-xs leading-relaxed">
            Centrix allows center managers to publish recorded classroom lectures directly to YouTube instantly without manual downloads, title typing, or browser uploads.
          </p>
        </div>

        {/* 4-Step Visual Workflow */}
        <div className="space-y-2.5">
          <h4 className="font-bold text-slate-900 text-xs uppercase tracking-wider">How It Works Step-by-Step</h4>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
            {/* Step 1 */}
            <div className="bg-white border border-slate-200/80 rounded-2xl p-3.5 space-y-1.5 shadow-2xs">
              <div className="flex items-center gap-2 text-slate-900 font-bold text-xs">
                <span className="w-6 h-6 rounded-lg bg-rose-100 text-rose-700 flex items-center justify-center font-mono font-bold text-xs">
                  1
                </span>
                <span>Select & Tap "Publish"</span>
              </div>
              <p className="text-[11px] text-slate-500 leading-relaxed">
                On any lecture in the <strong>Review</strong> hub, click <em>"Publish unlisted"</em>. The agent queues the raw local video for upload.
              </p>
            </div>

            {/* Step 2 */}
            <div className="bg-white border border-slate-200/80 rounded-2xl p-3.5 space-y-1.5 shadow-2xs">
              <div className="flex items-center gap-2 text-slate-900 font-bold text-xs">
                <span className="w-6 h-6 rounded-lg bg-rose-100 text-rose-700 flex items-center justify-center font-mono font-bold text-xs">
                  2
                </span>
                <span>Auto Metadata & Tags</span>
              </div>
              <p className="text-[11px] text-slate-500 leading-relaxed">
                Video Title is automatically set to <code className="bg-slate-100 px-1 py-0.5 rounded text-[10px]">{`{Batch} — {Subject} | {Date}`}</code>, description with room details, and Category set to <em>Education</em>.
              </p>
            </div>

            {/* Step 3 */}
            <div className="bg-white border border-slate-200/80 rounded-2xl p-3.5 space-y-1.5 shadow-2xs">
              <div className="flex items-center gap-2 text-slate-900 font-bold text-xs">
                <span className="w-6 h-6 rounded-lg bg-rose-100 text-rose-700 flex items-center justify-center font-mono font-bold text-xs">
                  3
                </span>
                <span>Unlisted Privacy by Default</span>
              </div>
              <p className="text-[11px] text-slate-500 leading-relaxed">
                All lectures are uploaded as <strong>Unlisted</strong>. The video will NOT appear in public YouTube search or channel recommendations.
              </p>
            </div>

            {/* Step 4 */}
            <div className="bg-white border border-slate-200/80 rounded-2xl p-3.5 space-y-1.5 shadow-2xs">
              <div className="flex items-center gap-2 text-slate-900 font-bold text-xs">
                <span className="w-6 h-6 rounded-lg bg-rose-100 text-rose-700 flex items-center justify-center font-mono font-bold text-xs">
                  4
                </span>
                <span>Instant Link & Player</span>
              </div>
              <p className="text-[11px] text-slate-500 leading-relaxed">
                As soon as upload finishes, the YouTube Video ID and link (<code className="bg-slate-100 px-1 py-0.5 rounded text-[10px]">https://youtu.be/...</code>) are saved with an in-app player.
              </p>
            </div>
          </div>
        </div>

        {/* Security & Control Info */}
        <div className="bg-slate-50 border border-slate-200/90 rounded-2xl p-3.5 space-y-2">
          <h4 className="font-bold text-slate-900 text-xs flex items-center gap-1.5">
            <ShieldCheck className="w-4 h-4 text-emerald-600" />
            <span>Privacy & Operations Limits</span>
          </h4>
          <ul className="space-y-1.5 text-[11px] text-slate-600">
            <li className="flex items-start gap-2">
              <CheckCircle2 className="w-3.5 h-3.5 text-emerald-600 shrink-0 mt-0.5" />
              <span><strong>1-Click Make Private:</strong> If you ever need to revoke access, click <em>"Make Private"</em> and the video privacy is locked instantly.</span>
            </li>
            <li className="flex items-start gap-2">
              <CheckCircle2 className="w-3.5 h-3.5 text-emerald-600 shrink-0 mt-0.5" />
              <span><strong>YouTube API Quota Guard:</strong> Centrix caps daily operations (default 10) to prevent hitting Google API quota limits.</span>
            </li>
            <li className="flex items-start gap-2">
              <CheckCircle2 className="w-3.5 h-3.5 text-emerald-600 shrink-0 mt-0.5" />
              <span><strong>Zero Extra Bandwidth Lag:</strong> Uploads run in the background worker queue and do not interrupt Google Drive sync or classroom recording.</span>
            </li>
          </ul>
        </div>

        <div className="pt-2 flex justify-end">
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 bg-slate-900 hover:bg-slate-800 text-white font-semibold text-xs rounded-xl transition active:scale-95 cursor-pointer"
          >
            Got It!
          </button>
        </div>
      </div>
    </Modal>
  );
}
