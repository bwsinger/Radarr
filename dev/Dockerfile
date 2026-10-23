# Keep runtime, UI, native dependencies and Core source on the same upstream release.
FROM lscr.io/linuxserver/radarr@sha256:c960f2b52ec6542dbe6707c5a21e696a7c74fd8b17997454f4d10a55dacee133
ARG DEV_REVISION
LABEL org.opencontainers.image.source="https://github.com/bwsinger/Radarr" \
      org.opencontainers.image.revision="${DEV_REVISION}" \
      media-server.custom-features="import-minimum-score"
# Preserve the matching upstream UI while replacing the full self-contained runtime.
RUN find /app/radarr/bin -mindepth 1 -maxdepth 1 ! -name UI ! -name ffprobe ! -name LICENSE ! -name LICENSE.md -exec rm -rf {} +
COPY .dev-build/ /app/radarr/bin/
RUN chmod -R a+rX /app/radarr/bin
