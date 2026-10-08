import type { RouteDef } from "../../kernel/router";
import { HomePage } from "./HomePage";

export const routes: RouteDef[] = [{ path: "/", titleKey: "shell.home.title", component: HomePage }];
