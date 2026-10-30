/** API shapes of the jobs module (times are ISO 8601 text). */

export type JobStatus = "queued" | "running" | "succeeded" | "failed" | "cancelled";
export type JobSource = "user" | "schedule" | "module" | "rerun";

/** A row of the background jobs list (its keys are the list's column keys). */
export type JobRow = {
  id: string;
  jobType: string;
  titleEn: string;
  titleAr: string;
  jobStatus: JobStatus;
  startedBy: JobSource;
  progressPercent: number;
  progressEn: string;
  progressAr: string;
  runAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  tryCount: number;
  maxAttempts: number;
  errorCode: string | null;
  errorEn: string;
  errorAr: string;
  createdAt: string;
  updatedAt: string;
  version: number;
  canRerun: boolean;
  canCancel: boolean;
};

export type JobAttempt = {
  number: number;
  startedAt: string;
  finishedAt: string | null;
  outcome: "running" | "succeeded" | "failed" | "cancelled" | "timedOut" | "abandoned";
  errorCode: string | null;
  errorEn: string;
  errorAr: string;
};

/** A job as its panel reads it. */
export type Job = {
  id: string;
  type: string;
  typeNameEn: string;
  typeNameAr: string;
  titleEn: string;
  titleAr: string;
  status: JobStatus;
  source: JobSource;
  priority: number;
  progress: number;
  progressEn: string;
  progressAr: string;
  runAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  attempts: number;
  maxAttempts: number;
  cancelRequested: boolean;
  errorCode: string | null;
  errorEn: string;
  errorAr: string;
  payload: Record<string, unknown>;
  result: Record<string, unknown> | null;
  hasOutput: boolean;
  outputName: string | null;
  outputContentType: string | null;
  outputBytes: number;
  scheduleId: string | null;
  rerunOf: string | null;
  attemptLog: JobAttempt[];
  createdAt: string;
  updatedAt: string;
  version: number;
  canRerun: boolean;
  canCancel: boolean;
  canDownload: boolean;
};

export type JobType = {
  key: string;
  nameEn: string;
  nameAr: string;
  descriptionEn: string | null;
  descriptionAr: string | null;
  maxAttempts: number;
  timeoutSeconds: number;
  startable: boolean;
  schedulable: boolean;
};

export type ScheduleRow = {
  id: string;
  scheduleName: string;
  jobType: string;
  typeNameEn: string;
  typeNameAr: string;
  cronTiming: string;
  isEnabled: boolean;
  nextRunAt: string | null;
  lastRunAt: string | null;
  lastJobId: string | null;
  updatedAt: string;
  version: number;
  canUpdate: boolean;
};

export type Schedule = Omit<ScheduleRow, "scheduleName" | "jobType" | "cronTiming"> & {
  scheduleName: string;
  type: string;
  cron: string;
  payload: Record<string, unknown>;
  timeZone: string;
  createdAt: string;
};

export type ListPage<T> = { items: T[]; total: number; next?: string | null };

/** Sent when a job or schedule changes, so open screens read them again. */
export const jobsChanged = "jobs:changed";

export const isActive = (status: JobStatus) => status === "queued" || status === "running";
