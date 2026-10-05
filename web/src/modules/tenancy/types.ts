import type { Emirate } from "./ui";

export type CompanyRow = {
  id: string;
  code: string;
  legalNameEn: string;
  legalNameAr: string;
  baseCurrency: string;
  city: string | null;
  emirate: Emirate | null;
  branchCount: number;
  isActive: boolean;
  version: number;
};

export type Company = {
  id: string;
  code: string;
  legalNameEn: string;
  legalNameAr: string;
  tradeLicenceNumber: string | null;
  tradeLicenceAuthority: string | null;
  taxRegistrationNumber: string | null;
  baseCurrency: string;
  fiscalYearStartMonth: number;
  fiscalYearStartDay: number;
  addressLine1: string | null;
  addressLine2: string | null;
  city: string | null;
  emirate: Emirate | null;
  poBox: string | null;
  country: string;
  addressAr: string | null;
  phone: string | null;
  email: string | null;
  website: string | null;
  hasLogo: boolean;
  logoHash: string | null;
  isActive: boolean;
  branchCount: number;
  createdAt: string;
  updatedAt: string;
  version: number;
};

export type BranchRow = {
  id: string;
  companyId: string;
  companyCode: string;
  code: string;
  nameEn: string;
  nameAr: string;
  city: string | null;
  emirate: Emirate | null;
  isActive: boolean;
  version: number;
};

export type Branch = BranchRow & {
  addressLine1: string | null;
  addressLine2: string | null;
  poBox: string | null;
  country: string;
  addressAr: string | null;
  phone: string | null;
  email: string | null;
  createdAt: string;
  updatedAt: string;
};

export type WorkplaceBranch = { id: string; code: string; nameEn: string; nameAr: string };
export type WorkplaceCompany = { id: string; code: string; legalNameEn: string; legalNameAr: string; baseCurrency: string; branches: WorkplaceBranch[] };
export type Workplace = { companyId: string | null; branchId: string | null; companies: WorkplaceCompany[] };

export type AccessCompanySummary = { companyId: string; code: string; allBranches: boolean; branchCount: number };
export type AccessRow = { id: string; displayName: string; email: string; companies: AccessCompanySummary[] };
export type CompanyAccess = { companyId: string; allBranches: boolean; branchIds: string[] };
export type AccessOption = {
  id: string;
  code: string;
  legalNameEn: string;
  legalNameAr: string;
  isActive: boolean;
  branches: { id: string; code: string; nameEn: string; nameAr: string; isActive: boolean }[];
  /** False when the caller works in only some branches of the company: they give only those. */
  canGiveAllBranches?: boolean;
};
export type UserAccess = {
  userId: string;
  displayName: string;
  email: string;
  isCaller: boolean;
  companies: CompanyAccess[];
  options: AccessOption[];
  /** False when the caller may not change this user's access at all (see readOnlyReason). */
  canEdit?: boolean;
  /** Text key saying why the access is read-only for the caller. */
  readOnlyReason?: string | null;
  /** This state of the user's access in the caller's companies; a save sends it back (409 when stale). */
  version: number;
};

/** Fired on window when the user switches their working company or branch. */
export const workplaceChanged = "erp:workplace-changed";

/** Fired on window when a company or branch is created or changed (the switcher reloads). */
export const companiesChanged = "erp:companies-changed";
