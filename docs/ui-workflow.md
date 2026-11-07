# AIESummit UI workflow

Always start with [docs/ui.md](ui.md). It contains the project description, visual direction, and design tokens. [DESIGN.md](../DESIGN.md) points Impeccable to the current implementation. Update the brief first when the visual direction changes.

## Run the workspace

Start the backend from the repository root with `./start.ps1`. In another terminal run:

```powershell
cd web
npm ci
npm run dev
```

Open [AIESummit](http://127.0.0.1:5173). Vite sends `/api/v1` and `/firebase-config` to the ASP.NET API on port 18080. Firebase uses the local emulator when the API is configured for it. The API also serves the original browser client at port 18080. `npm run build` type-checks and builds the standalone frontend into `web/dist`. `npm run preview` serves that build with the same local API proxy. For a deployment, serve the build at the site root and forward the two API paths on the same origin.

## Add a visual element

1. Define the task, the data the API can actually return, and the smallest useful interaction. Sketch the loading, empty, success, and error states where applicable. Read the [API contract](../AIESummit.Api/ApiSpec/AGENTS.md) when the element needs new data.
2. Place the element alongside the closest existing React component under `web/src`. Reuse button treatments, input labels, list rows, and the named CSS variables. Use native controls with a visible label and focus indicator. Use text alongside color to communicate status. Let long filenames, titles, and Markdown wrap.
3. Use the supplied AIESummit mark from `docs/assets` for the brand. Keep product data as real interface text and preserve generated Markdown as literal text.
4. If a new requirement needs a raster illustration or photo, use the Image Generation skill. Describe the subject, crop, intended dimensions, grayscale treatment, graphite setting, and how the image sits alongside copy. Review candidates at their rendered sizes, then export the chosen asset. Record its origin or exact generation prompt and intended usage alongside it. Reserve the image dimensions and give informative images meaningful alt text. For icons, use Lucide or a simple SVG. For a view of the product, capture the actual app with synthetic content.
5. For a substantial new view, use Impeccable’s `shape` to work through hierarchy and states. If you need design directions, use Product Design’s ideation workflow before implementation. For review, use Product Design’s audit workflow with screenshots of the real interaction.

## Install and use Impeccable

Install the Codex skill from the repository root with the official package:

```powershell
npx --yes impeccable@4.8.0 install --providers=codex --scope=project --no-hooks --yes
```

Reload Codex to discover the installed skill. The install uses explicit checks in place of a post-edit hook. Source and documentation are available from [the Impeccable project](https://github.com/pbakaus/impeccable).

In Codex, use `$impeccable critique <screen>` for hierarchy, task clarity and usability, `$impeccable audit <screen>` for accessibility and responsive issues, and `$impeccable polish <element>` for targeted refinements. Give the review the brief, target files, and screenshots of the affected states.

From `web`, run both checks with the app open:

```powershell
npm run build
npm run design:check
npx --no-install impeccable detect http://127.0.0.1:5173/
```

The first detector checks source patterns. The URL scan checks the rendered public screen. Review authenticated states in the browser. For each finding, fix the issue or document why the brief calls for that specific choice. Retest changed screens.

## Verify in the in-app browser

- Inspect the changed screen at desktop and phone widths. Confirm readable type, wrapping, visible focus, and that all actions fit without sideways scrolling. Test tab order and keyboard activation.
- Use local emulator accounts and synthetic files. Check sign-in errors, account creation, upload validation, processing transitions, Markdown and page ranges, and the image viewer when the fixture contains an extractable image. Sign out and verify a second account sees only its own uploads.
- Record what you observed. A source or public-screen detector pass does not verify authenticated behavior. The [backend smoke test](../scripts/smoke.AGENTS.md) can verify provider processing and telemetry separately.
