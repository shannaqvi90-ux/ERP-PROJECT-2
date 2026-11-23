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
  attach: { d: "M15.5 9.2l-5.6 5.6a3.4 3.4 0 0 1-4.8-4.8l6-6a2.3 2.3 0 0 1 3.3 3.3l-6 6a1.1 1.1 0 0 1-1.6-1.6l5.4-5.4", directional: false },
  sun: { d: "M10 6.5a3.5 3.5 0 1 1 0 7a3.5 3.5 0 0 1 0-7zM10 2v2M10 16v2M2 10h2M16 10h2M4.3 4.3l1.4 1.4M14.3 14.3l1.4 1.4M4.3 15.7l1.4-1.4M14.3 5.7l1.4-1.4", directional: false },
  moon: { d: "M16.5 11.5A6.5 6.5 0 0 1 8.5 3.5a6.5 6.5 0 1 0 8 8z", directional: false },
  contrast: { d: "M10 2.5a7.5 7.5 0 1 1 0 15a7.5 7.5 0 0 1 0-15zM10 2.5v15M10 2.5a7.5 7.5 0 0 1 0 15z", directional: false },
  calendar: { d: "M3.5 5h13v11.5h-13zM3.5 8.5h13M7 3v3M13 3v3", directional: false },
  pin: { d: "M7 3h6l-1 5 3 3H5l3-3zM10 11v6", directional: false },
  users: { d: "M7.5 3a3 3 0 1 1 0 6a3 3 0 0 1 0-6zM2 16.5c.5-3 2.7-4.6 5.5-4.6s5 1.6 5.5 4.6M13.5 4a2.6 2.6 0 1 1 0 5.2M14.5 12c2 .3 3.2 1.7 3.5 4", directional: false },
  building: { d: "M4.5 17V4h8v13M12.5 8h3v9M2.5 17h15M7 7h2M7 10h2M7 13h2", directional: false },
  coin: { d: "M10 2.5a7.5 7.5 0 1 1 0 15a7.5 7.5 0 0 1 0-15zM12.5 7.2c-.5-.8-1.400-1.2-2.5-1.2c-1.400 0-2.300.7-2.300 1.7c0 2.400 5 1.100 5 3.6c0 1-.9 1.800-2.400 1.800c-1.2 0-2.100-.5-2.6-1.300M10 4.800v1.2M10 14v1.2", directional: false },
  history: { d: "M3 10a7 7 0 1 0 2.2-5.100M3 3.5v3.5h3.5M10 6.5V10l2.5 1.5", directional: false },
  clock: { d: "M10 2.5a7.5 7.5 0 1 1 0 15a7.5 7.5 0 0 1 0-15zM10 6v4.2l2.800 1.800", directional: false },
  hash: { d: "M7.5 3L6 17M14 3l-1.5 14M3.5 7.5h13M3 12.5h13", directional: false },
  chart: { d: "M3.5 3v14h13M6.5 13V9.5M10 13V6M13.5 13V8.5", directional: false },
  upload: { d: "M10 13V3.5M6.5 7L10 3.5L13.5 7M3.5 12.5V16.5h13V12.5", directional: false },
  sliders: { d: "M3 6h8M15 6h2M3 14h2M9 14h8M13 3.5v5M7 11.5v5", directional: false },
  list: { d: "M3 5h14M3 10h14M3 15h14M3 5h.01M3 10h.01M3 15h.01", directional: false },
  star: { d: "M10 2.800l2.2 4.5 4.900.7-3.5 3.5.8 4.900-4.400-2.300-4.400 2.300.8-4.900L2.900 8l4.900-.7z", directional: false },
  more: { d: "M4.5 10h.01M10 10h.01M15.5 10h.01", directional: false },
  plus: { d: "M10 4v12M4 10h12", directional: false },
  edit: { d: "M3.5 16.5l1-4L13.5 3.5l3 3L7.5 15.5zM12 5l3 3", directional: false },
  archive: { d: "M3 4.5h14v3H3zM4.5 7.5V16h11V7.5M8 11h4", directional: false },
  save: { d: "M4 3.5h9l3 3V16.5H4zM7 3.5v4h5v-4M7 16.5v-5h6v5", directional: false },
  collapse: { d: "M5 7.5l5 5l5-5", directional: false },
  trend: { d: "M3 14l4.5-5l3 3L17 5M12.5 5H17v4.5", directional: false },
  warning: { d: "M10 3L18 16.5H2zM10 8v4M10 14.2v.2", directional: false },
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
