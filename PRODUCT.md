# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Stack

React and TypeScript for the separate AIESummit workspace. ASP.NET Core, Firebase, PostgreSQL, and BlueShift provide the existing service.

## Product Purpose

AIESummit helps people make sense of information across documents with AI. The current workspace turns uploaded PDFs or images into ordered Markdown documents with page ranges and available extracted images.

## Operating Context

Users sign in with email and password, upload a source file, track processing, and inspect the resulting documents. A specific professional audience has yet to be specified.

## Capabilities and Constraints

The existing API defines supported formats, file limits, result states and owner-scoped access. The React reader switches between one fitted original PDF page and its scrolling OCR Markdown. Its sidebar displays native Blueshift page-section classifications, short summaries and separately expandable exact source quotes. Reviewer notes and legal risk scoring remain future work. Preserve the original Markdown and evidence provenance; see [page analysis](docs/page-analysis.md).

## Brand Commitments

The product brief, brand assets and visual authority are in [docs/ui.md](docs/ui.md).
