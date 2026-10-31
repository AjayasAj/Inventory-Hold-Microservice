import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { api } from './client';
import type { CreateHoldRequest, Hold } from './types';

/**
 * Server state lives in TanStack Query rather than component state: it gives caching, loading/error
 * flags, background refresh and — most importantly — one place to declare that a hold mutation makes
 * both the inventory and the hold list stale, so every view re-syncs without a manual refresh.
 */
export const queryKeys = {
  inventory: ['inventory'] as const,
  activeHolds: ['holds', 'active'] as const,
};

// Background polling picks up changes made by other clients and by server-side expiry.
const POLL_MS = 15_000;

export function useInventory() {
  return useQuery({ queryKey: queryKeys.inventory, queryFn: api.getInventory, refetchInterval: POLL_MS });
}

export function useActiveHolds() {
  return useQuery({ queryKey: queryKeys.activeHolds, queryFn: api.listActiveHolds, refetchInterval: POLL_MS });
}

/** Re-fetch everything a stock change can affect. */
export function useInvalidateStock() {
  const queryClient = useQueryClient();
  return useCallback(
    () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.inventory }),
        queryClient.invalidateQueries({ queryKey: queryKeys.activeHolds }),
      ]),
    [queryClient],
  );
}

export function useCreateHold() {
  const queryClient = useQueryClient();
  const invalidateStock = useInvalidateStock();
  return useMutation({
    mutationFn: (request: CreateHoldRequest) => api.createHold(request),
    onSuccess: (hold) => {
      // Show the new hold immediately; the invalidation below then reconciles with the server.
      queryClient.setQueryData<Hold[]>(queryKeys.activeHolds, (holds = []) => [hold, ...holds]);
    },
    // On failure too: a 409 means our view of stock was stale.
    onSettled: invalidateStock,
  });
}

export function useReleaseHold() {
  const invalidateStock = useInvalidateStock();
  return useMutation({
    mutationFn: (holdId: string) => api.releaseHold(holdId),
    // On failure too: 409/410 mean the hold already closed elsewhere.
    onSettled: invalidateStock,
  });
}
