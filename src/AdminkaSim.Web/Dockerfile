# adminka-sim.Web — multi-stage image.
#
# Build context: REPO ROOT (so Directory.Build.props / Directory.Packages.props /
# global.json / NuGet.config are reachable). Standalone — no DevExpress feed,
# no PAT secret, no Adminka.* project references.
#
#   docker build -f src/AdminkaSim.Web/Dockerfile -t adminka-sim:dev .
#
# ── build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG GIT_SHA
WORKDIR /src

# Restore first (layer-cached until a csproj/props changes).
COPY global.json Directory.Build.props Directory.Packages.props NuGet.config ./
COPY src/AdminkaSim.Web/AdminkaSim.Web.csproj ./src/AdminkaSim.Web/
RUN dotnet restore src/AdminkaSim.Web/AdminkaSim.Web.csproj

# Cache-bust on a new commit so the source copy + publish re-run.
RUN echo "build ${GIT_SHA:-unknown}" >/dev/null
COPY src/AdminkaSim.Web ./src/AdminkaSim.Web
RUN dotnet publish src/AdminkaSim.Web/AdminkaSim.Web.csproj -c Release -o /app

# ── runtime stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG GIT_SHA
LABEL git.sha=$GIT_SHA
WORKDIR /app
COPY --from=build /app .

# Non-root (the aspnet image ships uid 1654 `app`); chown defensively for any
# startup write (data-protection keys, temp).
RUN chown -R app:app /app
USER app

EXPOSE 8080
ENTRYPOINT ["dotnet", "AdminkaSim.Web.dll"]
