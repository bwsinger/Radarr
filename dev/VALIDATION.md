# Development patch validation — 2026-09-20

- Focused changed-path tests: 77 passed; no failures.
- Broader import/download filter: 907 passed, 10 skipped; one upstream baseline failure.
- Independent correctness, data-safety, and deployment reviews completed;
  findings fixed and reviewed again until clean.
- Image started with an isolated copied database, no network, no published
  ports, and read-only media/download mounts. Ping and authenticated system
  status succeeded with the matching upstream release.
- Live Knight/Jentry payloads disappeared before smoke replay; filename
  behavior is verified by regression tests, not a replay of those payloads.

Run from the repository root (matching SDK from `global.json`):

```sh
DOTNET_PROCESSOR_COUNT=2 "$HOME/.local/share/arr-dev-dotnet/dotnet" test \
  src/NzbDrone.Core.Test/Radarr.Core.Test.csproj -c Release -f net8.0 \
  -p:SolutionDir="$PWD/src/" -p:RuntimeIdentifiers=linux-x64 \
  -p:UseSharedCompilation=false \
  --filter 'FullyQualifiedName~MediaFiles.MovieImport|FullyQualifiedName~Download'
```

The broad filter also matches DecisionEngineTests.DownloadDecisionMakerFixture.
Its `broken_report_shouldnt_blowup_the_process` test expects three error log
messages and observes zero. The same failure was reproduced in a separate,
untouched worktree at upstream tag `v6.4.4.10685`. This patch does not change
that decision-engine path.

## Benefit instrumentation — 2026-09-21

- History and failure-recovery fixtures: 13 passed, zero failures.
- Successful imports receive a versioned denominator marker; custom score
  recovery receives a separate durable marker and post-insert Info log.
- No Radarr parsing-assisted success is claimed: the maintained Radarr patches
  protect quality and recover rejected downloads.

## Dependency security update — 2026-10-23

- 168 focused import, failure recovery, parsing/history tests passed; zero failures.
- Full self-contained console and updater publishes each resolve their Mono
  dependencies in one graph. This prevents separately published platform DLLs
  from downgrading application dependencies.
- Isolated image startup with copied SQLite configuration passed authenticated
  system status and preserved production library counts. No network or media
  mounts were available to the test instance.
- Two independent review agents cleared packaging after permission, license-file
  retention and dependency-graph corrections.
- ImageSharp remains on the current 3.1.x release: fixed 4.1.2 requires a license.
- Runtime updated to 8.0.32 and PKCS to 8.0.2. Existing ImageSharp audit
  warnings remain visible. For this explicitly accepted partial update, publish
  used `WarningsNotAsErrors=NU1902;NU1903`; no audit suppression is committed.

- Preserved the deployed download-recovery and runtime-observation patches.
  Broader validation passed 1,174 tests with 15 platform skips and the same
  previously documented upstream `broken_report_shouldnt_blowup_the_process`
  failure. Both independent reviewers found no blockers in the preserved code.
