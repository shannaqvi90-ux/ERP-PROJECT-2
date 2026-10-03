import { useEffect, useId, useRef, type KeyboardEvent, type ReactNode, type RefObject } from "react";

const focusable =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * A modal dialog that keeps the keyboard inside it: focus moves in when it opens (to
 * `initialFocus`, else the first control), Tab and Shift+Tab cycle within it, Escape closes it,
 * and focus returns to whatever had it before when it closes.
 */
export function Dialog({
  title,
  onClose,
  children,
  className,
  initialFocus,
  hideTitle = false,
}: {
  title: string;
  onClose: () => void;
  children: ReactNode;
  className?: string;
  initialFocus?: RefObject<HTMLElement | null>;
  hideTitle?: boolean;
}) {
  const titleId = useId();
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const target = initialFocus?.current ?? ref.current?.querySelector<HTMLElement>(focusable) ?? ref.current;
    target?.focus();
    return () => {
      if (previous && previous.isConnected) previous.focus();
    };
    // Runs once per opening; the dialog is mounted only while open.
  }, []);

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      onClose();
      return;
    }
    if (event.key !== "Tab" || !ref.current) return;
    const items = [...ref.current.querySelectorAll<HTMLElement>(focusable)].filter((el) => el.offsetParent !== null || el === document.activeElement);
    if (items.length === 0) return;
    const first = items[0]!;
    const last = items[items.length - 1]!;
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  return (
    <div className="dialog-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        className={className ? `dialog ${className}` : "dialog"}
        onKeyDown={onKeyDown}
      >
        <h2 id={titleId} className={hideTitle ? "visually-hidden" : "dialog-title"}>
          {title}
        </h2>
        {children}
      </div>
    </div>
  );
}
