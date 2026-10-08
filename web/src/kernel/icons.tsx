/**
 * The shell's icons, drawn inline (no icon font, no network). Icons that point somewhere
 * (chevrons, arrows, sign-out) are marked directional and mirror on right-to-left screens; icons
 * that do not imply a direction (search, help, globe) never mirror. Icons are decorative: the
 * control that holds one carries the accessible name.
 */
const paths = {
  menu: { d: "M3 5h14M3 10h14M3 15h14", directional: false },
  search: { d: "M8.5 3a5.5 5.5 0 1 1 0 11a5.5 5.5 0 0 1 0-11zM12.5 12.5L17 17", directional: false },
  chevron: { d: "M7.5 4.5L13 10l-5.5 5.5", directional: true },
  back: { d: "M12.5 4.5L7 10l5.5 5.5", directional: true },
  globe: { d: "M10 2.5a7.5 7.5 0 1 1 0 15a7.5 7.5 0 0 1 0-15zM2.5 10h15M10 2.5c2.2 2.4 2.2 12.6 0 15M10 2.5c-2.2 2.4-2.2 12.6 0 15", directional: false },
  help: { d: "M10 2.5a7.5 7.5 0 1 1 0 15a7.5 7.5 0 0 1 0-15zM7.8 7.8a2.3 2.3 0 1 1 3.2 2.1c-.7.3-1 .8-1 1.5v.6M10 14.3v.2", directional: false },
  user: { d: "M10 3a3.2 3.2 0 1 1 0 6.4A3.2 3.2 0 0 1 10 3zM3.8 17c.6-3.2 3.1-5 6.2-5s5.6 1.8 6.2 5", directional: false },
  signOut: { d: "M8 3.5H4.5v13H8M12.5 6.5L16 10l-3.5 3.5M16 10H8", directional: true },
  home: { d: "M3 9.5L10 3.5l7 6M5 8v8.5h4v-5h2v5h4V8", directional: false },
  close: { d: "M5 5l10 10M15 5L5 15", directional: false },
  print: { d: "M5.5 7.5V3h9v4.5M5.5 14H3.5V8h13v6h-2M5.5 11.5h9V17h-9z", directional: false },
  keyboard: { d: "M2.5 5.5h15v9h-15zM5 8h1M8 8h1M11 8h1M14 8h1M6 11.5h8", directional: false },
  check: { d: "M4 10.5l4 4L16 6", directional: false },
  screen: { d: "M3 4h14v10H3zM7 17h6", directional: false },
  bolt: { d: "M11 2.5L4.5 11H10l-1 6.5L15.5 9H10z", directional: false },
  record: { d: "M5 3h7l3 3v11H5zM8 9h4M8 12h4", directional: false },
} as const;

export type IconName = keyof typeof paths;

export const isDirectional = (name: IconName): boolean => paths[name].directional;

export function Icon({ name, size = 16, className }: { name: IconName; size?: number; className?: string }) {
  const icon = paths[name];
  const classes = ["icon", icon.directional ? "icon-directional" : "", className ?? ""].filter(Boolean).join(" ");
  return (
    <svg
      className={classes}
      width={size}
      height={size}
      viewBox="0 0 20 20"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      data-icon={name}
    >
      <path d={icon.d} />
    </svg>
  );
}
