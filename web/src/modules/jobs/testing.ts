/** Records the jobs screens' tests serve (API shapes as the server answers them). */
export const jobRow = () => ({
  id: "j1", jobType: "jobs.selfCheck", titleEn: "Self-check of the job queue", titleAr: "فحص ذاتي لطابور المهام", jobStatus: "succeeded", startedBy: "user", progressPercent: 100,
  progressEn: "Step 5 of 5", progressAr: "الخطوة 5 من 5", runAt: "2026-10-29T08:00:00Z", startedAt: "2026-10-29T08:00:01Z", finishedAt: "2026-10-29T08:00:03Z",
  tryCount: 1, maxAttempts: 1, errorCode: null, errorEn: "", errorAr: "", createdAt: "2026-10-29T08:00:00Z", updatedAt: "2026-10-29T08:00:03Z", version: 3,
  canRerun: true, canCancel: false,
});
export const failedRow = () => ({
  ...jobRow(), id: "j2", jobStatus: "failed", progressPercent: 40, tryCount: 3, maxAttempts: 3, errorCode: "jobs.unexpected",
  errorEn: "The job stopped because of an unexpected error. The details are in the server log.", errorAr: "توقفت المهمة بسبب خطأ غير متوقع. التفاصيل في سجل الخادم.",
});
/** A job as its panel reads it (the detail answer names the fields in full: type, status, source, progress, attempts). */
export const job = () => {
  const { jobType, jobStatus, startedBy, progressPercent, tryCount, ...rest } = jobRow();
  return {
    ...rest, type: jobType, status: jobStatus, source: startedBy, progress: progressPercent, attempts: tryCount,
    typeNameEn: "Self-check of the job queue", typeNameAr: "فحص ذاتي لطابور المهام", priority: 0, cancelRequested: false,
    payload: { steps: 5 }, result: { steps: 5 }, hasOutput: true, outputName: "self-check.txt", outputContentType: "text/plain", outputBytes: 60, scheduleId: null, rerunOf: null,
    attemptLog: [{ number: 1, startedAt: "2026-10-29T08:00:01Z", finishedAt: "2026-10-29T08:00:03Z", outcome: "succeeded", errorCode: null, errorEn: "", errorAr: "" }],
    canDownload: true,
  };
};
export const failedJob = () => ({
  ...job(), id: "j2", status: "failed", progress: 40, attempts: 3, maxAttempts: 3, errorCode: "jobs.unexpected",
  errorEn: "The job stopped because of an unexpected error. The details are in the server log.", errorAr: "توقفت المهمة بسبب خطأ غير متوقع. التفاصيل في سجل الخادم.",
  result: null, hasOutput: false, outputName: null, canDownload: false,
  attemptLog: [1, 2, 3].map((n) => ({ number: n, startedAt: "2026-10-29T08:00:01Z", finishedAt: "2026-10-29T08:00:02Z", outcome: "failed", errorCode: "jobs.unexpected", errorEn: "The job stopped because of an unexpected error. The details are in the server log.", errorAr: "توقفت المهمة بسبب خطأ غير متوقع. التفاصيل في سجل الخادم." })),
});
export const types = () => [
  { key: "jobs.selfCheck", nameEn: "Self-check of the job queue", nameAr: "فحص ذاتي لطابور المهام", descriptionEn: "Runs a few short steps.", descriptionAr: "ينفذ بضع خطوات قصيرة.", maxAttempts: 1, timeoutSeconds: 120, startable: true, schedulable: true },
  { key: "jobs.cleanup", nameEn: "Clean up finished jobs", nameAr: "تنظيف المهام المنتهية", descriptionEn: null, descriptionAr: null, maxAttempts: 2, timeoutSeconds: 1200, startable: true, schedulable: true },
];
export const scheduleRow = () => ({
  id: "s1", scheduleName: "Clean up finished jobs / تنظيف المهام المنتهية", jobType: "jobs.cleanup", typeNameEn: "Clean up finished jobs", typeNameAr: "تنظيف المهام المنتهية", cronTiming: "30 3 * * *",
  isEnabled: true, nextRunAt: "2026-10-30T23:30:00Z", lastRunAt: "2026-10-29T23:30:00Z", lastJobId: "j1", updatedAt: "2026-10-29T08:00:00Z", version: 2, canUpdate: true,
});
export const schedule = () => { const { scheduleName, jobType, cronTiming, ...rest } = scheduleRow(); return { ...rest, name: scheduleName, type: jobType, cron: cronTiming, payload: {}, timeZone: "Asia/Dubai", createdAt: "2026-10-01T00:00:00Z" }; };
