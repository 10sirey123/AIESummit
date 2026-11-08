# Gemini gateway

Read the [deployment guide](../AGENTS.md) first. [server.mjs](server.mjs) is a dependency-free Node service, built by [Dockerfile](Dockerfile) on `node:24-alpine` and run as Compose service `gemini-gateway` on port 8080 with no published host port.

## Why it exists

The prebuilt BlueShift worker runs the split step's LLM calls as Azure OpenAI Responses API requests. BlueShift's built-in Gemini client requires a Google Cloud service-account credential (Vertex), not an AI Studio API key, and the worker binary cannot be modified here. Bootstrap therefore seeds the `openai` APIInfo with BaseURI `http://gemini-gateway:8080` and the `GEMINI_API_KEY` as its key, and this gateway translates each call to Gemini `generateContent`. OCR does not use the gateway; [OcrAgent.cs](../../AIESummit.Agents/OcrAgent.cs) calls Gemini directly.

## Contract

| Route | Behavior |
| --- | --- |
| `GET /health` | Returns `{"status":"ok"}`; used by the image health check |
| `POST /openai/v1/responses` (also `/v1/responses`) | Translates to `POST {GEMINI_ENDPOINT}/models/{model}:generateContent` |

| Responses field | Gemini field |
| --- | --- |
| `instructions`, system/developer messages | `systemInstruction` |
| `input_text`, `input_image`, `input_file` | `text` and `inline_data` parts |
| `text.format` `json_schema` | `responseMimeType=application/json`, `responseJsonSchema` |
| `reasoning.effort` | `thinkingConfig.thinkingLevel` (`low`, `medium` or `high`) |
| `max_output_tokens` | `maxOutputTokens` |

The reply is mapped back to Responses shape: non-thought text becomes one `output_text`, `status` is `completed` only for finish reason `STOP` with text and otherwise `incomplete` with the reason, and token counts become `usage` (thought tokens count as output). Gemini errors are returned with their HTTP status.

The key comes from the caller's `api-key` header (or `x-goog-api-key` / bearer token) and is forwarded upstream. The gateway stores no secrets and logs only model, status and duration. `GEMINI_ENDPOINT` defaults to `https://generativelanguage.googleapis.com/v1beta`; Compose does not set it. Request bodies are limited to 64 MiB and upstream calls time out after 15 minutes.

The worker waits for this service to be healthy. Health proves only that the process is listening, not that Gemini accepts the key or model. Rebuild the image after editing `server.mjs`.
