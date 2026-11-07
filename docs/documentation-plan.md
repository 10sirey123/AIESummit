# Documentation implementation plan

Each numbered task owns one guidance file. Authors must read the current AIESummit sources and the named Horus example, then verify framework contracts against external/blueshift without editing that submodule. Horus describes legacy APIs in several areas. Explain current equivalents and migration differences explicitly. Files must contain substantial operational guidance, examples where appropriate, failure modes, verification, and source references. Directory AGENTS.md files link their focused companion files so they are discoverable. Examples of future work must be labeled and must not claim current implementation.

1. `AGENTS.md` — repository map, reading routes and verification. Current evidence: README.md, compose.yaml, AIESummit.slnx. Horus reference: `../horus/AGENTS.md`. Status: complete; reviewed.
2. `AIESummit.Domain/AGENTS.md` — entity design and registration. Current evidence: AIESummit.Domain/Entities.cs. Horus reference: `../horus/Horus.Domain/Entities/AGENTS.md`. Status: complete; reviewed.
3. `AIESummit.Domain/DisplayMetadata.AGENTS.md` — property metadata and connector choices. Current evidence: Entities.cs and framework metadata/catalog reader. Horus reference: `../horus/Horus.Domain/Entities/AGENTS.md`. Status: complete; reviewed.
4. `AIESummit.Domain/Relationships.AGENTS.md` — connected entity lists and EF relationships. Current evidence: Entities.cs and framework ForeignTypes resolver. Horus reference: `../horus/Horus.Domain/Entities/AGENTS.md`. Status: complete; reviewed.
5. `AIESummit.Domain/IAmLLMCapable.AGENTS.md` — modern LLM subject contract and hydration. Current evidence: Entities.cs, SplitStep.cs, framework ILlmCapable. Horus reference: `../horus/Horus.Domain/Entities/IAmLLMCapable.agents.md`. Status: complete; reviewed.
6. `AIESummit.Domain/IHaveChildren.AGENTS.md` — ownership, children and deletion. Current evidence: Entities.cs, framework children interfaces. Horus reference: `../horus/Horus.Domain/Entities/IHaveChildren.agents.md`. Status: complete; reviewed.
7. `AIESummit.Domain/IHaveUniqueTag.AGENTS.md` — stable tags and uniqueness. Current evidence: Bootstrap.cs, SplitStep.cs, framework tag interface. Horus reference: `../horus/Horus.Domain/LLMSteps/AGENTS.md`. Status: complete; reviewed.
8. `AIESummit.Domain/Persistence.AGENTS.md` — schema evolution and prompt persistence. Current evidence: AIESummitDb in Entities.cs, Bootstrap.cs. Horus reference: `../horus/Horus.Domain/Entities/migrations.AGENTS.md`. Status: complete; reviewed.
9. `AIESummit.Domain/Files.AGENTS.md` — page/image ownership and storage. Current evidence: Entities.cs, OcrAgent.cs, UploadEndpoints.cs. Horus reference: `../horus/Voiston.Agents/Containers/PageImageContainer/AGENTS.md`. Status: complete; reviewed.
10. `AIESummit.Agents/AGENTS.md` — worker contracts and activation. Current evidence: all AIESummit.Agents source. Horus reference: `../horus/Voiston.Agents/AGENTS.md`. Status: complete; reviewed.
11. `AIESummit.Agents/OcrAgent.AGENTS.md` — OCR adapter and page persistence. Current evidence: OcrAgent.cs. Horus reference: `../horus/Voiston.Agents/Containers/PageImageContainer/AGENTS.md`. Status: complete; reviewed.
12. `AIESummit.Agents/SplitStep.AGENTS.md` — code-defined steps and registry. Current evidence: SplitStep.cs. Horus reference: `../horus/Horus.Domain/LLMSteps/AGENTS.md`. Status: complete; reviewed.
13. `AIESummit.Agents/Metadata.AGENTS.md` — structured response validation and atomic application. Current evidence: SplitResult, PageRange, SplitPolicy. Horus reference: `../horus/Voiston.Agents/Concrete/LLM/AGENTS.md`. Status: complete; reviewed.
14. `AIESummit.Agents/QueueRecovery.AGENTS.md` — queue eligibility recovery and retries. Current evidence: UploadQueueRecovery.cs, worker.overlay.json. Horus reference: `../horus/Voiston.AgentHostCore/AGENTS.md`. Status: complete; reviewed.
15. `AIESummit.Agents/Telemetry.AGENTS.md` — execution correlation and Admin evidence. Current evidence: UploadExecutionContext.cs, compose.yaml. Horus reference: `../horus/Voiston.Agents/Observability/AGENTS.md`. Status: complete; reviewed.
16. `AIESummit.Api/AGENTS.md` — API architecture and change workflow. Current evidence: Program.cs, UploadEndpoints.cs. Horus reference: `../horus/DUTai.Api/AGENTS.md`. Status: complete; reviewed.
17. `AIESummit.Api/Authentication.AGENTS.md` — Firebase authentication and ownership. Current evidence: Program.cs, app.js, firebase.json. Horus reference: `../horus/DUTai.Api/Configuration/AGENTS.md`. Status: complete; reviewed.
18. `AIESummit.Api/UploadEndpoints.AGENTS.md` — endpoint validation projections and isolation. Current evidence: UploadEndpoints.cs, API schemas. Horus reference: `../horus/DUTai.Api/Controllers/AGENTS.md`. Status: complete; reviewed.
19. `AIESummit.Api/Bootstrap.AGENTS.md` — idempotent seeds and APIInfo. Current evidence: Bootstrap.cs, .env.example. Horus reference: `../horus/Horus.Domain/Configuration/AGENTS.md`. Status: complete; reviewed.
20. `AIESummit.Api/ApiSpec/AGENTS.md` — contract build and validation. Current evidence: build.mjs, template.json. Horus reference: `../horus/DUTai.Api/ApiSpec/AGENTS.md`. Status: complete; reviewed.
21. `AIESummit.Api/ApiSpec/Components/AGENTS.md` — DTO schema design. Current evidence: Components/uploads.json, UploadEndpoints.cs. Horus reference: `../horus/DUTai.Api/ApiSpec/Components/AGENTS.md`. Status: complete; reviewed.
22. `AIESummit.Api/ApiSpec/Paths/AGENTS.md` — paths verbs responses and auth. Current evidence: Paths/uploads.json, UploadEndpoints.cs. Horus reference: `../horus/DUTai.Api/ApiSpec/Paths/AGENTS.md`. Status: complete; reviewed.
23. `AIESummit.Api/wwwroot/AGENTS.md` — browser workflow and safe rendering. Current evidence: app.js, index.html, site.css. Horus reference: `../horus/DUTai.Api/DemoApp/AGENTS.md`. Status: complete; reviewed.
24. `AIESummit.Api/wwwroot/Session.AGENTS.md` — session refresh and asynchronous state. Current evidence: app.js, Program.cs. Horus reference: `../horus/DUTai.Api/DemoApp/AGENTS.md`. Status: complete; reviewed.
25. `deploy/AGENTS.md` — container topology and build. Current evidence: compose.yaml, Dockerfile. Horus reference: `../horus/Voiston.Agents/Containers/AGENTS.md`. Status: complete; reviewed.
26. `deploy/Admin.AGENTS.md` — venture Admin configuration and permissions. Current evidence: admin.json, Dockerfile, framework Admin options. Horus reference: `../horus/AGENTS.md`. Status: complete; reviewed.
27. `deploy/Worker.AGENTS.md` — pack loading queue configuration and emulator. Current evidence: worker.overlay.json, servicebus.json, compose.yaml. Horus reference: `../horus/Voiston.AgentHostCore/Configuration/AGENTS.md`. Status: complete; reviewed.
28. `deploy/Firebase.AGENTS.md` — local and hosted Firebase configuration. Current evidence: firebase.json, Firebase.Dockerfile, Program.cs. Horus reference: `../horus/DUTai.Api/Configuration/AGENTS.md`. Status: complete; reviewed.
29. `scripts/AGENTS.md` — verification gates and fixture handling. Current evidence: smoke.ps1, start.ps1. Horus reference: `../horus/Voiston.Agents.Tests/AGENTS.md`. Status: complete; reviewed.
30. `scripts/smoke.AGENTS.md` — end-to-end assertions and live-model costs. Current evidence: smoke.ps1. Horus reference: `../horus/Voiston.Agents.Tests/LLM/AGENTS.md`. Status: complete; reviewed.
31. `Startup.AGENTS.md` — startup shutdown secrets and troubleshooting. Current evidence: start.ps1, compose.yaml, .gitignore. Horus reference: `../horus/Voiston.AgentHostCore/build_to_temp.AGENTS.md`. Status: complete; reviewed.

## Cite8 reference selection

Per user instruction, use Erick's most recently active Cite8 branch as the supplementary net10 reference. After fetching origin on 2026-11-06, the latest remote branch tip authored by Erick was `origin/codex/certification-tier-checkout`, commit `903626a9bb4ce471bccc28490b857886d6aa5c78` (2026-11-06 18:55:03 UTC). Read files with `git -C ../cite8 show 903626a9bb4ce471bccc28490b857886d6aa5c78:<path>` so the sibling working tree remains untouched. Start with `AGENTS.md`, then the corresponding `src/Cite8.Domain`, `src/Cite8.Agents`, `src/Cite8.Api`, or `deploy` guidance as relevant. Horus remains the requested documentation-depth example; current AIESummit code and its pinned BlueShift contracts determine actual behavior. Cite8's Auth0, Stripe, and legal-domain features are not AIESummit requirements.

Review gates

- At least 25 distinct substantive guidance files outside external and artifacts.
- One sub-agent task per file, each given a bounded source set and Horus reference.
- Verify named symbols, paths, commands and examples against current code.
- Cover metadata, connected lists, ILlmCapable, IHaveChildren, IHaveUniqueTag, persistence, API, browser, deployment, telemetry and tests.
- Distinguish implemented capabilities from proposed patterns, including absent migrations and CLI.
- Check navigation, contradictions and secrets. Preserve application source and submodule state.

## Task execution record

Each assignment was issued separately for one file, with the current source set and the corresponding Horus reference above. The same three sub-agents were reused for successive bounded assignments.

| Author | Numbered file tasks |
| --- | --- |
| `doc_root` | 1, 5, 8, 11, 14, 17, 20, 23, 26, 29 |
| `doc_domain` | 2, 4, 7, 10, 13, 16, 18, 22, 24, 27, 31 |
| `doc_metadata` | 3, 6, 9, 12, 15, 19, 21, 25, 28, 30 |

Authors checked the relevant current implementation and reported their source evidence. Coordinator review checked cross-guide consistency, reference paths, source-versus-proposed distinctions, and the session's persistence, hydration, recovery, and verification lessons. The root guide was also revisited after authoring to replace rollout notes with the finished navigation.

## Final audit — 2026-11-06

- All 31 planned guidance files exist in this repository, outside the framework submodule. Every file contains more than 500 words of substantive guidance.
- The link audit inspected 412 Markdown links and found no unresolved local targets. Local target existence was checked automatically; remote availability was not claimed.
- No unpaired code fences, merge conflict markers, or trailing whitespace were found in the guidance files. Temporary rollout caveats were removed.
- Application-file hashes matched the documentation-pass baseline across 31 non-documentation files. The BlueShift submodule remained clean at its recorded revision.
- README and root guidance record Erick's selected Cite8 branch and exact reference commit. Horus's legacy assumptions are distinguished from current implementation throughout the guides.
- No runtime tests, database changes, or provider calls were needed for this documentation-only pass. Existing smoke coverage is documented without claiming additional browser, negative-path, or hosted-Firebase testing.


