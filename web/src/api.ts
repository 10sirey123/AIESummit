export type Upload = {
  id: number;
  fileName: string;
  created: string;
  pageCount: number;
  status: 'queued' | 'splitting' | 'ready' | 'failed';
  error: string | null;
};

export type ImageReference = { id: number; page: number; reference: string };
export type DocumentResult = {
  id: number;
  title: string;
  firstPage: number;
  lastPage: number;
  markdown: string;
  images: ImageReference[];
};
export type UploadDetail = { upload: Upload; documents: DocumentResult[] };
type FirebaseConfig = { apiKey: string; emulator: boolean; emulatorPort: string };
type Identity = { email: string; idToken: string; refreshToken: string; expiresIn: string };
type Refresh = { id_token: string; refresh_token: string; expires_in: string };
export type AuthMode = 'signInWithPassword' | 'signUp';

const authErrors: Record<string, string> = {
  EMAIL_EXISTS: 'An account already uses this email. Try signing in.',
  INVALID_LOGIN_CREDENTIALS: 'Check your email and password, then try again.',
  INVALID_PASSWORD: 'Check your email and password, then try again.',
  EMAIL_NOT_FOUND: 'Check your email and password, then try again.',
  TOO_MANY_ATTEMPTS_TRY_LATER: 'Too many attempts. Wait a moment and try again.',
};

async function checked(response: Response) {
  if (response.ok) return response;
  const problem = await response.json().catch(() => ({}));
  const code = problem.error?.message;
  if (response.status === 401) throw new Error('Your session expired. Sign out and sign in again.');
  if (response.status === 404) throw new Error('This item is no longer available.');
  throw new Error(problem.detail || authErrors[code] || `The request failed (${response.status}). Try again.`);
}

async function json<T>(response: Response): Promise<T> {
  return (await checked(response)).json();
}

export async function authenticate(mode: AuthMode, email: string, password: string) {
  const config = await json<FirebaseConfig>(await fetch('/firebase-config'));
  const emulator = `http://${location.hostname}:${config.emulatorPort}`;
  const identityHost = config.emulator ? `${emulator}/identitytoolkit.googleapis.com` : 'https://identitytoolkit.googleapis.com';
  const tokenHost = config.emulator ? `${emulator}/securetoken.googleapis.com` : 'https://securetoken.googleapis.com';
  const key = encodeURIComponent(config.apiKey);
  const identity = await json<Identity>(await fetch(`${identityHost}/v1/accounts:${mode}?key=${key}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password, returnSecureToken: true }),
  }));

  const lifetime = new AbortController();
  let { idToken, refreshToken } = identity;
  let expires = expiry(identity.expiresIn);
  let refreshing: Promise<string> | undefined;

  async function refresh() {
    const result = await json<Refresh>(await fetch(`${tokenHost}/v1/token?key=${key}`, {
      method: 'POST',
      body: new URLSearchParams({ grant_type: 'refresh_token', refresh_token: refreshToken }),
      signal: lifetime.signal,
    }));
    idToken = result.id_token;
    refreshToken = result.refresh_token;
    expires = expiry(result.expires_in);
    return idToken;
  }

  async function request(path: string, options: RequestInit = {}) {
    const token = Date.now() < expires ? idToken : await (refreshing ??= refresh().finally(() => { refreshing = undefined; }));
    const signal = options.signal ? AbortSignal.any([lifetime.signal, options.signal]) : lifetime.signal;
    return fetch(`/api/v1${path}`, { ...options, signal, headers: { Authorization: `Bearer ${token}` } });
  }

  return {
    email: identity.email,
    close: () => lifetime.abort(),
    list: async (before?: number) => json<Upload[]>(await request(before ? `/uploads?before=${before}` : '/uploads')),
    detail: async (id: number, signal: AbortSignal) => json<UploadDetail>(await request(`/uploads/${id}`, { signal })),
    upload: async (file: File) => {
      const body = new FormData();
      body.set('file', file);
      return json<{ id: number }>(await request('/uploads', { method: 'POST', body }));
    },
    image: async (uploadId: number, imageId: number, signal: AbortSignal) =>
      (await checked(await request(`/uploads/${uploadId}/images/${imageId}`, { signal }))).blob(),
  };
}

function expiry(seconds: string) {
  return Date.now() + Math.max(0, Number(seconds) - 60) * 1000;
}

export type Session = Awaited<ReturnType<typeof authenticate>>;

export function errorMessage(error: unknown) {
  return error instanceof TypeError ? 'Could not reach the service. Check your connection and try again.'
    : error instanceof Error ? error.message : 'Something went wrong. Try again.';
}
