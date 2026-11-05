/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Absolute API origin, when the portal is not served from the API's origin. */
  readonly VITE_API_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}

// Fontsource packages are imported for their CSS only (see main.tsx).
declare module '@fontsource-variable/*';
