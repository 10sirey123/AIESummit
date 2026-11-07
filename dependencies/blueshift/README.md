# Bundled Blueshift dependencies

This directory replaces the private Git submodule. AIESummit users need access only to the AIESummit repository, not the Blueshift repository or a private package feed. These binary artifacts are intentionally tracked in ordinary Git; no Git LFS setup is required.

The bundle is built from the `net10` source revision recorded in [manifest.json](manifest.json). The Domain and Agents libraries are local NuGet packages, preserving their public transitive dependency declarations. [NuGet.Config](../../NuGet.Config) maps `10123.Blueshift.*` exclusively to `packages/`; other packages restore from public NuGet. Application package references pin the bundled version exactly. This is not a fully offline installation: public NuGet/npm packages, container images and configured model services still require network access.

| Artifact | Consumer |
| --- | --- |
| `packages/10123.Blueshift.Domain.*.nupkg` | AIESummit.Domain |
| `packages/10123.Blueshift.Agents.*.nupkg` | AIESummit.Agents |
| `worker-linux-x64.tar.gz` | Shared worker host and all its publish dependencies |
| `admin-api-linux-x64.tar.gz` | Shared Admin API and all its publish dependencies |
| `admin-web.tar.gz` | Built Admin website |
| `cli-linux-x64.tar.gz` | One-off configuration/repository CLI host |
| `cli-linux-x64.tar.gz` | One-off configuration/repository CLI host |
| `bootstrap-admin-auth.ps1` | Local operator settings/token generation |
| `nginx.conf` | Admin website proxy configuration |

The .NET libraries target `net10.0` and support host builds on Windows. The published hosts target **Linux x64**, are framework-dependent, and run in the Docker .NET 10 runtime image; they are not Windows executables or self-contained .NET installations. Docker Desktop must use Linux containers on an x64 machine. The frontend runs in Nginx. Configuration, credentials and application packs are supplied separately by AIESummit's deployment files. No Blueshift application source checkout is used during normal build/startup.

Run `./scripts/verify-dependencies.ps1` from the repository root to check every artifact's size and SHA-256 against the manifest. Startup runs that check before generating credentials or building containers. A checksum detects changes relative to this manifest; it is not an independent publisher signature.

Validated November 7, 2026: restore/build from an exported Git checkout without framework source and with an empty NuGet cache; bundle checksums after checkout; rejection of a deliberately modified disposable artifact; Docker builds/startup for all services; and the complete provider-backed smoke test (synthetic upload 2). One existing nullable warning remains in `AIESummit.Agents/SplitStep.cs`. Browser interaction/layout were not rechecked during this dependency change.

## Refreshing the bundle (maintainers only)

Only maintainers refreshing Blueshift need private source access. Use a separate clean checkout at the intended `net10` revision. Do not restore a submodule or add framework source to AIESummit. For the current revision, from a maintainer's chosen parent directory:

```powershell
git clone --branch net10 https://github.com/10123IO/10.Blueshift.123.git Blueshift-source
git -C Blueshift-source checkout cc69aa4da64bf313ec7120711624816c7f9d4254
```

From the AIESummit repository root, pass that checkout's absolute path:

```powershell
./scripts/package-blueshift.ps1 -SourcePath C:\src\Blueshift-source
./scripts/verify-dependencies.ps1
```

The packaging script rejects a mismatched revision or dirty checkout, builds in Linux containers using [Blueshift.Bundle.Dockerfile](../../deploy/Blueshift.Bundle.Dockerfile), exports to an ignored staging directory, then copies the eight expected artifacts and records hashes/provenance in the manifest. It does not fetch or advance a branch automatically. It builds NuGet libraries, the worker, Admin API and CLI Linux hosts and the Admin website from the same source revision. Sources and credentials are not published in the bundle. Compiler warnings remain visible in the build output.

For an upgrade, pass explicit `-ExpectedCommit` and a new unique `-Version`, update the exact package versions in both application project files, and deliberately remove obsolete `.nupkg` files. Never replace a package with different bytes under a previously used version: NuGet caches packages by identity/version. Check the generated package dependencies as well as the manifest, then build the solution and all Docker targets, start the stack and run the synthetic provider-backed smoke test. Commit the packages, archives, manifest, corresponding reference changes and updated documentation together. Each artifact must stay below GitHub's 100 MiB file limit.

## Provenance and distribution

Blueshift is authored by 10.123 Ventures; source repository: [10123IO/10.Blueshift.123](https://github.com/10123IO/10.Blueshift.123). The source revision and branch are provenance, not a consumer access requirement. Distribution of this bundle in AIESummit was explicitly requested by the repository owner. No license file was found at the source repository root at this revision; do not infer that Blueshift is open source. Third-party components retain their own licenses. NuGet package metadata/dependency groups and the hosts' `.deps.json` files identify their dependencies. Do not publish secrets, real documents, source maps or local authentication configuration in a refreshed bundle.

## Meta, API-key Gemini and Admin compatibility patches

The current bundle (`10.0.0-aiesummit.cc69aa4.meta1`) is built from the recorded upstream commit plus three patches listed under `sourcePatches` in the manifest. `meta-provider.patch` captures the sibling framework's `10.0.0-preview.meta.1` source changes: Meta enum/factory registration and the native Responses adapter. It is a snapshot of local source changes, not a newer published upstream commit. `gemini-api-key.patch` adds official Developer API key/JSON-schema support without changing the Vertex service-account path and records the actual selected model on Gemini responses. `admin-apiinfo-secrets.patch` makes APIInfo credentials write-only editable in Admin; generic reads and broadcasts continue to exclude them. The packaging Dockerfile normalizes source line endings and applies these tracked patches to a clean source context; normal consumers use only the resulting binaries. Patch bytes are preserved by `.gitattributes`. See [page analysis](../../docs/page-analysis.md) and [Meta setup](../../docs/meta-analysis.md).

The bundled CLI is used by [configuration seeding](../../docs/configuration-seeding.md); its native framework seed commands are exposed through `scripts/admin.ps1`. It uses the same recorded source revision, patches and version as the other hosts.

The bundled CLI is used by [configuration seeding](../../docs/configuration-seeding.md); its native framework seed commands are exposed through `scripts/admin.ps1`. It uses the same recorded source revision, patches and version as the other hosts.
