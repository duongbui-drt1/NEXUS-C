import { useState } from 'react';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AppErrorBoundary, ToastProvider } from '../components/feedback/Feedback';
import { AuthProvider } from '../services/auth/AuthContext';
import { ApiClientError } from '../services/api/errors';

export function AppProviders({ children }: { children: ReactNode }) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 30_000,
            gcTime: 5 * 60_000,
            refetchOnWindowFocus: false,
            retry: (failureCount, error) => {
              // Do not retry 4xx client/auth/permission errors
              if (error instanceof ApiClientError && error.status >= 400 && error.status < 500) {
                return false;
              }
              return failureCount < 1;
            },
          },
        },
      }),
  );

  return (
    <AppErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <ToastProvider>
          <AuthProvider>{children}</AuthProvider>
        </ToastProvider>
      </QueryClientProvider>
    </AppErrorBoundary>
  );
}
