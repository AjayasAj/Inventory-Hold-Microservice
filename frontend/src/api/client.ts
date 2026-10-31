import type { CreateHoldRequest, Hold, InventoryItem, ProblemDetails } from './types';

// Same-origin by default: Vite (dev) and nginx (Docker) proxy /api to the backend.
const API_BASE = import.meta.env.VITE_API_URL ?? '/api';

export class ApiError extends Error {
  readonly status: number;
  readonly code: string | undefined;
  readonly problem: ProblemDetails | undefined;

  constructor(status: number, message: string, problem?: ProblemDetails) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = problem?.code;
    this.problem = problem;
  }
}

function messageFrom(status: number, problem: ProblemDetails | undefined): string {
  if (problem?.errors) {
    const details = Object.values(problem.errors).flat();
    if (details.length > 0) return details.join(' ');
  }
  return problem?.detail ?? problem?.title ?? `Request failed with status ${status}.`;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${API_BASE}${path}`, {
      ...init,
      headers: { Accept: 'application/json', 'Content-Type': 'application/json', ...init?.headers },
    });
  } catch {
    throw new ApiError(0, 'Cannot reach the Inventory Hold API. Check that the backend is running.');
  }

  if (!response.ok) {
    const problem = (await response.json().catch(() => undefined)) as ProblemDetails | undefined;
    throw new ApiError(response.status, messageFrom(response.status, problem), problem);
  }

  return (response.status === 204 ? undefined : await response.json()) as T;
}

export const api = {
  getInventory: () => request<InventoryItem[]>('/inventory'),
  listActiveHolds: () => request<Hold[]>('/holds?status=Active&limit=100'),
  createHold: (body: CreateHoldRequest) =>
    request<Hold>('/holds', { method: 'POST', body: JSON.stringify(body) }),
  releaseHold: (holdId: string) =>
    request<Hold>(`/holds/${encodeURIComponent(holdId)}`, { method: 'DELETE' }),
};

export const errorMessage = (error: unknown): string =>
  error instanceof Error ? error.message : 'Something went wrong.';
