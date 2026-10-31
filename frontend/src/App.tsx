import { useEffect, useState } from 'react';
import type { Hold } from './api/types';
import { ActiveHoldsList } from './components/ActiveHoldsList';
import { CreateHoldForm } from './components/CreateHoldForm';
import { InventoryDashboard } from './components/InventoryDashboard';

export default function App() {
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    if (!notice) return;
    const timer = window.setTimeout(() => setNotice(null), 4000);
    return () => window.clearTimeout(timer);
  }, [notice]);

  return (
    <main>
      <header>
        <div>
          <p className="eyebrow">Commerce platform</p>
          <h1>Inventory holds</h1>
          <p className="sub">Reserve stock temporarily while a customer checks out.</p>
        </div>
      </header>

      <div className="toast" role="status" aria-live="polite">
        {notice && <div className="alert success">{notice}</div>}
      </div>

      <InventoryDashboard />

      <div className="columns">
        <CreateHoldForm onCreated={(hold) => setNotice(`Hold ${hold.id.slice(0, 8)} placed for ${hold.items.length} product(s).`)} />
        <ActiveHoldsList onReleased={(hold) => setNotice(`Hold ${hold.id.slice(0, 8)} released; stock returned.`)} />
      </div>

      <footer>Inventory Hold Service · React + TypeScript + TanStack Query</footer>
    </main>
  );
}
