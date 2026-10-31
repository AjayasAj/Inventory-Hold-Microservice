import { useState, type FormEvent } from 'react';
import { errorMessage } from '../api/client';
import { useCreateHold, useInventory } from '../api/queries';
import type { Hold } from '../api/types';

interface Line {
  key: number;
  productId: string;
  quantity: number;
}

let nextKey = 1;
const newLine = (productId = ''): Line => ({ key: nextKey++, productId, quantity: 1 });

interface Props {
  onCreated: (hold: Hold) => void;
}

export function CreateHoldForm({ onCreated }: Props) {
  const { data: inventory = [], isPending: loadingInventory } = useInventory();
  const createHold = useCreateHold();
  const [lines, setLines] = useState<Line[]>([newLine()]);

  const stockOf = (productId: string) => inventory.find((p) => p.productId === productId)?.availableQuantity ?? 0;
  const update = (key: number, patch: Partial<Line>) =>
    setLines((current) => current.map((line) => (line.key === key ? { ...line, ...patch } : line)));
  const remove = (key: number) => setLines((current) => current.filter((line) => line.key !== key));

  const chosen = new Set(lines.map((line) => line.productId));
  const canAddLine = lines.length < inventory.length;

  // Client-side checks give instant feedback; the server remains the source of truth (stock can change).
  const problems = lines.flatMap((line) => {
    if (!line.productId) return ['Choose a product for every line.'];
    if (!Number.isInteger(line.quantity) || line.quantity < 1) return ['Quantities must be whole numbers of at least 1.'];
    if (line.quantity > stockOf(line.productId)) return [`Only ${stockOf(line.productId)} of ${line.productId} available.`];
    return [];
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (problems.length > 0) return;
    createHold.mutate(
      { items: lines.map(({ productId, quantity }) => ({ productId, quantity })) },
      {
        onSuccess: (hold) => {
          setLines([newLine()]);
          onCreated(hold);
        },
      },
    );
  };

  return (
    <section className="panel">
      <h2>Create a hold</h2>
      <form onSubmit={submit} noValidate>
        {lines.map((line, index) => (
          <div className="line" key={line.key}>
            <label>
              {index === 0 && 'Product'}
              <select
                value={line.productId}
                disabled={loadingInventory || createHold.isPending}
                onChange={(e) => update(line.key, { productId: e.target.value })}
                aria-label={`Product for line ${index + 1}`}
              >
                <option value="">{loadingInventory ? 'Loading…' : 'Select a product'}</option>
                {inventory.map((product) => (
                  <option
                    key={product.productId}
                    value={product.productId}
                    disabled={product.productId !== line.productId && (chosen.has(product.productId) || product.availableQuantity === 0)}
                  >
                    {product.name} ({product.availableQuantity} available)
                  </option>
                ))}
              </select>
            </label>
            <label className="qty">
              {index === 0 && 'Qty'}
              <input
                type="number"
                min={1}
                max={stockOf(line.productId) || undefined}
                value={Number.isNaN(line.quantity) ? '' : line.quantity}
                disabled={createHold.isPending}
                onChange={(e) => update(line.key, { quantity: e.target.valueAsNumber })}
                aria-label={`Quantity for line ${index + 1}`}
              />
            </label>
            <button
              type="button"
              className="icon"
              onClick={() => remove(line.key)}
              disabled={lines.length === 1 || createHold.isPending}
              aria-label={`Remove line ${index + 1}`}
            >
              ×
            </button>
          </div>
        ))}

        <button type="button" className="link" onClick={() => setLines((c) => [...c, newLine()])} disabled={!canAddLine || createHold.isPending}>
          + Add another product
        </button>

        {problems.length > 0 && lines.some((l) => l.productId) && <p className="hint warn">{problems[0]}</p>}
        {createHold.isError && (
          <div className="alert error" role="alert">
            {errorMessage(createHold.error)}
          </div>
        )}

        <button type="submit" disabled={createHold.isPending || loadingInventory || problems.length > 0}>
          {createHold.isPending ? 'Placing hold…' : 'Place hold'}
        </button>
      </form>
    </section>
  );
}
