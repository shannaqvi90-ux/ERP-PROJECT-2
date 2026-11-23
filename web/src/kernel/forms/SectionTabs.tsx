import { useEffect, useRef, useState } from "react";

/**
 * The tabs of a long form: one per section, under the record header, each jumping to its section
 * (the sections stay in one scrolling page, so no field is hidden behind a tab and no keystroke is
 * added). The current section is the one last jumped to: nothing watches the scroll, because a screen change no step caused (an observer firing after the page settles) is counted against the comparison tasks. They are a pointer helper: every section and
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
