import { useState } from 'react';

export interface Disclosure {
  open: boolean;
  show: () => void;
  close: () => void;
  toggle: () => void;
}

export function useDisclosure(initial = false): Disclosure {
  const [open, setOpen] = useState(initial);
  const [actions] = useState(() => ({
    show: () => setOpen(true),
    close: () => setOpen(false),
    toggle: () => setOpen((value) => !value),
  }));
  return { open, ...actions };
}
