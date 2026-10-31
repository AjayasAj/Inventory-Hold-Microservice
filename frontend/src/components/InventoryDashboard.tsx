import { errorMessage } from '../api/client';
import { useInventory } from '../api/queries';

const LOW_STOCK = 5;

export function InventoryDashboard() {
  const { data: inventory, isPending, isError, error, isFetching, refetch } = useInventory();

  return (
    <section className="panel" aria-busy={isFetching}>
      <div className="panel-head">
        <h2>Inventory</h2>
        <button className="secondary" onClick={() => refetch()} disabled={isFetching}>
          {isFetching ? 'Refreshing…' : 'Refresh'}
        </button>
      </div>

      {isPending ? (
        <div className="products">
          {Array.from({ length: 5 }, (_, i) => <div key={i} className="product skeleton" />)}
        </div>
      ) : isError ? (
        <div className="alert error" role="alert">
          {errorMessage(error)} <button className="link" onClick={() => refetch()}>Retry</button>
        </div>
      ) : inventory.length === 0 ? (
        <p className="empty">No products.</p>
      ) : (
        <div className="products">
          {inventory.map((product) => (
            <article key={product.productId} className={`product ${product.availableQuantity === 0 ? 'out' : product.availableQuantity <= LOW_STOCK ? 'low' : ''}`}>
              <span className="sku">{product.productId}</span>
              <h3>{product.name}</h3>
              <div className="stock">
                {product.availableQuantity}
                <small> available</small>
              </div>
            </article>
          ))}
        </div>
      )}
    </section>
  );
}
