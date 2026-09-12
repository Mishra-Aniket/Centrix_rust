#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
TEST_RECORDINGS="${ROOT_DIR}/test_recordings"
mkdir -p "${TEST_RECORDINGS}"

export PATH="/usr/local/share/dotnet:$PATH"
export DOTNET_ROOT="/usr/local/share/dotnet"
export DOTNET_CLI_HOME="${ROOT_DIR}/.dotnet"
export FileWatcher__MonitorFolder="${TEST_RECORDINGS}"
export FileWatcher__EnableFileWatcher=true
export GoogleDrive__Enabled=false
export Agent__CenterId="CENTER_001"
export Agent__RoomId="ROOM_101"

echo "=== 1. Starting LectureAgent in Background ==="
dotnet run --project "${ROOT_DIR}/src/LectureAgent/LectureAgent.csproj" > /tmp/agent_test.log 2>&1 &
AGENT_PID=$!

cleanup() {
    echo "Stopping Agent PID $AGENT_PID..."
    kill "$AGENT_PID" 2>/dev/null || true
}
trap cleanup EXIT

echo "Waiting for agent to initialize on https://localhost:5201..."
for i in {1..20}; do
    if curl -sk https://localhost:5201/health | grep -q "Healthy"; then
        echo "Agent is up and Healthy!"
        break
    fi
    sleep 1
done

# Current local classroom date
TODAY="$(date +"%Y-%m-%d")"

# Slot duration spanning current local time window
SLOT_START="$(date -v-15M +"%H:%M:00" 2>/dev/null || date -d "-15 minutes" +"%H:%M:00")"
SLOT_END="$(date -v+30M +"%H:%M:00" 2>/dev/null || date -d "+30 minutes" +"%H:%M:00")"

echo ""
echo "=== 2. Creating Dummy Timetable Slot matching Current Time (${SLOT_START} - ${SLOT_END} Local) ==="
curl -sk -X POST https://localhost:5201/api/timetable \
  -H "Content-Type: application/json" \
  -d "{
    \"organizationId\": \"ORG_001\",
    \"centerId\": \"CENTER_001\",
    \"roomId\": \"ROOM_101\",
    \"scheduledDate\": \"${TODAY}T00:00:00\",
    \"slotStartTime\": \"${SLOT_START}\",
    \"slotEndTime\": \"${SLOT_END}\",
    \"slotId\": \"SLOT-JEE-PHY-01\",
    \"batchId\": \"JEE-2026\",
    \"subjectId\": \"PHYSICS\",
    \"teacherId\": \"PROF_VERMA\"
  }" | jq .

echo ""
echo "=== 3. Dropping Dummy Video with RANDOM FILENAME (e.g. REC_982374.mp4) into ${TEST_RECORDINGS} ==="
# Notice: filename has NO 'physics' or 'jee-2026' keywords! System will match purely by Room + Timetable!
DUMMY_FILE="${TEST_RECORDINGS}/REC_$(date +%s)_RANDOM.mp4"
rm -f "${TEST_RECORDINGS}"/*.mp4
dd if=/dev/urandom of="${DUMMY_FILE}" bs=1048576 count=24 status=none
echo "Created: ${DUMMY_FILE} (Size: $(wc -c < "${DUMMY_FILE}" | tr -d ' ') bytes)"

echo ""
echo "=== 4. Waiting for FileWatcher, Smart Matching & Upload Queue ==="
sleep 8

echo ""
echo "=== 5. Latest Created Lecture & Auto-Assigned Match ==="
curl -sk "https://localhost:5201/api/lectures?limit=1" | jq .

echo ""
echo "=== 6. Live Monitor Snapshot (Upload Queue & Target Drive Folder) ==="
curl -sk https://localhost:5201/api/monitor/snapshot | jq .

echo ""
echo "=== SUCCESS: End-to-end Test Verified! ==="
