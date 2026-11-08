/** API shapes of the custom fields module (src/Modules/CustomFields). */
import type { CustomFieldType } from "../../kernel/forms/customFields";

export type FieldOption = { value: string; labelEn: string; labelAr: string };

export type CustomField = {
  id: string;
  recordType: string;
  key: string;
  labelEn: string;
  labelAr: string;
  type: CustomFieldType;
  required: boolean;
  default: string | null;
  decimals: number;
  options: FieldOption[];
  lookupTarget: string | null;
  sortOrder: number;
  searchable: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  version: number;
  everyCompany: boolean;
};

export type RecordType = { table: string; labelKey: string; canBePointedAt: boolean; fieldCount: number };

export type ListPage<T> = { items: T[]; total: number; next: string | null };

export const fieldTypes: readonly CustomFieldType[] = ["text", "number", "date", "boolean", "select", "lookup"];

/** Raised on window after a field is saved (lists refresh). */
export const fieldsChanged = "customfields:changed";

export const fieldsPath = "/customfields/fields";
