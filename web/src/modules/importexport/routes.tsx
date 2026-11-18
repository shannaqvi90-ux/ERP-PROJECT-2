import type { RouteDef } from "../../kernel/router";
import { ExportsPage } from "./ExportsPage";

export const routes: RouteDef[] = [
  { path: "/importexport/exports", titleKey: "importexport.menu.exports", permission: "importexport.exports.read", component: ExportsPage },
];
