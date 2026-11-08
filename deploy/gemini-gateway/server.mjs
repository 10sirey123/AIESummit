// Translates the Azure OpenAI Responses API calls made by the prebuilt BlueShift worker
// into Gemini generateContent calls. Dependency-free; holds no secrets: the caller's
// `api-key` header (the GEMINI_API_KEY stored in the BlueShift "openai" APIInfo) is forwarded.
import http from 'node:http';

const PORT = Number(process.env.PORT ?? 8080);
const GEMINI = (process.env.GEMINI_ENDPOINT ?? 'https://generativelanguage.googleapis.com/v1beta').replace(/\/$/, '');
const MAX_BODY = 64 * 1024 * 1024;
const THINKING = { minimal: 'low', low: 'low', medium: 'medium', high: 'high', xhigh: 'high' };

const send = (res, status, body) => {
  res.writeHead(status, { 'content-type': 'application/json' });
  res.end(JSON.stringify(body));
};
const fail = (res, status, message, code = 'gateway_error') =>
  send(res, status, { error: { message, type: 'gemini_gateway', code } });

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    req.on('data', c => {
      size += c.length;
      if (size > MAX_BODY) { reject(Object.assign(new Error('Request body too large.'), { status: 413 })); req.destroy(); }
      else chunks.push(c);
    });
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    req.on('error', reject);
  });
}

// "data:<mime>;base64,<data>" -> inline_data part; bare base64 falls back to the given mime.
function inlinePart(value, fallbackMime) {
  const m = /^data:([^;,]+);base64,(.*)$/s.exec(value ?? '');
  if (m) return { inline_data: { mime_type: m[1], data: m[2] } };
  if (value && fallbackMime) return { inline_data: { mime_type: fallbackMime, data: value } };
  return null;
}

const mimeFromName = name => ({
  pdf: 'application/pdf', png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg',
  webp: 'image/webp', gif: 'image/gif', txt: 'text/plain', md: 'text/markdown',
})[(name ?? '').split('.').pop()?.toLowerCase()];

function toPart(item) {
  switch (item?.type) {
    case 'input_text':
    case 'output_text':
    case 'text':
      return { text: item.text ?? '' };
    case 'input_image':
      return inlinePart(item.image_url ?? item.imageUrl, 'image/png');
    case 'input_file':
      return inlinePart(item.file_data ?? item.fileData, mimeFromName(item.filename ?? item.fileName) ?? 'application/pdf');
    default:
      return null;
  }
}

export function toGeminiRequest(r) {
  const contents = [];
  for (const msg of r.input ?? []) {
    if (msg.role === 'system' || msg.role === 'developer') continue;
    const items = typeof msg.content === 'string' ? [{ type: 'input_text', text: msg.content }] : msg.content ?? [];
    const parts = items.map(toPart).filter(Boolean);
    if (parts.length) contents.push({ role: msg.role === 'assistant' ? 'model' : 'user', parts });
  }
  const system = [r.instructions,
    ...(r.input ?? []).filter(m => m.role === 'system' || m.role === 'developer')
      .flatMap(m => typeof m.content === 'string' ? [m.content] : (m.content ?? []).map(c => c.text))]
    .filter(Boolean).join('\n\n');

  const generationConfig = {};
  const maxTokens = r.max_output_tokens ?? r.maxOutputTokens;
  if (maxTokens) generationConfig.maxOutputTokens = maxTokens;
  const effort = r.reasoning?.effort?.toLowerCase();
  if (effort && THINKING[effort]) generationConfig.thinkingConfig = { thinkingLevel: THINKING[effort] };
  const format = r.text?.format;
  if (format?.type === 'json_schema' && format.schema) {
    generationConfig.responseMimeType = 'application/json';
    generationConfig.responseJsonSchema = format.schema;
  } else if (format?.type === 'json_object') {
    generationConfig.responseMimeType = 'application/json';
  }

  const body = { contents, generationConfig };
  if (system) body.systemInstruction = { parts: [{ text: system }] };
  return body;
}

export function toResponsesResponse(g, model) {
  const candidate = g.candidates?.[0];
  const text = (candidate?.content?.parts ?? []).filter(p => !p.thought && p.text).map(p => p.text).join('');
  const finish = candidate?.finishReason;
  const usage = g.usageMetadata ?? {};
  const output_tokens = (usage.candidatesTokenCount ?? 0) + (usage.thoughtsTokenCount ?? 0);
  return {
    id: g.responseId ?? `resp_${Date.now()}`,
    object: 'response',
    model,
    status: finish === 'STOP' && text ? 'completed' : 'incomplete',
    incomplete_details: finish === 'STOP' ? undefined : { reason: finish ?? g.promptFeedback?.blockReason ?? 'unknown' },
    output: text ? [{ type: 'message', role: 'assistant', content: [{ type: 'output_text', text }] }] : [],
    usage: {
      input_tokens: usage.promptTokenCount ?? 0,
      output_tokens,
      total_tokens: usage.totalTokenCount ?? (usage.promptTokenCount ?? 0) + output_tokens,
    },
  };
}

async function handleResponses(req, res) {
  const key = req.headers['api-key'] ?? req.headers['x-goog-api-key'] ??
    req.headers.authorization?.replace(/^Bearer\s+/i, '');
  if (!key) return fail(res, 401, 'Missing api-key header.', 'unauthorized');
  let r;
  try { r = JSON.parse(await readBody(req)); }
  catch (e) { return fail(res, e.status ?? 400, e.status ? e.message : 'Invalid JSON body.'); }
  if (!r.model) return fail(res, 400, 'model is required.');

  const started = Date.now();
  let upstream;
  try {
    upstream = await fetch(`${GEMINI}/models/${encodeURIComponent(r.model)}:generateContent`, {
      method: 'POST',
      headers: { 'content-type': 'application/json', 'x-goog-api-key': key },
      body: JSON.stringify(toGeminiRequest(r)),
      signal: AbortSignal.timeout(15 * 60 * 1000),
    });
  } catch (e) {
    console.error(`gemini ${r.model} network error: ${e.name}`);
    return fail(res, 502, `Gemini request failed (${e.name}).`, 'upstream_unreachable');
  }
  const raw = await upstream.text();
  let g;
  try { g = JSON.parse(raw); } catch { g = null; }
  console.log(`gemini ${r.model} -> HTTP ${upstream.status} in ${Date.now() - started} ms`);
  if (!upstream.ok || !g) {
    return fail(res, upstream.status >= 400 ? upstream.status : 502,
      g?.error?.message ?? `Gemini returned HTTP ${upstream.status}.`, g?.error?.status ?? 'upstream_error');
  }
  send(res, 200, toResponsesResponse(g, r.model));
}

const server = http.createServer(async (req, res) => {
  const path = new URL(req.url, 'http://gateway').pathname.replace(/\/$/, '');
  try {
    if (req.method === 'GET' && path === '/health') return send(res, 200, { status: 'ok' });
    if (req.method === 'POST' && (path === '/openai/v1/responses' || path === '/v1/responses')) return await handleResponses(req, res);
    fail(res, 404, `Unsupported route ${req.method} ${path}.`, 'not_found');
  } catch (e) {
    console.error(`gateway error: ${e.message}`);
    if (!res.headersSent) fail(res, 500, 'Gateway error.');
  }
});

if (process.argv[1] && import.meta.url.endsWith(process.argv[1].split('/').pop())) {
  server.listen(PORT, () => console.log(`gemini-gateway listening on :${PORT}, upstream ${GEMINI}`));
}
