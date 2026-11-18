import { useEffect, useRef, useState } from "react";

/**
 * The tabs of a long form: one per section, under the record header, each jumping to its section
 * (the sections stay in one scrolling page, so no field is hidden behind a tab and no keystroke is
 * added). The current section follows the scroll. They are a pointer helper: every section and
 * field stays in the Tab order, so the bar is out of it and hidden from screen readers.
 */
export function SectionTabs({ root }: { root: React.RefObject<HTMLElement | null> }) {
  const [sections, setSections] = useState<{ id: number; title: string }[]>([]);
  const [current, setCurrent] = useState(0);
  const found = useRef<HTMLElement[]>([]);

  useEffect(() => {
    const body = root.current;
    if (!body) return;
    const nodes = [...body.querySelectorAll<HTMLElement>(".form-section")].filter((n) => n.querySelector(":scope > legend"));
    found.current = nodes;
    setSections(nodes.map((n, id) => ({ id, title: n.querySelector(":scope > legend")?.textContent?.trim() ?? "" })));
    if (typeof IntersectionObserver === "undefined" || nodes.length === 0) return;
    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries.filter((e) => e.isIntersecting).sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)[0];
        if (visible) setCurrent(nodes.indexOf(visible.target as HTMLElement));
      },
      { rootMargin: "0px 0px -60% 0px" },
    );
    nodes.forEach((n) => observer.observe(n));
    return () => observer.disconnect();
  }, [root]);

  if (sections.length < 3) return null;
  return (
    <div className="section-tabs" aria-hidden="true">
      {sections.map((s) => (
        <button
          key={s.id}
          type="button"
          tabIndex={-1}
          className="section-tab"
          data-current={s.id === current || undefined}
          onClick={() => {
            found.current[s.id]?.scrollIntoView?.({ block: "start", behavior: "smooth" });
            setCurrent(s.id);
          }}
        >
          {s.title}
        </button>
      ))}
    </div>
  );
}
