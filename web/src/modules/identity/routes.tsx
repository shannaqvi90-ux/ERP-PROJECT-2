import type { RouteDef } from "../../kernel/router";
import { MyAccountPage } from "./MyAccountPage";
import { RolesPage } from "./RolesPage";
import { UsersPage } from "./UsersPage";

export const routes: RouteDef[] = [
  { path: "/identity/users", titleKey: "identity.menu.users", permission: "identity.users.read", component: UsersPage },
  { path: "/identity/roles", titleKey: "identity.menu.roles", permission: "identity.roles.read", component: RolesPage },
  { path: "/identity/me", titleKey: "identity.menu.me", permission: "identity.profile.update", component: MyAccountPage },
];
