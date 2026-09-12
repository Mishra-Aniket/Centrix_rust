# Cost Control Strategy

## Goal

**Minimum recurring cloud cost** while handling 500 centers, 10,000–20,000 lectures/day, at high scale.

Key: Keep heavy processing and files at center, cloud only for coordination.

---

## 1. Cost Breakdown Estimate

### Firebase (Monthly)

| Service | Cost Model | Estimated Usage | Cost |
|---------|-----------|-----------------|------|
| **Authentication** | Per user | 10,000 users | Free (< 50K) |
| **Firestore (reads)** | $0.06 per 100K reads | 30M reads/month | $180 |
| **Firestore (writes)** | $0.18 per 100K writes | 10M writes/month | $180 |
| **Firestore (delete)** | $0.02 per 100K deletes | 1M deletes/month | $2 |
| **Cloud Storage (Drive)** | $0.020 per GB | 0 GB (external: Google Drive) | $0 |
| **Hosting (PWA)** | Free tier | < 1 GB | $0 |
| **FCM** | Free | Unlimited | $0 |
| **Cloud Functions** | $0.40 per M invocations | 1M/month | $0.40 |
| **Total** | | | **~$362/month** |

### Google Drive

| Item | Cost |
|------|------|
| Organizational storage | Negotiated enterprise rate |
| Direct upload (no bandwidth cost through our server) | Free |
| Total | **Included in org contract** |

### Monitoring & Logging

| Service | Cost |
|---------|------|
| Cloud Logging | Free tier: 50 GB/month |
| Cloud Monitoring | Free tier: standard metrics |
| Error Reporting | Free |
| Total | **Free** |

### Domain & Certificates

| Item | Cost |
|------|------|
| Domain (example.com) | $12/year |
| SSL Certificate (Firebase auto) | Free |
| CDN (Firebase Hosting) | Free |
| Total | **$1/month** |

### Total Monthly Recurring Cost

```
Firebase:          $362/month
Google Drive:      (included in org contract)
Monitoring:        $0
Domain:            $1/month
───────────────────────────────
TOTAL:             ~$360/month
```

**For comparison**: A single VPS with video processing would cost $200–1000+/month.

---

## 2. Cost Optimization Strategies

### A. Firestore Costs

#### Reading Efficiency

❌ **Inefficient**: Fetch all lectures, filter in code
```python
all_lectures = db.collection('lectures').stream()
today_lectures = [l for l in all_lectures if l.date == today]
# Reads: millions
```

✅ **Efficient**: Use queries with indexes
```python
query = db.collection('lectures').where('centerId', '==', center_id) \
                                  .where('date', '==', today)
today_lectures = list(query.stream())
# Reads: only matching documents
```

#### Caching

Use **local SQLite cache** to avoid repeated reads:
```csharp
// First center load: read from Firestore
if (!db.TimetableCache.Any()) {
    var timetable = await firestore.GetTimetable(centerId, date);
    db.TimetableCache.AddRange(timetable);
}

// Subsequent loads: read from SQLite (much faster, zero cost)
var local_timetable = db.TimetableCache.Where(t => t.date == date);
```

#### Batch Operations

✅ Write multiple documents in one batch:
```csharp
var batch = firestore.Batch();
foreach (var lecture in lectures) {
    batch.Set(doc(lectureId), lecture);
}
batch.Commit(); // One write operation per doc (not multiplied)
```

#### Pagination

Limit document size and use cursors:
```
GET /api/lectures?centerId=C-001&limit=50&cursor=abc123

Reads: 50 docs (not 10,000)
```

#### TTL (Time-to-Live)

Archive old records to reduce read operations:
```
auditLog older than 2 years → deleted
Old lecture metadata → archived (separate collection)
```

### B. Cloud Functions Costs

#### Minimize Invocations

❌ Trigger on every data write (expensive)
```
Firestore trigger: on every lecture write
→ Function runs 20,000 times/day
```

✅ Batch or periodic only
```
Cloud Scheduler: Once per hour
→ Process batch of changes
→ Function runs 24 times/day (not 20,000)
```

#### Efficient Code

- Keep execution time short (< 1 second)
- Avoid retry loops (use idempotency)
- Minimize external API calls

### C. Bandwidth Costs

#### No Video Through Cloud

❌ Center → Our Cloud Server → Google Drive (costs bandwidth)

✅ Center → Google Drive Direct (costs nothing)

Savings: $0.20/GB * 100 TB/month = $20,000/month!

#### Static Content Delivery

- Firebase Hosting handles CDN automatically (free)
- Use gzip compression (reduce 1MB → 200KB)
- Cache aggressive (30 days for static files)

### D. Storage Costs

#### No Media Storage

Store only metadata in Firestore:
- `lectureSessionId`, `batchId`, `status`, `driveFileId`
- NOT: video binary, thumbnails, transcripts

Videos stay on Google Drive (your org's contract).

#### Database Cleanup

Delete transient data:
```sql
-- Delete old upload queue entries (after success)
DELETE FROM upload_queue 
WHERE status = 'UPLOADED' 
  AND updated_at < DATE('now', '-30 days');
```

---

## 3. Firestore Pricing Deep Dive

### Read Operations ($0.06 per 100K)

| Query | Operations |
|-------|-----------|
| Get single document | 1 read |
| Get 50 documents in list | 50 reads |
| Query with filter (=, <, etc.) | 1 read per matching doc |
| Get non-existent document | 1 read (counts) |

**Example**:
```
Center has 500 lectures on day X.
Reviewer opens dashboard → Load reviews.
Query: where centerId = C-001 AND date = 2026-09-02

Firestore reads: 500 documents
Cost: 500 reads * ($0.06 / 100K) = $0.000003
```

### Write Operations ($0.18 per 100K)

| Operation | Cost |
|-----------|------|
| Create document | 1 write |
| Update document | 1 write |
| Delete document | 1 write |
| Batch write (up to 500 ops) | Cost per doc (not per batch) |

**Example**:
```
Center uploads 20 lectures/day.
Each lecture creates 1 document in Firestore (after upload complete).
Daily writes: 20 * 500 centers = 10,000 writes/day
Monthly: 300,000 writes

Cost: 300K * ($0.18 / 100K) = $540/month
```

### Delete Operations ($0.02 per 100K)

Use deletion sparingly. Archive instead of delete.

---

## 4. Optimization Checklist

- [ ] All queries indexed properly
- [ ] Pagination applied (no fetching all records)
- [ ] Local caching used (timetable, config)
- [ ] Cloud Functions triggered only when needed
- [ ] Video never uploaded to our cloud
- [ ] Old data archived/deleted (retention policy)
- [ ] Batch operations used (not individual writes)
- [ ] Error handling prevents retries
- [ ] Compression applied to network data
- [ ] Rate limiting prevents abuse

---

## 5. Cost Monitoring

### Budget Alert

Set up Firestore budget alert:
```
Firebase Console → Project Settings → Usage
→ Set budget: $500/month
→ Alert if exceeds 90%
```

### Tracking

Monthly report:
```
Firestore reads: 30M
Firestore writes: 10M
Cost: $360

Breakdown by center:
C-001: 1M reads, $6/month
C-002: 2M reads, $12/month
...
```

### Cost Anomaly Detection

If cost suddenly spikes:
- Check Firestore console for unusual queries
- Review Cloud Functions execution logs
- Investigate device behavior (is it querying excessively?)
- Implement circuit breaker or rate limits

---

## 6. Scaling Costs

### 100 Centers (Phase Testing)

```
Firestore reads: 3M/month → $18
Firestore writes: 1M/month → $18
Total: ~$40/month
```

### 500 Centers (Full Scale)

```
Firestore reads: 30M/month → $180
Firestore writes: 10M/month → $180
Total: ~$360/month
```

### 1000 Centers (Future)

```
Firestore reads: 60M/month → $360
Firestore writes: 20M/month → $360
Total: ~$720/month
```

**Conclusion**: Firestore scales linearly and remains affordable even at massive scale.

---

## 7. Cost Comparison with Alternatives

### Option A: Our Design (Proposed)
- **Monthly**: $360 (Firestore, hosting, no video cost)
- **Bandwidth**: $0 (videos go direct to Drive)
- **Storage**: $0 (videos on Drive, metadata in Firestore)
- **Total**: ~$360/month for 500 centers

### Option B: Dedicated Servers
- **Monthly**: $300/server * 2 (redundancy) = $600/month (minimum)
- **Bandwidth**: $0.20/GB * 100TB/month = $20,000/month
- **Storage**: $0.023/GB * 100TB = $2,300/month
- **Total**: ~$23,000/month

### Option C: AWS S3 + EC2
- **Monthly**: $500/month (EC2)
- **Bandwidth**: $0.02/GB * 100TB = $2,000/month (cheaper than data center)
- **Storage**: S3: $0.023/GB * 100TB = $2,300/month
- **Total**: ~$4,800/month

### Conclusion

**Our design (Option A) is 63x cheaper than a dedicated server and 13x cheaper than AWS.**

---

## 8. Revenue Impact

### Break-even Analysis

Assuming cost per center:
```
Setup cost: ~$5,000 (development, training)
Monthly recurring: ~$1/month per center ($360 / 500)

If charging $50/month per center:
- Revenue: $25,000/month
- Costs: $360/month
- Profit: $24,640/month
- Payback: 1 week
```

### Pricing Strategy Options

1. **Per-Center License**: $50–100/month (profitable)
2. **Per-Lecture**: $0.01–0.05 per recording (usage-based, scales with revenue)
3. **Enterprise**: Negotiate custom rates for 100+ centers

---

## 9. Cost Optimization Roadmap

### Phase 1 (Months 1–3): Setup
- Set up budget monitoring
- Optimize Firestore queries
- Implement caching strategy

### Phase 2 (Months 4–6): Scale
- Monitor costs as users grow
- Implement archival policy
- Test load at 500 centers

### Phase 3 (Months 7–12): Maintenance
- Monthly cost audits
- Identify inefficiencies
- Implement improvements
- Plan for 1000+ center scale

---

**Status**: Cost Control Strategy Phase  
**Last Updated**: 2026-09-02  
**Version**: 1.0.0-alpha
