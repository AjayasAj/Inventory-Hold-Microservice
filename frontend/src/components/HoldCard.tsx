import { useEffect, useState } from 'react';
import { errorMessage } from '../api/client';
import { useInvalidateStock, useReleaseHold } from '../api/queries';
import type { Hold } from '../api/types';
import { formatRemaining, msUntil } from '../lib/time';

interface Props {
  hold: Hold;
  now: number;
  onReleased: (hold: Hold) => void;
}

export function HoldCard({ hold, now, onReleased }: Props) {
  const release = useReleaseHold();
  const invalidateStock = useInvalidateStock();
  const [confirming, setConfirming] = useState(false);

  const remaining = msUntil(hold.expiresAtUtc, now);
  const expired = remaining <= 0;
  const urgent = !expired && remaining < 60_000;

  // When the countdown reaches zero, ask the server: it expires the hold and returns its stock.
  useEffect(() => {
    if (expired) void invalidateStock();
  }, [expired, invalidateStock]);

  const confirmRelease = () =>
    release.mutate(hold.id, {
      onSuccess: () => onReleased(hold),
      onSettled: () => setConfirming(false),
    });

  return (
    <article className={`hold ${expired ? 'expired' : ''}`}>
      <div className="holdtop">
        <code title={hold.id}>{hold.id.slice(0, 8)}</code>
        <span className={`status ${expired ? 'expired' : hold.status.toLowerCase()}`}>{expired ? 'Expiring…' : hold.status}</span>
      </div>

      <ul className="items">
        {hold.items.map((item) => (
          <li key={item.productId}>
            {item.name} <span className="muted">× {item.quantity}</span>
          </li>
        ))}
      </ul>

      <small className={urgent ? 'urgent' : undefined}>{formatRemaining(remaining)}</small>

      {release.isError && (
        <div className="alert error" role="alert">
          {errorMessage(release.error)}
        </div>
      )}

      {confirming ? (
        <div className="confirm" role="group" aria-label="Confirm release">
          <span>Release this hold and return its stock?</span>
          <button className="danger" onClick={confirmRelease} disabled={release.isPending} autoFocus>
            {release.isPending ? 'Releasing…' : 'Yes, release'}
          </button>
          <button className="secondary" onClick={() => setConfirming(false)} disabled={release.isPending}>
            Cancel
          </button>
        </div>
      ) : (
        <button className="danger" onClick={() => setConfirming(true)} disabled={expired}>
          Release hold
        </button>
      )}
    </article>
  );
}
