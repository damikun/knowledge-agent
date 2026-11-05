import { useEffect, useEffectEvent, useState } from 'react';

/** Calls `onChange` after a render in which `value` changed, never on mount. */
export function useOnChange<T>(value: T, onChange: (value: T) => void): void {
  const [first] = useState(value);
  const [changed, setChanged] = useState(false);
  if (!changed && !Object.is(value, first)) {
    setChanged(true);
  }
  const run = useEffectEvent(onChange);
  useEffect(() => {
    if (changed) {
      run(value);
    }
  }, [changed, value]);
}
