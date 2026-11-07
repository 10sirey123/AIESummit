# Browser session and asynchronous state

This guide describes the actual session and request behavior in [app.js](app.js). Read the [browser guide](AGENTS.md) for rendering and the [authentication guide](../Authentication.AGENTS.md) for server verification. Treat race-condition improvements below as proposed work until implemented.

## Configuration and memory state

The module first fetches `/firebase-config`. It chooses hosted Firebase Identity Toolkit and Secure Token endpoints or their emulator equivalents using the returned `emulator` flag. Emulator URLs use the current page hostname, the configured emulator port and HTTP.

The server only selects emulator authentication when its environment is Development and the emulator configuration is enabled. The browser follows that server response. It does not independently determine whether accepting emulator tokens is safe.

Session state lives in module variables.

| Variable | Meaning |
| --- | --- |
| `session` | Current sign-in response plus early expiration time, or null |
| `selected` | Currently requested upload ID |
| `recent` | Merged upload summaries already loaded in this page |
| `refreshInFlight` | Shared pending token-refresh promise |
| `requestNumber` | Generation counter for detail requests and their image callbacks |
| `imageUrls` | Blob object URLs created for displayed images |
| `polling` | Prevents overlapping timer-driven poll cycles |

The client does not persist tokens or session state in localStorage, sessionStorage or cookies. Reloading the page loses the session. It does not use a Firebase SDK auth-state listener or restore a session from browser storage.

The initial configuration fetch has no local retry or error-display wrapper. A failed fetch or invalid response can prevent module initialization. Diagnose that separately from a failed sign-in request.

## Sign-in and sign-out transitions

The auth form submits email/password JSON to `accounts:` plus the submit button's action value. It requests secure tokens, disables that submitter while awaiting the request and clears the notice. On success it assigns a new session object and computes expiration from `expiresIn` with a 60-second margin.

Success resets the auth form, hides authentication UI, shows the workspace/sign-out controls and loads the upload list. Errors display through `showError`, which assigns the error message to notice text. The clicked submitter is re-enabled in `finally`.

Only the clicked auth submitter is disabled. The handler has no authentication-request generation check. If multiple auth actions are somehow in flight, a late success can replace a newer session and a late failure can display after state changes. Do not describe sign-in as fully race-proof.

Sign-out clears `session`, `selected` and `recent`, increments `requestNumber`, revokes tracked image URLs, clears list/document elements, resets the upload form, hides workspace/detail/sign-out and shows authentication UI. It also clears the notice.

Sign-out is local UI state clearing. It does not call Firebase token revocation, cancel in-flight requests or clear the shared refresh promise explicitly. A request already sent may still complete on the server. Do not describe sign-out as undoing an accepted upload.

## Early expiry and coalesced refresh

`token()` rejects when no session exists. While `Date.now() < session.expires`, it returns the current ID token. Otherwise it captures the current session object as `active` and creates or awaits `refreshInFlight`.

Refresh posts a form-encoded `refresh_token` grant to the Secure Token endpoint. The shared promise is cleared in `finally`, so concurrent callers generally reuse one refresh request instead of issuing one request per API call.

After refresh resolves, the caller checks object identity with `session !== active`. If the session changed, it throws `Your session changed.` and does not update the old captured session. Otherwise it stores the new `id_token`, `refresh_token` and expiration using `(expires_in - 60)` seconds.

The 60-second margin is applied on both initial sign-in and refresh. There is no background refresh timer. Refresh happens when a protected request asks for a token after the early-expiration point. There is also no automatic retry-on-401 mechanism or forced sign-out on refresh failure.

The coalescing variable is global to the module, not keyed by account. An old refresh remains pending across sign-out. If a later session needs refresh while that promise still exists, it can await the old request. The identity check protects callers whose own captured session changed, but it does not associate the shared promise with a specific session. Session-scoped refresh promises would be a proposed improvement, not current behavior.

## API helper and request identity

`api(path, options)` obtains a token and calls `jsonRequest('/api/v1' + path, ...)` with a bearer header. It replaces the supplied `headers` object rather than merging arbitrary caller headers. Current multipart uploads rely on the browser to set their boundary and do not supply a JSON content type.

`jsonRequest` throws using `detail`, Firebase's nested error message or a generic status message when the response is not successful. For successful responses it always calls `response.json()`. A future 204 operation would need different handling.

There are two distinct stale-response guards. Session object identity protects upload/list success handling across account changes. `requestNumber` protects detail/image success rendering across selection changes and sign-out. They are not interchangeable, and neither aborts network activity.

## Upload and list transitions

The upload handler captures `active = session`, disables the submitter and clears the notice. After a successful POST, it checks that the session is still identical before resetting the form and starting `list()` and `detail(newId)` concurrently.

Upload errors are displayed only if the captured session is still current. The button is re-enabled regardless. A sign-out after the POST was sent can prevent stale UI rendering while the upload still persists under the original authenticated account.

`list(before)` captures the session, awaits the API and rejects stale success results by session identity. It merges returned summaries into `recent` using a Map keyed by ID, then sorts descending. Existing summaries absent from the latest page remain in memory.

This merge retains older loaded pages during refresh. It does not remove server-deleted rows or refresh every older row. Concurrent list calls within the same session have no sequence guard, so an older response arriving late can overwrite a newer summary for the same ID.

The More button requests `recent.at(-1)?.id`. The API returns at most 50 older records using an exclusive ID cursor. The client updates More visibility when loading an older page or when the merged list contains at most 50 rows. A top-page refresh after more than 50 rows are cached does not recompute that visibility.

List rendering creates text buttons and status labels. A row button calls `detail(id).catch(showError)`, and More calls `list(...).catch(showError)`. Those catch handlers do not capture session identity. Stale list/detail errors can therefore appear after sign-out or another account's sign-in even though stale list success is ignored.

## Detail request generation

`detail(id)` assigns `selected`, increments `requestNumber` and captures that generation as `seq`. After the JSON response arrives, it returns without rendering if the generation no longer matches.

A later detail selection invalidates earlier successful detail responses. Sign-out also increments the generation. Detail does not separately capture session identity. Its success protection relies on generation changes, so any future account-switch path must preserve that invalidation.

For an accepted response, detail shows the panel, sets the filename and error/status text, clears document elements and revokes all existing image URLs. It then renders document titles, inclusive page labels and Markdown as text. The Markdown is placed in a `pre`, not interpreted as HTML.

The generation guard only runs after a successful response. Errors thrown before that point are handled by the caller's catch path, which may not have the same stale-response protection. Preserve this distinction when debugging a notice that appears after selecting another upload.

## Image requests and object URLs

Image buttons fetch the authorized image route with a token, check HTTP success and read a blob. Each callback retains the detail generation that created it. After blob loading, it checks that generation before creating an image element or object URL.

On current-generation success, the callback creates `URL.createObjectURL(blob)`, tracks it in `imageUrls` and replaces the button with a figure. Its alternative text comes from the image reference. A stale successful image response returns before allocating the URL.

`clearImages()` revokes every tracked URL. It runs on sign-out and accepted detail rendering, including a refresh of the same selected upload. Thus a detail refresh also removes already expanded images and requires reopening their controls.

There is no explicit unload handler, per-image disposal workflow or AbortController. Pending fetches still finish after navigation within this page. Image error handling unconditionally shows a notice and re-enables its button, even if that button has been detached by a newer detail render. Adding generation checks to errors would be a proposed improvement.

## Polling behavior

A timer fires every 4,000 milliseconds. It skips work when there is no session, a previous timer cycle is still running, or no cached summary has status `queued` or `splitting`.

Before refreshing, it remembers the selected upload's status from `recent`. It then refreshes the newest list page. It reloads selected detail only when that summary's status changed. It does not poll detail on every cycle and does not compare document counts, errors, Markdown or images directly.

This keeps ordinary processing updates responsive with limited requests, but has important limits.

- Pending uploads never loaded into `recent` do not start polling.
- Older pending uploads retained beyond the newest 50 can keep the timer active without receiving updated summaries from `list()`.
- Document or image changes under an unchanged summary status do not automatically refresh selected detail.
- A stale completed or failed summary can stop polling even if external activity later restarts that upload.
- The `polling` flag prevents overlapping timer cycles, not user-triggered list/detail requests.

The timer's catch shows an error whenever any session exists. It does not prove that the current session is the one that began the poll. Account changes during a poll therefore need dedicated race testing. Its `finally` resets `polling` even after failures.

## Proposed regression checks

These are targeted test scenarios for future browser coverage. They are not claims that the current repository already automates each race.

| Scenario | Observe |
| --- | --- |
| Several protected requests at early expiry | One refresh request under the same session and updated tokens used afterward |
| Refresh rejects | Shared promise clears, callers fail visibly and later attempts can retry |
| Sign out during refresh | Old caller does not mutate its former session after completion |
| New session while old refresh remains pending | Expose the global coalescing limitation before changing it |
| Slow detail A followed by fast detail B | Only B's successful response renders |
| Detail or image error after sign-out | Characterize stale error notices separately from guarded success |
| Upload completion after account change | No old-session upload UI is rendered, while server persistence is checked independently |
| Concurrent same-session list responses | Detect whether late summaries overwrite newer statuses |
| More than 50 rows with an older pending upload | Characterize top-page polling and retained-row staleness |
| Documents change without public status change | Detail is not automatically reloaded under current logic |
| Repeated detail refresh and sign-out | Created object URLs are revoked and stale successes allocate none |
| Reload page after signing in | Memory-only session is lost and authentication UI returns |

Use controlled delayed responses for ordering tests rather than relying on provider latency. Keep tokens and passwords out of console dumps, fixtures and screenshots. Proposed hardening may include session-scoped refresh promises, guarded error rendering, cancellation and more targeted polling, but each changes behavior and requires its own review.

The existing [smoke workflow](../../scripts/smoke.AGENTS.md) provides API/processing and account-isolation evidence. It does not substitute for browser promise-ordering and object-URL lifecycle assertions. Review the actual smoke script before claiming a browser regression is already covered.

## References and legacy differences

Current authority is [app.js](app.js) and [Program.cs](../Program.cs). The complete historical `../../../horus/DUTai.Api/DemoApp/AGENTS.md` was read for session and progressive-refresh context. That React/Vite app's localStorage OAuth cache, configurable API origin, TanStack Query behavior and case-document flow do not describe this same-origin, plain-JavaScript Firebase client.
