import { errorMessage } from '../api/client';
import { useActiveHolds } from '../api/queries';
import type { Hold } from '../api/types';
import { useNow } from '../lib/time';
import { HoldCard } from './HoldCard';

interface Props {
  onReleased: (hold: Hold) => void;
}

export function ActiveHoldsList({ onReleased }: Props) {
  const { data: holds, isPending, isError, error, refetch } = useActiveHolds();
  const now = useNow();

  return (
    <section className="panel">
      <h2>
        Active holds {holds && <span className="count">{holds.length}</span>}
      </h2>

      {isPending ? (
        <p className="empty">Loading holds…</p>
      ) : isError ? (
        <div className="alert error" role="alert">
          {errorMessage(error)} <button className="link" onClick={() => refetch()}>Retry</button>
        </div>
      ) : holds.length === 0 ? (
        <p className="empty">No active holds. Place one to reserve stock.</p>
      ) : (
        <div className="holdlist">
          {holds.map((hold) => (
            <HoldCard key={hold.id} hold={hold} now={now} onReleased={onReleased} />
          ))}
        </div>
      )}
    </section>
  );
}
