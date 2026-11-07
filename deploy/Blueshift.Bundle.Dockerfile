# Maintainer-only build: context is a separate, clean Blueshift source checkout.
FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS dotnet-build
ARG BUNDLE_VERSION
ARG SOURCE_COMMIT
WORKDIR /src
COPY Blueshift.Domain/ Blueshift.Domain/
COPY Blueshift.Agents/ Blueshift.Agents/
COPY Blueshift.Agents.Worker/ Blueshift.Agents.Worker/
COPY Blueshift.Admin.Api/ Blueshift.Admin.Api/
COPY --from=aiesummit deploy/patches/ /patches/
RUN find Blueshift.Domain Blueshift.Agents Blueshift.Admin.Api /patches -type f \( -name '*.cs' -o -name '*.patch' \) -exec sed -i 's/\r$//' {} + && for patch in /patches/*.patch; do git apply "$patch" || exit 1; done
RUN --mount=type=cache,id=aiesummit-bundle-nuget,target=/root/.nuget/packages,sharing=locked dotnet pack Blueshift.Domain/Blueshift.Domain.csproj -c Release -o /bundle/packages -p:Version=$BUNDLE_VERSION -p:RepositoryCommit=$SOURCE_COMMIT -p:DebugType=None --nologo
RUN --mount=type=cache,id=aiesummit-bundle-nuget,target=/root/.nuget/packages,sharing=locked dotnet pack Blueshift.Agents/Blueshift.Agents.csproj -c Release -o /bundle/packages -p:Version=$BUNDLE_VERSION -p:RepositoryCommit=$SOURCE_COMMIT -p:DebugType=None --nologo
RUN --mount=type=cache,id=aiesummit-bundle-nuget,target=/root/.nuget/packages,sharing=locked dotnet publish Blueshift.Agents.Worker/Blueshift.Agents.Worker.csproj -c Release -r linux-x64 --self-contained false -o /worker -p:Version=$BUNDLE_VERSION -p:RepositoryCommit=$SOURCE_COMMIT -p:DebugType=None -p:UseAppHost=false --nologo
RUN --mount=type=cache,id=aiesummit-bundle-nuget,target=/root/.nuget/packages,sharing=locked dotnet publish Blueshift.Admin.Api/Blueshift.Admin.Api.csproj -c Release -r linux-x64 --self-contained false -o /admin -p:Version=$BUNDLE_VERSION -p:RepositoryCommit=$SOURCE_COMMIT -p:DebugType=None -p:UseAppHost=false -p:SkipAdminFrontendInstall=true -p:SkipAdminFrontendBuild=true --nologo
RUN tar -czf /bundle/worker-linux-x64.tar.gz -C /worker . && tar -czf /bundle/admin-api-linux-x64.tar.gz -C /admin .

FROM dotnet-build AS cli-build
COPY Blueshift.CLI/ Blueshift.CLI/
RUN --mount=type=cache,id=aiesummit-bundle-nuget,target=/root/.nuget/packages,sharing=locked dotnet publish Blueshift.CLI/Blueshift.CLI.csproj -c Release -r linux-x64 --self-contained false -o /cli -p:Version=$BUNDLE_VERSION -p:RepositoryCommit=$SOURCE_COMMIT -p:DebugType=None -p:UseAppHost=false --nologo
RUN tar -czf /cli-linux-x64.tar.gz -C /cli .

FROM scratch AS cli-bundle
COPY --from=cli-build /cli-linux-x64.tar.gz /

FROM node:22-alpine AS frontend-build
WORKDIR /src
COPY Blueshift.Admin/ ./
RUN npm ci --no-audit --no-fund && npm run build --workspace=blueshift-admin-sdk && npm run build
RUN mkdir /bundle && tar -czf /bundle/admin-web.tar.gz -C dist .

FROM scratch AS bundle
COPY --from=cli-build /cli-linux-x64.tar.gz /
COPY --from=dotnet-build /bundle/ /
COPY --from=frontend-build /bundle/ /
COPY scripts/bootstrap-admin-auth.ps1 /bootstrap-admin-auth.ps1
COPY Blueshift.Admin/nginx.conf /nginx.conf
