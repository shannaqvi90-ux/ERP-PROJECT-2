import "./tenancy.css";
import type { ShellItem } from "../../kernel/slots";
import { WorkplaceSwitcher } from "./WorkplaceSwitcher";

/** What the tenancy module adds to the app shell: the working company and branch switcher. */
export const topBarItems: ShellItem[] = [
  { key: "tenancy.workplace", order: 10, permission: "tenancy.workplace.read", component: WorkplaceSwitcher },
];
