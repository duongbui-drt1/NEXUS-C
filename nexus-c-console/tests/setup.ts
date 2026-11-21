import '@testing-library/jest-dom/vitest';
import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';
import { tokenStore } from '../src/services/auth/tokenStore';

afterEach(() => {
  cleanup();
  tokenStore.clear();
  window.localStorage.clear();
});
