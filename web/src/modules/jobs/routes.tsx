import type { RouteDef } from "../../kernel/router";
import { JobsPage } from "./JobsPage";
import { SchedulesPage } from "./SchedulesPage";

export const routes: RouteDef[] = [
  { path: "/jobs/jobs", titleKey: "jobs.menu.jobs", permission: "jobs.jobs.read", component: JobsPage },
  { path: "/jobs/schedules", titleKey: "jobs.menu.schedules", permission: "jobs.schedules.read", component: SchedulesPage },
];
