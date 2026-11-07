# App API contract

This directory maintains the public upload API as OpenAPI 3.0.3 JSON. Read [Components/AGENTS.md](Components/AGENTS.md) for schema design and [Paths/AGENTS.md](Paths/AGENTS.md) for operations, parameters and responses. Compare contract changes with [UploadEndpoints.cs](../UploadEndpoints.cs) and [Program.cs](../Program.cs), which implement the actual HTTP behavior.

The builder assembles a static document. It does not generate controllers, minimal endpoints, C# DTOs or browser clients, and the API does not automatically derive this contract from endpoint metadata.

## Files and ownership

| File or directory | Role |
| --- | --- |
| `template.json` | OpenAPI version, API information, tags, server URL and security scheme |
| `Paths/*.json` | Root maps of path strings to complete Path Item objects |
| `Components/*.json` | Root maps of schema names to Schema Objects |
| `build.mjs` | Dependency-free Node assembly script using filesystem built-ins |
| `../wwwroot/openapi.json` | Generated document served publicly as `/openapi.json` |

The current fragments are `Paths/uploads.json` and `Components/uploads.json`. Keep JSON sources authoritative and rebuild the output after changes. Direct edits to `wwwroot/openapi.json` will be overwritten by the builder.

Use JSON, not YAML or JSON with comments. Before adding a fragment, inventory existing names to avoid duplicate schemas or splitting one path across files. Unlike the Horus reference, this builder does not recurse into tag or label subdirectories.

## Exact builder behavior

`build.mjs` resolves every input and output relative to `import.meta.url`. It reads `template.json`, then collects `Paths/` and `Components/` independently.

For each collection it reads the immediate directory entries, sorts their names, skips names that do not end with lowercase `.json`, parses the selected files and merges each file's top-level object entries into one object. The builder uses each entry's key, not the filename, as the resulting path or schema identity.

It rejects a key already present in that collection with `Duplicate OpenAPI entry <key>`. This means two files cannot contribute separate verbs to the same path. Put the complete Path Item under one key in one fragment. Schema and path collections are separate namespaces, so their duplicate checks are independent.

The directory read does not request file-type information. A directory whose name happens to end in `.json` is selected and fails when read as a file. Ordinary nested directories are skipped, not traversed. `.JSON` does not match the lowercase suffix test.

The collected objects replace `spec.paths` and `spec.components.schemas`. Existing path or schema entries placed only in the template are therefore overwritten. Other template fields, including `components.securitySchemes`, remain. Keep the template's `components` object present because the script assigns its `schemas` property directly.

The final document is written with two-space JSON indentation and a trailing newline to `AIESummit.Api/wwwroot/openapi.json`. Sorting filenames helps stable assembly order. Keys within each fragment retain the order produced by JSON parsing and object enumeration. The script does not alphabetize every operation, property or schema field.

Parsing and duplicate checks complete before the final write call. A failed build can leave the previously generated file in place, so its presence does not prove the current sources built successfully. Check command exit status and review the regenerated diff.

## What assembly does not validate

The builder does not perform the following checks.

- Resolve or dereference `$ref` targets.
- Validate fragments against the OpenAPI schema.
- Check operation IDs for uniqueness or verify tags exist in the template.
- Validate path parameters against route placeholders.
- Check that required properties exist or that example values match schemas.
- Compare schemas, status codes or media types with the running endpoints.
- Generate code or enforce constraints on requests at runtime.

`JSON.parse` checks JSON syntax, but it does not reject duplicate member names within the same JSON object before the later value replaces the earlier one. The builder's explicit duplicate protection is for merged top-level entries across files, not a complete duplicate-key lint.

Use an available OpenAPI validator when appropriate and state exactly which tool and rules ran. There is no local npm package script, bundled Spectral configuration or code-generation command declared by this directory or `AIESummit.Api.csproj`. Do not document `npm run ...` commands or a sibling repository's installed binary as guaranteed AIESummit tooling.

## Build and review

From the repository root, run the actual entrypoint.

```powershell
node AIESummit.Api/ApiSpec/build.mjs
git diff -- AIESummit.Api/ApiSpec AIESummit.Api/wwwroot/openapi.json
git diff --check
```

From another working directory, provide a correct relative or absolute path to `build.mjs`. Once Node has located the script, its internal file resolution is independent of the current working directory. For example, when already inside `AIESummit.Api/ApiSpec`, use `node ./build.mjs`.

The .NET project contains no target that runs this script automatically. A successful `dotnet build` or Docker publish can therefore ship a stale generated document if regeneration was skipped. Review source and generated changes together before handing off an API contract change.

When the local app is running, fetching `http://localhost:18080/openapi.json` verifies which document it is actually serving. It does not establish semantic correctness or authenticated endpoint behavior. Rebuild/restart the appropriate image when verifying a containerized source change rather than assuming the host output has been mounted into the container.

## OpenAPI 3.0.3 conventions

Keep `openapi: "3.0.3"` unless intentionally migrating the document and validating consumer compatibility. Use 3.0 schema conventions, such as `type: "string"` with `nullable: true`, rather than introducing 3.1-only type arrays or treating the document as unrestricted modern JSON Schema.

The current server URL is `/`, so paths include their full `/api/v1/...` prefix and resolve against the host serving the contract. Do not duplicate `/api/v1` in both server and path without deliberately updating the composition.

References to local schemas use `#/components/schemas/SchemaName`. There is no file-path resolution phase in the builder. An external or file reference is preserved as text and becomes a consumer responsibility. Prefer the established internal reference pattern for this compact contract.

Describe actual response projections instead of serializing domain entities into schemas. The public upload contract omits owner identifiers, original source bytes, provider configuration and internal prompts. `Upload`, `Document` and `ImageReference` describe endpoint output, not complete EF entities.

`required` means a property is present, not necessarily nonnull. Current `Upload.error` is required and nullable. Empty arrays are valid for documents and images where the API can return them. Do not add `minItems: 1` merely because a successful sample happens to contain results.

Use string enums matching the API's serialized public states. Current upload states are `queued`, `splitting`, `ready` and `failed`, not the numeric or named internal processing enums. Keep one-based page ranges distinct from provider zero-based OCR indexes.

The multipart upload body is an object with required `file`, whose schema is string/binary. The field name matches the C# `IFormFile file` parameter. The body itself is not a bare binary string. Extracted image responses use binary schemas under their supported image media types rather than base64 JSON strings.

Reuse schemas where the actual representation is shared. Do not inherit Horus's blanket requirement to create an `XInput`/`allOf` pair for every response-only object. This API currently has a multipart file input and output projections, not a CRUD editor for every entity. A new writable DTO must expose only fields the server accepts and authorizes.

## Security and visibility

The template declares HTTP bearer scheme `FirebaseBearer`, described as a Firebase ID token, and applies it globally through `security`. Current upload operations inherit that declaration. No authentication token or key belongs in a specification example.

The document is publicly readable, so its descriptions and examples must be safe to share. Do not include live filenames, extracted documents, owner subjects, signed URLs, provider keys or Admin tokens. Use synthetic examples if examples are added.

OpenAPI security is documentation, not enforcement. Preserve the `/api/v1` authorization group and owner predicates in actual code. A new operation that intentionally permits anonymous access needs both an explicit contract decision and matching runtime behavior. Do not add `security: []` to work around an interactive client's missing token.

Public `/health` and `/firebase-config` endpoints and the separate BlueShift Admin API are not currently included in this upload contract. Their absence does not mean they do not exist. If adding them, give each its actual security semantics rather than inheriting Firebase bearer requirements accidentally.

## Change workflow

1. Read the implemented endpoint, relevant browser consumer and existing fragment before designing a change.
2. Decide the actual wire representation, status codes, media types, authorization and owner boundaries. A schema edit alone cannot add behavior.
3. Update or add a top-level JSON fragment in the current flat directories. Keep every path in one fragment and use unique operation IDs.
4. Update template tags when introducing a new operation tag. Existing operation IDs use lower camel case, such as `listUploads` and `createUpload`, and do not generate C# method names.
5. Use precise summaries and descriptions. Document asynchronous acceptance and polling instead of implying the POST returns completed OCR results.
6. Update endpoint implementation and browser behavior only when included in the task's authorized scope. For documentation-only work, record discrepancies without silently changing application code.
7. Rebuild the generated output, inspect the full diff and check all references and parameters. Apply a semantic validator if one is available and appropriate.
8. Verify representative requests against the running API and distinguish successful assembly, schema validation and runtime acceptance in the report.

## Current contract details to preserve

The upload POST returns HTTP 202 with an ID and `Location` pointing to the detail route. A successful POST does not mean OCR has succeeded. The documented 30-page maximum is enforced after the OCR response is received, not by a synchronous PDF page-count preflight.

Listing returns at most 50 owned uploads, newest first, with an optional exclusive `before` cursor. The schema's positive minimum documents intended cursor use. The endpoint does not automatically enforce every OpenAPI numeric bound, so compare binding and application behavior before claiming a specific validation response for nonpositive values.

Detail and image reads return 404 when the requested upload relationship is unavailable to the account. Authentication failures are distinct from owner isolation. Image content can be PNG, JPEG, WebP or GIF because extracted images have a broader supported MIME set than initial uploads.

The upload operation describes 400 for file validation, 413 for transport body limits and 415 for unsupported file signatures. The endpoint uses problem responses for some validation failures, while current error responses in the specification are description-only. Do not invent a shared JSON error shape unless it matches all the responses to which it is attached.

## Verification checklist

- All fragments parse as JSON and are located directly in the directory the builder reads.
- Every root path/schema key has one owner, and all operation IDs are unique.
- Internal `$ref` names exist in the assembled `components.schemas` map.
- Route parameter names match their path placeholders and are marked required for path parameters.
- Response property casing, required/nullable behavior and enum values match actual serialization.
- Request fields and media types match endpoint binding, including multipart `file`.
- Security declarations match runtime authentication, while owner checks are verified with two accounts.
- Generated output is rebuilt and reviewed, and the served version is checked when runtime verification is relevant.
- Tests and validators are reported accurately. The live smoke uses paid configured providers and does not cover every contract error case.

## Sources and Horus translation

Current sources are [build.mjs](build.mjs), [template.json](template.json), [Components/uploads.json](Components/uploads.json), [Paths/uploads.json](Paths/uploads.json), [AIESummit.Api.csproj](../AIESummit.Api.csproj) and [UploadEndpoints.cs](../UploadEndpoints.cs).

The complete reference `../../../horus/DUTai.Api/ApiSpec/AGENTS.md` motivates separate JSON components and paths, reusable schemas, multipart correctness and clear descriptions. Its recursive folder structure, `build-openapi.js`, generated-controller assumptions, blanket composition rules and complex-component approval workflow do not describe this repository. Use the self-contained workflow above and the user's authorized scope.
