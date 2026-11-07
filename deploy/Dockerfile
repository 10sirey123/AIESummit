FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY NuGet.Config global.json ./
COPY dependencies/blueshift/packages/ dependencies/blueshift/packages/
COPY AIESummit.Domain/ AIESummit.Domain/
COPY AIESummit.Agents/ AIESummit.Agents/
COPY AIESummit.Api/ AIESummit.Api/
COPY AIESummit.Configuration/ AIESummit.Configuration/
RUN --mount=type=cache,id=aiesummit-nuget,target=/root/.nuget/packages,sharing=locked dotnet publish AIESummit.Api/AIESummit.Api.csproj -c Release -o /out/api --nologo -v:q /clp:ErrorsOnly
RUN --mount=type=cache,id=aiesummit-nuget,target=/root/.nuget/packages,sharing=locked dotnet publish AIESummit.Agents/AIESummit.Agents.csproj -c Release -o /out/packs --nologo -v:q /clp:ErrorsOnly

FROM build AS configuration-build
RUN --mount=type=cache,id=aiesummit-nuget,target=/root/.nuget/packages,sharing=locked dotnet publish AIESummit.Configuration/AIESummit.Configuration.csproj -c Release -o /out/configuration --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl && rm -rf /var/lib/apt/lists/*

FROM runtime AS api
COPY --from=build /out/api .
ENTRYPOINT ["dotnet","AIESummit.Api.dll"]

FROM runtime AS configuration
ADD dependencies/blueshift/cli-linux-x64.tar.gz /cli/
COPY --from=configuration-build /out/configuration /configuration
COPY --from=build /out/packs /packs
COPY deploy/cli/aiesummit.overlay.json /config/aiesummit.overlay.json
ENV BLUESHIFT_CLI_CONFIG=/config/aiesummit.overlay.json
ENTRYPOINT ["dotnet", "/cli/Blueshift.CLI.dll"]

FROM runtime AS worker
ADD dependencies/blueshift/worker-linux-x64.tar.gz ./
COPY --from=build /out/packs /packs
COPY deploy/worker.overlay.json /config/worker.overlay.json
ENTRYPOINT ["dotnet","Blueshift.Agents.Worker.dll","--config","/config/worker.overlay.json","--enqueue-objects","false"]

FROM runtime AS admin
ADD dependencies/blueshift/admin-api-linux-x64.tar.gz ./
COPY --from=build /out/packs /packs
COPY deploy/admin.json ./appsettings.Development.local.json
ENTRYPOINT ["dotnet","Blueshift.Admin.Api.dll"]

FROM nginx:1.27-alpine AS admin-web
COPY dependencies/blueshift/nginx.conf /etc/nginx/conf.d/default.conf
ADD dependencies/blueshift/admin-web.tar.gz /usr/share/nginx/html/
EXPOSE 80




