# AIESummit React workspace

Meta Muse Wonder uses the native page LLMStep and persisted APIInfo; see [Meta setup](../docs/meta-analysis.md) for version provenance, explicit key import, model selection and verification. The reader labels are provider-neutral.

Read [the product/design brief](../docs/ui.md) and [UI workflow](../docs/ui-workflow.md) before visual changes. Preserve authenticated owner-scoped requests. Document content and model output are untrusted.

`document-viewer.tsx` selects one persisted OCR page across Original PDF / Markdown tabs. `pdf-page.tsx` uses local PDF.js and a local worker to draw exactly one canvas, fitted to the stage width and height. Never use a continuous iframe PDF viewer or render adjacent pages. PDF bytes come from the protected original-source endpoint, stay in memory, and are not sent to a third-party viewer. Clean up PDF tasks, render tasks, requests and object URLs on changes/unmount. PNG/JPEG originals use a fitted image. Page changes reset the bounded Markdown scroll region; Markdown comes directly from `UploadDetail.pages`, not inferred splits of document Markdown.

Markdown uses `react-markdown`/`remark-gfm`, skips raw HTML, allows only explicit HTTP(S)/mailto navigation with `noopener noreferrer`, and renders Markdown image URLs as inert text references. Actual extracted images use the authenticated API and revocable blob URLs. Full document Markdown remains downloadable; a details control shows the selected page's source.

The sidebar displays the selected page's native Blueshift section classifications, generated summaries and separately expandable exact quoted evidence. It distinguishes not-started, analyzing, completed and failed states. Existing pages require an explicit owner action before analysis; fresh uploads use the enabled page step. No reviewer notes or legal risk scoring are implemented. On phones the sidebar sits below the reader. See [page-analysis.md](../docs/page-analysis.md).

Run `npm run build` and `npm run test:viewer` from `web`. The latter uses Vite SSR, React static rendering and Node assertions for safe Markdown, page provenance, reader controls and output states; PDF rendering and tab/page navigation need real browser checks. Verify one complete PDF page, no neighboring pages, the matching Markdown, scrolling/reset, keyboard tab switching, narrow screens and source ownership. The original static client on port 18080 remains a separate literal-Markdown client.
