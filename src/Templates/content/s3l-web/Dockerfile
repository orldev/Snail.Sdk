# The application as a container image, built by .github/workflows/image.yml through the shared mcp-image.yml and pushed
# from a version tag as ghcr.io/<owner>/tmplimage.
#
#   docker build -t tmplimage:local .
#
# A build without a tag would be called latest, the name a deployment pulls, and nothing would tell it from a release.
# So a local build is tagged local, and the version built in says what it is: VERSION is 0.0.0-local unless the build
# passes one (the shared workflow passes the release's version from its tag, and 0.0.0-<branch>.<commit> from a
# branch); the image carries it as org.opencontainers.image.version. A local build that should name its commit passes
# it, since the image sees no git:
#
#   docker build -t tmplimage:local --build-arg VERSION=0.0.0-local.$(git rev-parse --short HEAD) .
#
# The SDK is pinned to the band global.json asks for: on the floating sdk:10.0 a newer feature band would one day
# refuse the build. The application listens on 8080 as the image's own user, behind the proxy that terminates TLS.
#
# tini runs as process 1 and starts the application under it. .NET ends on an unhandled exception — a setting that fails
# its check at start, say — with SIGABRT, and process 1 of a container ignores that signal: run as process 1 itself,
# the application spins at a full core and never exits, so nothing restarts it. Under tini it exits with 134.
#if (IsBlazor)
# The stylesheet is built here, never committed: the Tailwind package runs npm and the Tailwind CLI, and the .NET SDK
# image carries no Node of its own, so the build stage takes Node from the official image.
#endif
ARG VERSION=0.0.0-local
#if (IsBlazor)

FROM node:22-bookworm-slim AS node
#endif

FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
ARG VERSION
#if (IsBlazor)
COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s ../lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
    && ln -s ../lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx
#endif
WORKDIR /source
COPY . .
RUN dotnet publish src/Snail.Web.Template.csproj -c Release -o /app -p:UseAppHost=false -p:Version=$VERSION

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG VERSION
LABEL org.opencontainers.image.version=$VERSION
RUN apt-get update \
    && apt-get install --yes --no-install-recommends tini \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
#if (UsesCookie)
RUN mkdir /keys && chown app:app /keys
ENV DataProtection__KeysPath=/keys
#endif
USER app
EXPOSE 8080

ENTRYPOINT ["/usr/bin/tini", "--", "dotnet", "/app/Snail.Web.Template.dll"]
