import "./jobs.css";
import { api } from "../../kernel/api";
import type { ModuleExtensions } from "../../kernel/extensions";
import { recordPath } from "../../kernel/router";
import type { JobRow, ListPage } from "./types";

/**
 * What the jobs module adds to the shell: G then J opens Background jobs and G then S the job
 * schedules; from anywhere a schedule added and the job list printed (starting a job is the screen's own Alt+N); and in the command palette the jobs
 * by title, kind or state ("clean up", "failed"), each opened in the list with its panel.
 */
export const extensions: ModuleExtensions = {
  actions: [
    { key: "jobs.jobs.print", labelKey: "jobs.action.printJobs", keywordsKey: "jobs.action.printKeywords", permission: "jobs.jobs.read", path: "/jobs/jobs", command: "lists.print" },
    { key: "jobs.schedules.new", labelKey: "jobs.action.newSchedule", keywordsKey: "jobs.action.newScheduleKeywords", permission: "jobs.schedules.create", path: "/jobs/schedules", command: "jobs.schedules.new" },
  ],
  goTo: [
    { key: "jobs.jobs", menu: "jobs.jobs", code: "KeyJ" },
    { key: "jobs.schedules", menu: "jobs.schedules", code: "KeyS" },
  ],
  palette: [
    {
      key: "jobs.jobs",
      labelKey: "jobs.palette.jobs",
      permission: "jobs.jobs.read",
      minLength: 3,
      search: async (query, { language, signal }) => {
        const params = new URLSearchParams({ search: query, take: "5" });
        const page = await api<ListPage<JobRow>>("GET", `/api/jobs/jobs?${params}`, undefined, { signal });
        return {
          total: page.total,
          items: page.items.map((j) => ({
            id: j.id,
            title: language === "ar" ? j.titleAr || j.titleEn : j.titleEn || j.titleAr,
            subtitle: j.status,
            path: recordPath("/jobs/jobs", j.id, `${new URLSearchParams({ q: query })}`),
          })),
        };
      },
      showAll: (query) => `/jobs/jobs?${new URLSearchParams({ q: query })}`,
    },
  ],
};
