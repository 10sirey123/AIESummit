---
name: AIESummit
description: Modern Dark for a document workspace with graphite surfaces and precise typography.
colors:
  ground: "#050505"
  panel: "#0e0e0e"
  hairline: "#222222"
  hairline-2: "#303030"
  bright: "#f2f2f2"
  read: "#a6a9ad"
  meta: "#8a8d91"
  live: "#3fd6c6"
  inverse-ink: "#0a0a0a"
  brand-focus: "#70d7ff"
  error: "#ffada7"
  control-border: "#626262"
  control-border-hover: "#686868"
  control-fill: "#242424"
  primary-hover: "#cfd8de"
typography:
  display:
    fontFamily: "Geist, 'Helvetica Neue', Arial, sans-serif"
    fontWeight: 450
    lineHeight: 1.02
    letterSpacing: "-0.04em"
  headline:
    fontFamily: "Geist, 'Helvetica Neue', Arial, sans-serif"
    fontWeight: 450
    lineHeight: 1.12
    letterSpacing: "-0.04em"
  title:
    fontFamily: "Geist, 'Helvetica Neue', Arial, sans-serif"
    fontSize: "1.5rem"
    fontWeight: 450
    lineHeight: 1.12
    letterSpacing: "-0.025em"
  body:
    fontFamily: "Geist, 'Helvetica Neue', Arial, sans-serif"
    fontSize: "16px"
    lineHeight: 1.55
  label:
    fontFamily: "Geist, 'Helvetica Neue', Arial, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 550
    lineHeight: 1.55
  metadata:
    fontFamily: "'Geist Mono', Menlo, monospace"
    fontSize: "0.8125rem"
    lineHeight: 1.55
  markdown:
    fontFamily: "Geist, 'Helvetica Neue', Arial, sans-serif"
    fontSize: "0.9375rem"
    lineHeight: 1.8
rounded:
  panel: "6px"
  pill: "999px"
spacing:
  gutter: "48px"
  gutter-medium: "36px"
  gutter-narrow: "20px"
  gap: "24px"
  gap-narrow: "16px"
  max: "1360px"
components:
  button-primary:
    backgroundColor: "{colors.bright}"
    textColor: "{colors.inverse-ink}"
    typography: "{typography.label}"
    rounded: "{rounded.pill}"
    padding: "9px 17px"
    height: "44px"
  button-primary-hover:
    backgroundColor: "{colors.primary-hover}"
  button-secondary:
    backgroundColor: "{colors.panel}"
    textColor: "{colors.bright}"
    typography: "{typography.label}"
    rounded: "{rounded.pill}"
    padding: "9px 17px"
    height: "44px"
  button-secondary-hover:
    backgroundColor: "{colors.control-fill}"
  button-disabled:
    backgroundColor: "{colors.control-fill}"
    textColor: "{colors.read}"
  input:
    backgroundColor: "{colors.ground}"
    textColor: "{colors.bright}"
    typography: "{typography.body}"
    rounded: "{rounded.panel}"
    padding: "8px 12px"
    height: "46px"
  upload-panel:
    backgroundColor: "{colors.panel}"
    rounded: "{rounded.panel}"
    padding: "{spacing.gap}"
  upload-row:
    backgroundColor: "transparent"
    textColor: "{colors.read}"
    padding: "12px 8px"
  upload-row-current:
    backgroundColor: "{colors.panel}"
  status:
    textColor: "{colors.read}"
    typography: "{typography.body}"
  markdown-block:
    backgroundColor: "{colors.panel}"
    textColor: "{colors.bright}"
    typography: "{typography.markdown}"
    rounded: "{rounded.panel}"
    padding: "18px"
---

# Design System for AIESummit

## Overview

**Creative North Star: "Modern Dark"**

A near-black field gives document content room to breathe. Opaque graphite groups related controls and results. White headlines and primary actions stand forward, while quiet grays keep supporting information legible.

[docs/ui.md](docs/ui.md) is the authoritative product and design brief. Read it before changing the interface. This file records the system in the [React stylesheet](web/src/styles.css). Follow the [UI workflow](docs/ui-workflow.md) for component, asset, and verification steps.

**Key Characteristics:**

- Near-black ground and opaque graphite surfaces
- Regular Geist for reading and Geist Mono for technical content
- White primary actions, icy blue focus, and small mint status signals
- Static light behind the sign-in form and hairlines through reading areas

## Colors

### Primary

- **Bright** is the near-white for headlines, active text, and the primary button fill. **Inverse ink** gives the filled button dark text.
- **Brand focus** is the icy blue used for keyboard focus, input carets, and text selection. It picks up the blue in the supplied [AIESummit mark](docs/assets/aiesummit_logo_only_large.png).

### Secondary

- **Live** is mint for the selected upload arrow and the ready dot beside its label.
- **Error** is soft coral for error copy and the failed status dot.

### Neutral

- **Ground** is the deepest surface. **Panel** is the opaque graphite used for forms, selected rows, and Markdown blocks.
- **Hairline** marks section and row boundaries. **Hairline 2** gives panels and the result region a slightly clearer edge. Inputs and secondary actions use the stronger **control border**.
- **Read** carries supporting copy. **Meta** carries captions, placeholders, and secondary labels.

**The Small Signal Rule.** Keep mint to small marks and pair status with readable text.

## Typography

Geist is bundled for headings, reading, and form controls. Bundled Geist Mono distinguishes dates, upload metadata, page ranges, and Markdown source. Form labels stay in Geist.

The opening statement uses regular-weight type with tight tracking and near-single leading. It scales with the viewport using a desktop clamp of 3–6rem and a clamp of 2.75–5.5rem at 1180px and below. The workspace heading uses a separate 2.25–4.5rem clamp. The frontmatter records the shared type attributes, with fluid sizes here so the token fields remain portable.

## Layout

A centered shell sets the outer edges of the header, workspace, and footer. At wide widths the sign-in grid gives the opening statement seven of twelve columns and the form five. The ruled workflow summary spans the shell. The signed-in workspace places compact intake controls above an upload rail and a document reader. The reader pairs a Markdown preview with an annotations column; document selection and Preview/Markdown source controls sit above it.

At 1180px the sign-in copy and form stack, with the form capped at 540px. At 900px the workflow summary steps from three columns to two. At 690px the results and upload list stack, upload actions can wrap, and a back-to-uploads control appears. At 560px the shell uses its narrow gutter and gap, and the workflow summary becomes a single column. The stylesheet implements these as viewport breakpoints.

## Elevation & Depth

**The Opaque Surface Rule.** Separate reading and work areas with tonal contrast and one-pixel rules. Keep the form and result surfaces opaque.

A static radial gradient behind the sign-in form supplies a soft neutral light. The panels rely on their graphite fill and border for depth.

## Shapes

The compact panel radius (6px) is shared by graphite panels, inputs, the brand image, and Markdown blocks. The pill radius (999px) gives action buttons their silhouette and rounds the small status dots.

## Components

- **Buttons.** A bright filled pill marks the primary action. The outlined graphite pill handles secondary actions. Both have a 44px minimum height. Hover changes fill or border over 120ms with ease-out, and reduced-motion preferences disable the transition. Disabled buttons use the quiet control fill with read-gray text. Text actions such as the account switch and back-to-uploads control are underlined.
- **Fields.** Sign-in fields use a ground fill, visible labels, and a clear neutral stroke. The global keyboard focus outline is 2px with a 4px offset. The file picker transfers the same outline to its visible label.
- **Panels.** The sign-in form and upload controls use a graphite fill and stronger hairline. The document reader uses graphite with proportional Geist for rendered Markdown and monospaced text for source/code. Its adjoining annotations column shows page-section classifications and summaries, with separately expandable source evidence. The original PDF tab fits one complete page; the matching Markdown tab scrolls within the reader.
- **Upload navigation.** Ruled rows show a bright filename above monospaced metadata. Hover and the current row use graphite. The current row adds a mint arrow and carries the current-state attribute.
- **Result status.** A labeled dot uses mint for Ready and coral for Failed. The remaining dot follows the text color.

## Do's and Don'ts

- **Do** reserve the bright fill for the primary action.
- **Do** show keyboard focus with the offset icy blue outline.
- **Do** keep rendered Markdown readable, provide the original source, and preserve page boundaries.
- **Don't** use colored glows, translucent surfaces, or decorative motion.
- **Don't** use mint as a general accent or let a status dot stand alone.
