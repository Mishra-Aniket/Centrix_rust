const byId = (id) => document.getElementById(id);
const escapeHtml = (value) => String(value ?? '').replace(/[&<>'"]/g, (char) => ({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[char]));
const formatTime = (value) => new Date(value).toLocaleTimeString([], {hour:'2-digit', minute:'2-digit', second:'2-digit'});

function render(snapshot) {
  const summary = snapshot.summary;
  byId('pending').textContent = summary.pending;
  byId('uploading').textContent = summary.uploading;
  byId('uploaded').textContent = summary.uploaded;
  byId('failed').textContent = summary.failed;
  byId('lectures').textContent = summary.totalLectures;
  byId('updated').textContent = `Snapshot ${formatTime(snapshot.generatedAt)}`;
  byId('connection').textContent = 'LIVE';

  byId('queue').innerHTML = snapshot.queue.length ? snapshot.queue.map((item) => `
    <tr>
      <td><div class="file-name" title="${escapeHtml(item.localFilePath)}">${escapeHtml(item.fileName)}</div><div class="mono">${escapeHtml(item.queueEntryId)}</div></td>
      <td><span class="tag">${escapeHtml(item.fileType)}</span></td>
      <td><span class="tag ${escapeHtml(item.status)}">${escapeHtml(item.status)}</span>${item.lastError ? `<div class="mono">${escapeHtml(item.lastError)}</div>` : ''}</td>
      <td><div class="progress"><b style="width:${Math.min(100, Math.max(0, item.progressPercentage))}%"></b></div><div class="mono">${item.progressPercentage}% · ${formatBytes(item.bytesUploaded)} / ${formatBytes(item.fileSizeBytes)}</div></td>
      <td class="mono">${item.driveFileId ? escapeHtml(item.driveFileId) : 'Waiting'}</td>
      <td class="mono">${formatTime(item.updatedAt)}</td>
    </tr>`).join('') : '<tr><td colspan="6" class="empty">No upload records yet.</td></tr>';

  byId('lectures-list').innerHTML = snapshot.lectures.length ? snapshot.lectures.map((item) => `
    <div class="activity"><div><strong>${escapeHtml(item.fileName)}</strong><span>${escapeHtml(item.centerId)} / ${escapeHtml(item.roomId)} / ${escapeHtml(item.lectureSessionId)}</span></div><span>${escapeHtml(item.status)} · ${item.confidenceScore}% · ${formatTime(item.updatedAt)}</span></div>`).join('') : '<p class="empty">No lecture records yet.</p>';
}

function formatBytes(bytes) {
  if (!bytes) return '0 B';
  const units = ['B', 'MB', 'GB', 'TB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${value.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`;
}

async function refresh() {
  try {
    const response = await fetch('/api/monitor/snapshot', {cache:'no-store'});
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    render(await response.json());
  } catch (error) {
    byId('connection').textContent = 'OFFLINE';
    byId('updated').textContent = error.message;
  }
}
refresh();
setInterval(refresh, 2000);
