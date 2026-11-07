import { createRoot } from 'react-dom/client';
import { useState } from 'react';
import aiesummitMark from '../../docs/assets/aiesummit_logo_only_large.png';
import { SignIn } from './sign-in';
import { Workspace } from './workspace';
import type { Session } from './api';
import './styles.css';

function App() {
  const [session, setSession] = useState<Session>();
  return (
    <>
      <header className="site-header">
        <div className="shell header-inner">
          <span className="brand"><img src={aiesummitMark} width="30" height="30" alt="" />AIESummit</span>
          {session && <button className="button secondary" onClick={() => { session.close(); setSession(undefined); }}>Sign out</button>}
        </div>
      </header>
      {session ? <Workspace session={session} /> : <SignIn onSignIn={setSession} />}
      <footer className="shell site-footer">
        <span>AIESummit</span><span>Document workspace</span>
      </footer>
    </>
  );
}

createRoot(document.getElementById('root')!).render(<App />);
