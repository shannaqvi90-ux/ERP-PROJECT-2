import { useEffect, useState } from "react";

/**
 * Polite announcements for screen readers ("The interface is now in English."). One live region
 * for the whole app; anything may call announce().
 */
type Listener = (text: string) => void;
const listeners = new Set<Listener>();

export function announce(text: string): void {
  for (const listener of listeners) listener(text);
}

export function Announcer() {
  const [text, setText] = useState("");
  useEffect(() => {
    let timer = 0;
    const listener: Listener = (next) => {
      // Clear first so the same text twice is announced twice.
      setText("");
      window.clearTimeout(timer);
      timer = window.setTimeout(() => setText(next), 50);
    };
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
      window.clearTimeout(timer);
    };
  }, []);
  return (
    <div className="visually-hidden" role="status" aria-live="polite" data-testid="announcer">
      {text}
    </div>
  );
}
