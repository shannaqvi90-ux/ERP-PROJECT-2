/** The shapes of the import and full export endpoints (docs: /api/importexport/...). */

export type ImportField = { key: string; label: string; labelEn: string; labelAr: string; required: boolean; custom: boolean; example: string | null };

export type ImportFields = { items: ImportField[]; oneOfRequired: string[]; formats: string[]; maxFileBytes: number; maxRows: number; maxColumns: number; listPath: string | null };

export type ImportColumn = { index: number; header: string; field: string | null; source: "memory" | "name" | "you" | null; sample: string[] };

export type ImportIssue = { row: number; field: string; fieldLabel: string; code: string; message: string };

export type ImportSession = {
  id: string;
  recordType: string;
  fileName: string;
  format: string;
  status: "uploaded" | "running" | "checked" | "completed" | "failed";
  rowCount: number;
  columns: ImportColumn[];
  rows: string[][];
  onError: "reject" | "skip";
  dryRun: boolean;
  rowsChecked: number;
  rowsValid: number;
  rowsImported: number;
  rowsFailed: number;
  rowsDuplicate: number;
  rowsUpdated: number;
  onDuplicate: "create" | "skip" | "update";
  jobId: string | null;
  issues: ImportIssue[];
  failure: string | null;
  failureCode: string | null;
  hasErrorFile: boolean;
  missingFields: string[];
  createdAt: string;
  finishedAt: string | null;
  version: number;
};

export type TenantExport = {
  id: string;
  status: "queued" | "running" | "succeeded" | "failed" | "expired";
  format: "csv" | "json";
  jobId: string | null;
  fileCount: number;
  byteSize: number;
  recordCounts: Record<string, number>;
  failure: string | null;
  failureCode: string | null;
  createdAt: string;
  finishedAt: string | null;
  expiresAt: string | null;
  downloadCount: number;
  downloadedAt: string | null;
  downloadable: boolean;
};

export const importBase = (table: string) => `/api/importexport/${table}`;
