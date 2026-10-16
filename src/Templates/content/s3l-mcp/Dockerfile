# The server as a container image, built by .github/workflows/image.yml through the shared mcp-image.yml and pushed
# from a version tag as ghcr.io/<owner>/s3l-tmplshort-mcp.
#
#   docker build -t s3l-tmplshort-mcp:local .
#if (UsesStdio)
#   docker run -i --rm s3l-tmplshort-mcp:local
#endif
#
# A build without a tag would be called latest, the name a deployment pulls, and nothing would tell it from a release.
# So a local build is tagged local, and the version built in says what it is: VERSION is 0.0.0-local unless the build
# passes one (the shared workflow passes the release's version from its tag, and 0.0.0-<branch>.<commit> from a
# branch), the server reports it in initialize and the image carries it as org.opencontainers.image.version. A local
# build that should name its commit passes it, since the image sees no git:
#
#   docker build -t s3l-tmplshort-mcp:local --build-arg VERSION=0.0.0-local.$(git rev-parse --short HEAD) .
#
#if (UsesHttp)
# Over HTTP the server listens on every interface of the container (McpHttpHost.CreateContainerBuilder, on the port of
# SNAIL_MCP_TMPLSHORT_URL): the published port is the boundary, and a browser's request is still refused.
#
#endif
# The SDK is pinned to the band global.json asks for: on the floating sdk:10.0 a newer feature band would one day
# refuse the build.
#if (native)
# The server is native: published for linux-x64 the SDK compiles it ahead of time (Native AOT), which needs clang and
# zlib in the build stage, and the image runs the one executable on runtime-deps, with no .NET runtime in it — a
# smaller image and a faster start.
ARG VERSION=0.0.0-local

FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
ARG VERSION
RUN apt-get update \
    && apt-get install --yes --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /source
COPY . .
RUN dotnet publish src/Snail.MCP.Template.csproj -c Release -r linux-x64 -o /app -p:Version=$VERSION

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
ARG VERSION
LABEL org.opencontainers.image.version=$VERSION
WORKDIR /app
COPY --from=build /app .
USER app
#if (UsesHttp)
EXPOSE 9300
#endif

ENTRYPOINT ["/app/Snail.MCP.Template"]
#else
# The server is framework-dependent: published without Native AOT onto the ASP.NET runtime.
ARG VERSION=0.0.0-local

FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
ARG VERSION
WORKDIR /source
COPY . .
RUN dotnet publish src/Snail.MCP.Template.csproj -c Release -o /app -p:PublishAot=false -p:UseAppHost=false -p:Version=$VERSION

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG VERSION
LABEL org.opencontainers.image.version=$VERSION
WORKDIR /app
COPY --from=build /app .
USER app
#if (UsesHttp)
EXPOSE 9300
#endif

ENTRYPOINT ["dotnet", "/app/Snail.MCP.Template.dll"]
#endif
