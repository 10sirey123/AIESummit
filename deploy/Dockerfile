FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY AIESummit.Domain/ AIESummit.Domain/
COPY AIESummit.Agents/ AIESummit.Agents/
COPY AIESummit.Api/ AIESummit.Api/
COPY external/blueshift/Blueshift.Domain/ external/blueshift/Blueshift.Domain/
COPY external/blueshift/Blueshift.Agents/ external/blueshift/Blueshift.Agents/
COPY external/blueshift/Blueshift.Agents.Worker/ external/blueshift/Blueshift.Agents.Worker/
COPY external/blueshift/Blueshift.Admin.Api/ external/blueshift/Blueshift.Admin.Api/
RUN --mount=type=cache,id=aiesummit-nuget,target=/root/.nuget/packages,sharing=locked dotnet publish AIESummit.Api/AIESummit.Api.csproj -c Release -o /out/api --nologo -v:q /clp:ErrorsOnly
RUN --mount=type=cache,id=aiesummit-nuget,target=/root/.nuget/packages,sharing=locked dotnet publish AIESummit.Agents/AIESummit.Agents.csproj -c Release -o /out/packs --nologo -v:q /clp:ErrorsOnly
RUN --mount=type=cache,id=aiesummit-nuget,target=/root/.nuget/packages,sharing=locked dotnet publish external/blueshift/Blueshift.Agents.Worker/Blueshift.Agents.Worker.csproj -c Release -o /out/worker --nologo -v:q /clp:ErrorsOnly
RUN --mount=type=cache,id=aiesummit-nuget,target=/root/.nuget/packages,sharing=locked dotnet publish external/blueshift/Blueshift.Admin.Api/Blueshift.Admin.Api.csproj -c Release -o /out/admin /p:SkipAdminFrontendInstall=true /p:SkipAdminFrontendBuild=true --nologo -v:q /clp:ErrorsOnly

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl && rm -rf /var/lib/apt/lists/*

FROM runtime AS api
COPY --from=build /out/api .
ENTRYPOINT ["dotnet","AIESummit.Api.dll"]

FROM runtime AS worker
COPY --from=build /out/worker .
COPY --from=build /out/packs /packs
COPY deploy/worker.overlay.json /config/worker.overlay.json
ENTRYPOINT ["dotnet","Blueshift.Agents.Worker.dll","--config","/config/worker.overlay.json","--enqueue-objects","false"]

FROM runtime AS admin
COPY --from=build /out/admin .
COPY --from=build /out/packs /packs
COPY deploy/admin.json ./appsettings.Development.local.json
ENTRYPOINT ["dotnet","Blueshift.Admin.Api.dll"]




