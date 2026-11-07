-- Additive, idempotent upgrade for retained databases; never resets uploads/prompts.
ALTER TABLE "Pages" ADD COLUMN IF NOT EXISTS "LLMStepStatus" integer NOT NULL DEFAULT 0;
ALTER TABLE "Pages" ADD COLUMN IF NOT EXISTS "LLMJsonResult" text NOT NULL DEFAULT '';
ALTER TABLE "Pages" ADD COLUMN IF NOT EXISTS "SectionsJson" text NOT NULL DEFAULT '';
ALTER TABLE "Pages" ADD COLUMN IF NOT EXISTS "AnalysisError" text NULL;
ALTER TABLE "LLMPrompts" ADD COLUMN IF NOT EXISTS "OcrPage_ID" integer NULL;
CREATE INDEX IF NOT EXISTS "IX_LLMPrompts_OcrPage_ID" ON "LLMPrompts" ("OcrPage_ID");
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_LLMPrompts_Pages_OcrPage_ID') THEN
        ALTER TABLE "LLMPrompts" ADD CONSTRAINT "FK_LLMPrompts_Pages_OcrPage_ID"
            FOREIGN KEY ("OcrPage_ID") REFERENCES "Pages" ("ID") ON DELETE CASCADE;
    END IF;
END $$;
