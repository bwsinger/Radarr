# Runtime observation trial

Starts at **±15% of catalog runtime**, inclusive at the boundary, in both
directions. All new-file import evaluations are observed after the existing
media probe, without a movie tag requirement. Existing-library scans are
excluded. Import previews can also produce observations; they do not prove a
successful import. The specification always accepts: matching, quality checks,
manual overrides, failure handling and payload handling remain unchanged.

`DevRuntimeReview` INFO lines contain one JSON record with UTC time, movie and
external IDs, title/year, download ID/title, source path, edition, multiple-file
context, expected/measured minutes, signed percentage deviation, tolerance,
comparison and hypothetical `wouldHold`. Missing/nonpositive runtimes are
`unavailable`. Multiple video files and named editions still log their measured
comparison, but are exempt from `wouldHold`: catalog runtime cannot determine
the runtime of an individual part or a particular cut. Direct manual imports
also log (`stage=manual-import`), without affecting overrides. Manual reprocessing
and imports, and automatic scans of an individual file, cannot establish the
full directory's file count: this is logged
as null with a `file-count-unknown` exemption, rather than assuming a single
video. No additional probing,
network lookup or schema change is introduced.

From the media-server repository:

```sh
python3 scripts/radarr_runtime_review.py --save logs/mserver/radarr-runtime-observations.log
python3 scripts/test_radarr_runtime_review.py
```

The first command compares ±5/10/15/20/25%, lists files near/outside the current
boundary and saves observations before normal Radarr logs rotate away. Rerun it
periodically while collecting evidence; saved records are merged. It uses the
latest measurement per download/movie/path, recovering matching known directory
context when the newest observation has unknown context. Context time is
retained separately; the archive keeps raw measurement/context representatives
so later merges are independent of input order. Malformed records are skipped
with a diagnostic. Retries and
duplicate info/debug log lines do not inflate download counts. Files whose runtime is never probed or
whose movie is never identified cannot produce this observation. Empty logs
mean no evidence, not zero false positives.

**Reevaluate the range from logging before adding enforcement.** Review all
outside-range files, plus a sample of passing files around and away from the
boundary. Record correct identity, edition/alternate-cut evidence, catalog
errors and multipart context; correlate download IDs with import history and
existing import rejections (including samples), which the runtime log does not
classify.
Compare false-positive and missed-mismatch rates at candidate tolerances.
Runtime deviation alone cannot label correctness or establish an optimal
range. Treat the existing-library baseline separately from new downloads and
do not describe hypothetical holds as actual failed imports.

The older `check_runtime_review.py` is the historical, opt-in 60% experiment,
not this trial's production policy.
