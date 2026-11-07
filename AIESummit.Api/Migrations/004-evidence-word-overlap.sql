-- Preserve historical exact-match evidence; no analyses or prompts are reset.
ALTER TABLE "ExtractionEvidence" ADD COLUMN IF NOT EXISTS "ModelQuote" text NOT NULL DEFAULT '';
ALTER TABLE "ExtractionEvidence" ADD COLUMN IF NOT EXISTS "MatchMethod" text NOT NULL DEFAULT 'exact';
ALTER TABLE "ExtractionEvidence" ADD COLUMN IF NOT EXISTS "MatchScore" double precision NOT NULL DEFAULT 1;
