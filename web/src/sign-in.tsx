import { useState, type FormEvent } from 'react';
import { ArrowRight, Files, ScanLine, TextAlignStart } from 'lucide-react';
import { authenticate, errorMessage, type AuthMode, type Session } from './api';

export function SignIn({ onSignIn }: { onSignIn: (session: Session) => void }) {
  const [mode, setMode] = useState<AuthMode>('signInWithPassword');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const creating = mode === 'signUp';

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;
    const fields = new FormData(event.currentTarget);
    setBusy(true);
    setError('');
    try {
      onSignIn(await authenticate(mode, String(fields.get('email')), String(fields.get('password'))));
    } catch (failure) {
      setError(errorMessage(failure));
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="shell welcome">
      <div className="welcome-copy">
        <h1>Bring order to<br className="desktop-break" /> your documents.</h1>
        <p>Turn PDFs and scans into organized Markdown. AIESummit reads each page, identifies where documents begin and end, and keeps the original page order.</p>
      </div>
      <section className="auth-area" aria-labelledby="auth-title">
        <div className="auth-panel">
          <h2 id="auth-title">{creating ? 'Create your account' : 'Sign in to AIESummit'}</h2>
          <p>{creating ? 'Start organizing your documents.' : 'Open your document workspace.'}</p>
          <form onSubmit={submit}>
            <label htmlFor="email">Email</label>
            <input id="email" name="email" type="email" autoComplete="username" placeholder="you@example.com" required />
            <label htmlFor="password">Password</label>
            <input id="password" name="password" type="password" autoComplete={creating ? 'new-password' : 'current-password'} minLength={creating ? 6 : undefined} required />
            {error && <p role="alert" className="error">{error}</p>}
            <button className="button primary auth-submit" disabled={busy}>
              {busy ? (creating ? 'Creating account…' : 'Signing in…') : (creating ? 'Create account' : 'Sign in')}
              <ArrowRight size={17} aria-hidden="true" />
            </button>
          </form>
          <p className="auth-switch">
            {creating ? 'Already have an account?' : 'New to AIESummit?'}{' '}
            <button type="button" className="text-button" disabled={busy} onClick={() => {
              setError('');
              setMode(creating ? 'signInWithPassword' : 'signUp');
            }}>{creating ? 'Sign in' : 'Create an account'}</button>
          </p>
        </div>
      </section>
      <ol className="process" aria-label="How AIESummit processes documents">
        <li><Files aria-hidden="true" /><div><strong>Upload a source</strong><span>PDF, PNG or JPEG</span></div></li>
        <li><ScanLine aria-hidden="true" /><div><strong>Identify documents</strong><span>OCR and AI-assisted splitting</span></div></li>
        <li><TextAlignStart aria-hidden="true" /><div><strong>Review the output</strong><span>Markdown and page ranges</span></div></li>
      </ol>
    </main>
  );
}
