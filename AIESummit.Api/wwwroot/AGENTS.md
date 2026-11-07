# Browser document workspace

This directory is the app's static browser interface, served by the ASP.NET API at the same origin. It is plain HTML, CSS and JavaScript with an ES module. There is no React/Vite project, frontend package installation, bundler, separate development server, client-side router or generated TypeScript client.

Read [Session.AGENTS.md](Session.AGENTS.md) for authentication and asynchronous state changes, [Authentication.AGENTS.md](../Authentication.AGENTS.md) for server validation and ownership, and [ApiSpec/AGENTS.md](../ApiSpec/AGENTS.md) for the public contract.

## Files and serving

| File | Responsibility |
| --- | --- |
| `index.html` | Semantic page structure, forms and stable DOM IDs |
| `site.css` | Layout, controls, text display and responsive sizing |
| `app.js` | Firebase REST session, upload/list/detail workflow and image loading |
| `openapi.json` | Generated public contract, sourced from `../ApiSpec` |

`Program.cs` calls `UseDefaultFiles` and `UseStaticFiles` before authentication middleware. The sign-in interface and static assets are public. Protected data is fetched from owner-scoped API routes after authentication. Do not put user content or private credentials into static files.

`index.html` loads `/app.js` with `type="module"` and `/site.css` with an absolute root path. Top-level `await` fetches `/firebase-config` before the script registers handlers. This requires serving through the API, not opening `index.html` through a `file://` URL. A configuration fetch or parse failure can prevent initialization, so inspect that request before debugging form listeners.

The normal local entrypoint is `http://localhost:18080` after running `./start.ps1` from the repository root. A container serves published assets from its image. Editing a host file does not imply that an already-running image sees the change.

## DOM and workflow contract

The script resolves elements with `document.getElementById`. Keep IDs synchronized when changing markup. Missing or renamed elements can break listener registration or later rendering without a compile-time error.

| IDs | Workflow role |
| --- | --- |
| `auth`, `auth-form` | Sign-in/account-creation panel and Firebase request inputs |
| `signout`, `workspace` | Session-dependent workspace visibility |
| `upload` | Multipart file form |
| `uploads`, `more` | Accumulated upload list and older-page action |
| `detail`, `detail-title`, `detail-status`, `documents` | Selected upload and generated document output |
| `notice` | User-visible errors and status announcements |

The auth form's buttons use `signInWithPassword` and `signUp` values that become Firebase REST operations. Its input names are `email` and `password`. The upload form's input name is `file`, matching the API's `IFormFile file` parameter and OpenAPI multipart schema.

On successful authentication, the script resets the password form, hides the auth panel, reveals the workspace and loads uploads. On upload success, it resets the file form and requests list/detail in parallel. Signout clears local state and rendered results, then restores the auth panel.

The selected upload is in memory. Clicking a row does not update the URL or provide a deep link. Refreshing the page also loses the in-memory session. Do not describe browser back/forward navigation, persisted selection or remembered sign-in as implemented features.

## API representations used by the UI

All application requests use same-origin `/api/v1` routes. There is no browser-configurable API base URL or CORS proxy in this frontend.

| Operation | Representation consumed |
| --- | --- |
| Multipart `POST /uploads` | HTTP 202 JSON with `id` |
| `GET /uploads` with optional `before` | Array of summaries with `id`, `fileName`, `status` and related fields |
| `GET /uploads/{id}` | `upload` plus `documents` containing title, page range, Markdown and image references |
| `GET /uploads/{id}/images/{imageId}` | Authenticated binary image response |

The UI uses the API's camel-case wire properties directly. It has no PascalCase normalization layer. An endpoint projection change must be checked against both the contract fragments and every browser property read.

Public state labels map `queued` to “Waiting for OCR”, `splitting` to “Identifying documents”, `ready` to “Ready”, and `failed` to “Failed”. The mapping is a display layer, not the framework state machine. Unknown public states currently have no explicit fallback label.

`jsonRequest` parses successful responses as JSON. On failure it tries JSON and chooses `detail`, a Firebase `error.message`, or a generic status-based message. It is not suitable unchanged for a successful empty response or a binary body. Images deliberately use a separate `fetch` path.

The `api` helper supplies the bearer header from `token()`. Its current header object replaces any `headers` supplied in options. If adding a JSON request, merge needed headers deliberately and verify the body encoding rather than assuming a caller's content type survives.

## Authentication and asynchronous state

The script chooses hosted Firebase REST endpoints or the local emulator using `/firebase-config`. Credentials and tokens remain in JavaScript memory rather than localStorage or sessionStorage. The browser contains no Admin token, Foundry key or provider configuration.

`token()` refreshes 60 seconds before the stored expiry using a shared `refreshInFlight` promise. It checks that the session object has not changed before applying refresh output. There is no automatic retry of every rejected API request, and browser signout does not revoke remote refresh tokens.

List and upload paths capture the active session and decline to apply results from an obsolete session. Detail requests increment `requestNumber`, and both detail rendering and image completion compare that generation. Signout increments it too. Preserve these guards when extracting helpers or introducing rendering abstractions.

The guards do not abort all requests. Some error callbacks still report errors without a complete session-generation check. Concurrent auth submissions are not globally serialized because only the clicked submit button is disabled. Test these edge cases when changing session behavior instead of claiming race-free behavior from the presence of one request counter.

## Safe text rendering

Filenames, status text, document titles, Markdown and notices are assigned with `textContent`. Document Markdown is displayed as literal text in a `<pre>`, not parsed into HTML. The page-break marker can therefore remain visible as source text. This is the current behavior, not an incomplete Markdown-renderer setup.

Keep source and model-produced content untrusted. Do not switch to `innerHTML` or add a Markdown renderer without defining sanitization, link behavior, image handling and supported markup. A provider response is not safe HTML merely because it came from the API.

CSS uses `white-space: pre-wrap` and wrapping for long text. Preserve readable page output when changing typography or widths. Newlines and long identifiers are legitimate document content, so test them instead of truncating all text indiscriminately.

## Authenticated image flow

Each document's image references create a “View image from page …” button. Clicking it fetches the protected image route with an Authorization header, converts the response to a blob and creates an object URL. A successful current-generation result replaces the button with a figure and image.

The image's alt text is the API's `reference` value. This is a technical reference and may not be a useful semantic description. Improving accessible descriptions is future work, not a claim about current OCR metadata.

Object URLs are tracked in `imageUrls`. `clearImages()` revokes them when detail is redrawn or the user signs out. Keep that cleanup when adding previews or downloads. A direct `<img src="/api/v1/...">` cannot attach the app's bearer header in the same way, so replacing the blob flow can break authenticated image access.

The UI shows extracted image records, not a thumbnail for every OCR page. A document can have valid page text and no images. There is no lightbox, zoom, signed public URL, image grid or prefetch cache. Do not invent a missing-image bug solely because a text-only PDF has no image buttons.

## Polling and pagination

Every four seconds the polling interval checks for a session, an idle polling flag and at least one cached upload in `queued` or `splitting`. It fetches the newest list page. It refreshes selected detail only when that selected upload's public status changes in the cached list.

The flag prevents two interval bodies from overlapping, but it is not a global lock across manual list requests, uploads and selection. Do not equate it with complete request serialization.

The list merges results into `recent` by ID, sorts descending and preserves previously loaded rows. “Load more” sends the lowest cached ID as an exclusive `before` cursor. The server returns at most 50 records. A page with fewer than 50 hides the older-page action under the current conditions.

Current limitations matter when testing updates.

- Polling only fetches the newest page. An older pending upload retained from pagination may stay stale if it falls outside that page.
- Existing cached rows are not removed merely because they are absent from a later list response.
- Detail does not automatically refresh for changed content while its public status remains identical.
- Polling stops when no cached upload is pending. It is not a background subscription for uploads created in another browser.
- Each detail redraw clears its image previews, so a status transition can require reopening an image.

These are implementation boundaries, not instructions to add a new framework. If the task changes them, choose the smallest state/update strategy that meets the desired behavior and add meaningful browser checks.

## Layout and accessibility

The current page declares English, has a viewport meta tag, semantic header/main/sections, heading hierarchy, labels wrapping inputs and native buttons. The auth section is named with `aria-labelledby`. `notice` has `role="status"` and `aria-live="polite"`. Hidden panels use the native `hidden` attribute with a CSS rule preserving its effect.

Inputs use native email, required and minimum-password-length validation. The file `accept` list improves selection but does not validate bytes or enforce the server limit. Upload size and signature checks remain server responsibilities, and the 30-page OCR bound is enforced later during processing.

CSS uses fluid heading/panel sizing, a bounded main width, wrapping action buttons and responsive image sizing. It does not contain a comprehensive mobile navigation system or an audited accessibility implementation. Native focus outlines are not explicitly removed, but there is no custom focus-management code after signin, signout, detail selection or errors.

When editing the interface, verify keyboard navigation, visible focus, label associations, text zoom, small widths and screen-reader announcements. Do not claim a contrast ratio or accessibility conformance without measurement. Consider whether newly inserted results need a deliberate announcement or focus destination rather than moving focus on every poll.

## Checks and manual scenarios

The existing syntax check runs from the repository root.

```powershell
node --check AIESummit.Api/wwwroot/app.js
```

There is no `npm run check`, `npm run build`, Playwright suite or frontend type-check configuration in this directory. The .NET API build verifies hosting code, not browser interaction. The OpenAPI builder is a separate contract assembly step, not a JavaScript build step.

For browser verification, start the intended local stack and open the workspace. Use synthetic content and local accounts unless the task specifies a different test environment. A useful manual pass includes the following.

1. Create an account, sign out and sign in. Verify form validation and readable failure messages without exposing passwords or tokens.
2. Upload a supported synthetic file and observe the list, selected detail and processing labels. Provider processing may incur configured live OCR/LLM charges.
3. Open a completed upload and verify literal Markdown, page ranges, long names and empty-image behavior. Where an extracted image exists, check that it loads and has nonzero natural dimensions.
4. Switch rapidly between uploads while requests are delayed. Sign out during a detail or image request and ensure previous-account content does not reappear.
5. Exercise refresh near token expiry and a failed refresh. Verify current-session guards and understandable error handling.
6. With enough fixture rows, load older pages and observe the documented polling limitations. Do not create dozens of paid OCR jobs merely to test list pagination.
7. Test narrow and wide viewports, keyboard-only controls, visible focus and zoom. Check text overflow and image sizing.
8. Use a second account to verify that knowing another user's upload or image ID does not grant access. UI hiding alone is not a security test.

`scripts/smoke.ps1` checks the API/provider workflow and some ownership boundaries. It does not launch this UI or prove layout, keyboard behavior, session races or object-URL cleanup. Report manual browser checks separately from script results. For documentation-only edits, do not claim either was performed.

## Troubleshooting and source references

If controls do nothing, inspect module loading and `/firebase-config` before modifying state code. If the workspace loads but processing stalls, inspect the API response and worker telemetry. If image buttons are absent, check `documents[].images` before changing rendering. If an image fetch is rejected, verify session refresh and ownership rather than making the endpoint public.

Use browser network inspection carefully. Do not export or share Authorization headers, Firebase responses containing tokens, passwords or real document bodies. There is no built-in API Inspector panel in this app.

Current sources are [app.js](app.js), [index.html](index.html), [site.css](site.css), [Program.cs](../Program.cs), [UploadEndpoints.cs](../UploadEndpoints.cs) and [Paths/uploads.json](../ApiSpec/Paths/uploads.json).

The complete historical reference is `../../../horus/DUTai.Api/DemoApp/AGENTS.md`. Its browser workflow and image-verification discipline inspired this guide. Its React/Vite stack, OAuth localStorage configuration, patient/case UI, inspector, public image URLs and npm commands do not apply to AIESummit's static same-origin workspace.
