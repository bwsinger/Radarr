# Radarr media-server development fork

This checkout backs a production media server. Root operational rules:
`/home/bradley/code/media-server-v2/AGENTS.md`.
Do not restart/recreate production containers until the prescribed Tautulli
check succeeds with no active streams (unless the user accepts the impact).
Enable and verify maintenance mode before downtime, and disable/verify it
after services and sanity checks pass. Build and test before cutover.

## Upstream and local branch

- Upstream remote: `upstream` → https://github.com/Radarr/Radarr
- Personal fork remote: `origin` → https://github.com/bwsinger/Radarr
- Maintained branch: `media-server-main`, initially based on `v6.4.4.10685`.
- Runtime/UI base is pinned in `dev/Dockerfile` to that same upstream release.

**Prefer upstream implementations.** On every upstream update, look for
equivalent functionality in merged upstream changes. If upstream covers the
behavior and safety requirements below, favor it over the manually maintained
implementation: run/adapt the regression tests, then remove redundant local
code. Do not retain duplicate checks or competing recovery paths. If upstream
only covers part of a feature, keep and document only the remaining difference.

## Custom features to preserve until upstream replaces them

- Enforce the quality profile's minimum custom-format score against actual
  downloaded-file formats during automatic import, including missing content
  and quality-tier upgrades. Preserve explicit manual-import overrides.
- Automatically fail/blocklist and use the existing replacement-search pipeline
  for completed, application-grabbed torrents whose import results are entirely
  safe, mapped below-minimum rejections. Acceptable equal-score/non-upgrade
  downloads, unknown files and mixed results must not be auto-failed.
- Remove only the torrent entry for these custom automatic failures. Retain
  its payload, because another torrent or the media library can share it.
  Retained rejected payloads require separate, deliberate cleanup. Never remove
  an existing library file as a side effect of rejecting a download.
- Completed, application-grabbed single-video torrents may also recover from
  `ZeroFilledVideo`: a failed sample probe plus a stable-sized MKV/MP4/M4V
  with zero-filled 64 KiB header, middle and tail samples. Require exact grabbed
  identity and safe import results; retain payload and library files. Generic
  probe errors, incomplete downloads, mixed results and ambiguous packs remain
  manual. Radarr direct-file imports with unknown inventory also remain manual;
  folder imports with one known video are eligible. Record `DevRecovery=zero-filled-video` and
  `DevBenefit outcome=zero-filled-recovery`, separately from score metrics.
- Retry cleanup only for a current client item whose latest durable failure
  carries `DevRecovery=minimum-format-score` or `DevRecovery=zero-filled-video`.
  Respect failed-removal settings
  and native seed readiness on retries; retain payload and never replay failure
  history, blocklisting or replacement searches. New grabs/imports supersede it.

- Episode-number/title parsing does not apply to Radarr (movies).
- Observe all new-download runtime comparisons at ±15% using existing media
  info; log `DevRuntimeReview` without rejecting or changing identity matching.
  See `dev/runtime-review.md`. Tune the range from reviewed passing/flagged
  observations before considering enforcement; preserve cut/multipart context.
- Benefit metrics use existing History.Data: `DevMetricsVersion=1` on successful
  new-download imports, and `DevRecovery=minimum-format-score` on custom score
  failures. Log `DevBenefit` after saving those failures. These are recovery
  events, not successful imports or proof of a regression. Radarr currently has
  no parsing-assisted import feature, so it must not claim parsing credit.
- Read counts and percentages with `media-server-v2/scripts/arr_dev_benefits.py`.
  Only instrumented, retained history is comparable; do not invent old tags or
  treat unmeasured regressions as zero. Preserve the history keys on upgrades.

## Build, test and deployment

- Exact .NET SDK version is in `global.json`. This server's side-by-side SDKs
  are at `$HOME/.local/share/arr-dev-dotnet/dotnet`.
- `dev/build.sh` publishes the complete patched application/runtime and updater,
  then builds `radarr-dev:local`.
  Set `DOTNET_BIN` to use another compatible SDK installation.
- `docker compose build` only packages the existing publish output; it does not compile
  source. Always run `dev/build.sh` after source changes before recreating.
- The image retains the matching official UI and media probe from the pinned
  LinuxServer image, and replaces its managed/native .NET runtime with the full
  self-contained publish. Console and updater must each reference the Mono
  platform project so NuGet resolves a single dependency graph per executable.
  Do not merge a separately published platform tree over the application.
- Run the relevant existing Core test fixtures for all changed paths, including
  failure recovery, rejection specifications and (Sonarr) episode aggregation.
- Every Radarr code change requires at least two independent review agents
  critiquing correctness, data safety and regression coverage. The overseeing
  agent resolves findings and repeats independent review of refinements until
  satisfied that no material issues remain.
- Stack integration lives in `media-server-v2/compose/mserver/radarr-dev.yml`.
  Existing production URL, host port and internal `radarr` DNS alias remain
  available. Dev configuration is separate under `appdata/radarr-dev`.
- Back up current SQLite state and config before replacing a live instance.
  Never run two instances against the same writable configuration directory.
- Do not blindly pull a newer major branch or mix a newer Core assembly with
  the old runtime/UI. Update the source release, pinned image and assembly
  version together; test migrations against a backup before production.
- Rollback after any dev imports must use a stopped copy of the latest dev
  database (same upstream schema/release) or reconcile the library. The original
  pre-cutover database can reference files that dev has since replaced.
- Do not commit appdata, credentials, build outputs or production databases.

Push changes to `origin` (the bwsinger fork); `remote.pushDefault=origin`.
Use feature PRs into `media-server-main`. Fetch official releases from
`upstream`, retaining the pinned source/runtime compatibility checks above.
