import type { RouteDef } from "../../kernel/router";
import { FieldsPage } from "./FieldsPage";

export const routes: RouteDef[] = [
  { path: "/customfields/fields", titleKey: "customfields.menu.fields", permission: "customfields.fields.read", component: FieldsPage },
];
