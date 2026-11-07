-- Additive fingerprint for exact-byte OCR reuse.
ALTER TABLE "Uploads" ADD COLUMN IF NOT EXISTS "OcrCacheKey" character varying NOT NULL DEFAULT '';
CREATE INDEX IF NOT EXISTS "IX_Uploads_OcrCacheKey_OcrStatus" ON "Uploads" ("OcrCacheKey", "OcrStatus");
