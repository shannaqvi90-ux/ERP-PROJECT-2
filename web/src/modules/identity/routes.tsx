import type { RouteDef } from "../../kernel/router";
import { RolesPage } from "./RolesPage";
import { UsersPage } from "./UsersPage";

export const routes: RouteDef[] = [
  { path: "/identity/users", titleKey: "identity.menu.users", permission: "identity.users.read", component: UsersPage },
  { path: "/identity/roles", titleKey: "identity.menu.roles", permission: "identity.roles.read", component: RolesPage },
];
