import { HomeCard } from "../../kernel/home";

/** Home card: background jobs that failed (work waiting for the user), wait or run. */
export function JobsHomeCard() {
  return (
    <HomeCard
      titleKey="jobs.home.title"
      figures={[
        { key: "jobs.failed", labelKey: "jobs.home.failed", endpoint: "/api/jobs/jobs", filter: "jobStatus eq 'failed'", path: "/jobs/jobs", attention: true },
        { key: "jobs.waiting", labelKey: "jobs.home.waiting", endpoint: "/api/jobs/jobs", filter: "jobStatus eq 'queued'", path: "/jobs/jobs" },
        { key: "jobs.running", labelKey: "jobs.home.running", endpoint: "/api/jobs/jobs", filter: "jobStatus eq 'running'", path: "/jobs/jobs" },
      ]}
      action={{ labelKey: "jobs.home.open", path: "/jobs/jobs" }}
    />
  );
}
