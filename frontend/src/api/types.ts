// Mirrors InventoryHold.Contracts. Dates are ISO-8601 UTC strings on the wire.

export type HoldStatus = 'Active' | 'Released' | 'Expired';

export interface InventoryItem {
  productId: string;
  name: string;
  availableQuantity: number;
}

export interface HoldItem {
  productId: string;
  name: string;
  quantity: number;
}

export interface Hold {
  id: string;
  status: HoldStatus;
  createdAtUtc: string;
  expiresAtUtc: string;
  closedAtUtc: string | null;
  items: HoldItem[];
}

export interface HoldItemRequest {
  productId: string;
  quantity: number;
}

export interface CreateHoldRequest {
  items: HoldItemRequest[];
}

/** RFC 7807 problem response returned by the API for every 4xx/5xx. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  /** Stable machine-readable code, e.g. "insufficient_stock". */
  code?: string;
  /** Present on model-binding validation failures. */
  errors?: Record<string, string[]>;
  [extension: string]: unknown;
}
