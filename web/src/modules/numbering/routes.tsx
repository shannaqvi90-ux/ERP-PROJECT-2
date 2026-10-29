import type { RouteDef } from "../../kernel/router";
import { SequencesPage } from "./SequencesPage";

export const routes: RouteDef[] = [
  { path: "/numbering/sequences", titleKey: "numbering.menu.sequences", permission: "numbering.sequences.read", component: SequencesPage },
];
