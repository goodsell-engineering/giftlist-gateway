# Dev image for Gateway.Host (GL-13).
#
# Build context is this repo's root (see giftlist-devenv/docker-compose.yml, which sets
# `context: ../giftlist-gateway`). At *runtime* compose bind-mounts that same directory over /src, so
# everything copied in here is replaced; the COPY exists only so the image is self-contained
# enough to inspect without the mount.
#
# THERE IS NO `dotnet restore` IN THIS IMAGE, and its absence is the point. Since the GL-25 split
# this project consumes BuildingBlocks(.Infrastructure) — and, where it applies, another
# service's *.Contracts — as NuGet packages from the local folder feed, and a `docker build` can
# only see its own context: ../local-feed sits outside this repo, so a restore here could not
# resolve them no matter how it were written (ARCHITECTURE.md "Making the local feed visible to
# containers"). The documented answer is to restore at container *runtime*, where compose has
# mounted the feed as a second volume at /feed and the repo's nuget.config points at it (GL-26,
# via a symlink created below). Until GL-29 adds that mount, this image builds and starts, and
# `dotnet watch` fails its first restore. Do not "fix" that by baking a restore back in here —
# the only way to make it work is to widen the build context to the parent of every repo, which
# is the secondary approach that section deliberately does not make the default.
#
# The cost of having no restore layer: the first `dotnet watch` inside a fresh container restores
# from cold instead of from a warm image layer. Measured in seconds, paid once per image.
FROM mcr.microsoft.com/dotnet/sdk:10.0

WORKDIR /src
COPY . .

# inotify is unreliable for bind-mounted source on macOS/Windows Docker Desktop, so dotnet watch
# needs polling. Suppress the launch-browser attempt, which has nothing to open in a container.
# ASPNETCORE_URLS is supplied by compose so Kestrel binds 0.0.0.0:8080 inside the container.
ENV DOTNET_USE_POLLING_FILE_WATCHER=1 \
    DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=1

# GL-26: this repo's nuget.config names the local feed as "../local-feed", resolved relative to
# the config file's own directory (this repo's root, /src at runtime), so the same literal value
# also works unmodified on the host, where local-feed really is a sibling directory. Compose
# mounts the feed at /feed instead of at a sibling path (ARCHITECTURE.md "Making the local feed
# visible to containers"), so this symlink creates the missing sibling. It lives outside /src on
# purpose: the runtime bind mount replaces /src wholesale, which would hide anything placed under
# it here. The target doesn't exist yet at image build time — that's fine, a symlink resolves
# when it's read, not when it's created.
RUN ln -s /feed /local-feed

WORKDIR /src/src/Gateway.Host

EXPOSE 8080

ENTRYPOINT ["dotnet", "watch", "run", "--no-launch-profile"]
