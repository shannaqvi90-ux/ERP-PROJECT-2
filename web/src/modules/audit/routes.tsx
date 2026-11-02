import type { RouteDef } from "../../kernel/router";
import { AuditLogPage } from "./AuditLogPage";

export const routes: RouteDef[] = [{ path: "/audit/log", titleKey: "audit.menu.log", permission: "audit.log.read", component: AuditLogPage }];
