# Keep runtime, UI, native dependencies and Core source on the same upstream release.
FROM lscr.io/linuxserver/radarr@sha256:c960f2b52ec6542dbe6707c5a21e696a7c74fd8b17997454f4d10a55dacee133
ARG DEV_REVISION
LABEL org.opencontainers.image.source="https://github.com/bwsinger/Radarr" \
      org.opencontainers.image.revision="${DEV_REVISION}" \
      media-server.custom-features="import-minimum-score"
COPY .dev-build/Radarr.Core.dll /app/radarr/bin/Radarr.Core.dll
