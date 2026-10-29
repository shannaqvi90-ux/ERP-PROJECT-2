/** API shapes of the numbering module. */
import type { Reset } from "./pattern";

/** A row of the document numbering list (its keys are the list's column keys). */
export type SequenceRow = {
  id: string;
  companyId: string;
  companyCode: string;
  documentType: string;
  nameEn: string;
  nameAr: string;
  prefix: string;
  suffix: string;
  padding: number;
  reset: Reset;
  gapless: boolean;
  perBranch: boolean;
  nextNumber: string;
  nextValue: number;
  workingCompany: boolean;
  updatedAt: string;
  version: number;
  canUpdate: boolean;
};

export type SequenceBranch = { id: string; code: string; nameEn: string; nameAr: string };

export type SequenceCounter = { branchId: string | null; branchCode: string | null; period: string; nextValue: number; lastIssued: number | null; lastIssuedAt: string | null };

export type SequenceIssued = { number: string; documentDate: string; issuedAt: string; gapless: boolean };

/** A sequence as its form reads it. */
export type Sequence = Omit<SequenceRow, "workingCompany"> & {
  companyNameEn: string;
  companyNameAr: string;
  previewBranchId: string | null;
  today: string;
  branches: SequenceBranch[];
  counters: SequenceCounter[];
  recent: SequenceIssued[];
  createdAt: string;
};

export type ListPage<T> = { items: T[]; total: number; next: string | null };

/** Sent when a sequence was saved, so other parts of the screen read it again. */
export const sequencesChanged = "numbering:sequences-changed";
