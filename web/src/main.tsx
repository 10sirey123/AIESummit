import { createRoot } from 'react-dom/client';
import { useState } from 'react';
import aiesummitMark from '../../docs/assets/aiesummit_logo_only_large.png';
import { SignIn } from './sign-in';
import { Workspace, type OpenRequest } from './workspace';
import { CompareView } from './compare-view';
import { PortfolioView } from './portfolio-view';
import { resetExtractions } from './extraction-store';
import { PlaybookView } from './playbook-view';
import type { Session } from './api';
import './styles.css';

type View = 'contracts' | 'compare' | 'portfolio' | 'playbook';
const viewLabels: Record<View, string> = { contracts: 'Contracts', compare: 'Compare', portfolio: 'Portfolio', playbook: 'Playbook' };

function App() {
  const [session, setSession] = useState<Session>();
  const [view, setView] = useState<View>('contracts');
  const [openRequest, setOpenRequest] = useState<OpenRequest>();
  const open = (request: Omit<OpenRequest, 'request'>) => { setOpenRequest(previous => ({ ...request, request: (previous?.request ?? 0) + 1 })); setView('contracts'); };
  return (
    <>
      <header className="site-header">
        <div className="shell header-inner">
          <span className="brand"><img src={aiesummitMark} width="30" height="30" alt="" />ClauseLens <span className="brand-sub">(AIESummit)</span></span>
          {session && <nav className="main-nav" aria-label="Main">
            {(['contracts', 'compare', 'portfolio', 'playbook'] as const).map(item => <button key={item} type="button" aria-current={view === item ? 'page' : undefined} onClick={() => setView(item)}>
              {viewLabels[item]}
            </button>)}
          </nav>}
          {session && <button className="button secondary" onClick={() => { session.close(); resetExtractions(); setSession(undefined); setView('contracts'); }}>Sign out</button>}
        </div>
      </header>
      {!session ? <SignIn onSignIn={setSession} />
        : view === 'playbook' ? <PlaybookView session={session} onOpenContracts={() => setView('contracts')} />
        : view === 'compare' ? <CompareView session={session} onOpen={open} />
        : view === 'portfolio' ? <PortfolioView session={session} onOpen={open} />
        : <Workspace session={session} onOpenPlaybook={() => setView('playbook')} openRequest={openRequest} />}
      <footer className="shell site-footer">
        <span>ClauseLens (AIESummit)</span><span>Contract review workspace</span>
      </footer>
    </>
  );
}

createRoot(document.getElementById('root')!).render(<App />);
