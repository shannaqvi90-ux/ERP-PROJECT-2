/** The contacts directory as the API answers it (src/Modules/Contacts). */

export type ContactKind = "organisation" | "person";

export const emirates = ["abuDhabi", "dubai", "sharjah", "ajman", "ummAlQuwain", "rasAlKhaimah", "fujairah"] as const;
export type Emirate = (typeof emirates)[number];

/** A money value: decimals travel as strings, never binary floats (CLAUDE.md rule 2). */
export type Money = { amount: string; currency: string; exchangeRate: string; baseAmount: string; baseCurrency: string };

export type Contact = {
  id: string;
  kind: ContactKind;
  reference: string | null;
  nameEn: string;
  nameAr: string;
  organisationId: string | null;
  organisationName: string | null;
  organisationNameAr: string | null;
  jobTitle: string | null;
  email: string | null;
  phone: string | null;
  mobile: string | null;
  website: string | null;
  addressLine1: string | null;
  addressLine2: string | null;
  area: string | null;
  city: string | null;
  emirate: Emirate | null;
  poBox: string | null;
  country: string;
  addressAr: string | null;
  tags: string[];
  tradeLicenceNumber: string | null;
  taxRegistrationNumber: string | null;
  defaultCurrency: string;
  creditLimit: Money | null;
  notes: string | null;
  isActive: boolean;
  peopleCount: number;
  createdAt: string;
  updatedAt: string;
  version: number;
};

export type ContactRow = {
  id: string;
  kind: ContactKind;
  reference: string | null;
  nameEn: string;
  nameAr: string;
  organisationId: string | null;
  organisationName: string | null;
  organisationNameAr: string | null;
  jobTitle: string | null;
  email: string | null;
  mobile: string | null;
  phone: string | null;
  city: string | null;
  emirate: Emirate | null;
  isActive: boolean;
  version: number;
};

export type ContactDuplicate = {
  id: string;
  kind: ContactKind;
  reference: string | null;
  nameEn: string;
  nameAr: string;
  email: string | null;
  phone: string | null;
  mobile: string | null;
  taxRegistrationNumber: string | null;
  isActive: boolean;
  matches: ("name" | "email" | "phone" | "taxRegistrationNumber")[];
};

export type ContactDefaults = { country: string; defaultCurrency: string; baseCurrency: string };

export type ListPage<T> = { items: T[]; total: number; next: string | null };

/** Raised on window after a contact is saved, archived or restored (lists and forms refresh). */
export const contactsChanged = "contacts:changed";

/** The screen's path; a record opens at <path>/<id>. */
export const contactsPath = "/contacts/contacts";

/** A contact's name in the screen's language: the Arabic one on Arabic screens when it has one. */
export function contactName(contact: { nameEn: string; nameAr: string }, language: string): string {
  return language === "ar" ? contact.nameAr || contact.nameEn : contact.nameEn || contact.nameAr;
}

/** A person's organisation's name in the screen's language. */
export function organisationName(contact: { organisationName: string | null; organisationNameAr: string | null }, language: string): string {
  return contactName({ nameEn: contact.organisationName ?? "", nameAr: contact.organisationNameAr ?? "" }, language);
}

/**
 * A Tax Registration Number as the form keeps it while it is typed (docs/compliance/vat-trn-format.md,
 * owner decision 2026-10-17): the Arabic-Indic digits ٠ to ٩ of an Arabic keyboard become 0 to 9 at
 * once. Spaces and dashes are removed by the server when it saves; other digit sets are refused there.
 */
export function trnDigits(typed: string): string {
  return typed.replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660));
}

/** Tags typed as one line ("Customer, VIP"): trimmed, each once (case ignored), in the order typed. */
export function parseTags(text: string): string[] {
  const kept: string[] = [];
  for (const part of text.split(/[,،;]/)) {
    const tag = part.trim();
    if (tag && !kept.some((k) => k.toLowerCase() === tag.toLowerCase())) kept.push(tag);
  }
  return kept;
}

/** The query of a duplicate check for what the form holds, or null when nothing worth comparing was typed. */
export function duplicateQuery(draft: { nameEn: string; nameAr: string; email: string; phone: string; mobile: string; taxRegistrationNumber: string }, except: string | null): string | null {
  const query = new URLSearchParams();
  const put = (key: string, value: string, min: number) => {
    const trimmed = value.trim();
    if (trimmed.length >= min) query.set(key, trimmed);
  };
  put("nameEn", draft.nameEn, 3);
  put("nameAr", draft.nameAr, 3);
  put("email", draft.email.includes("@") ? draft.email : "", 3);
  // A phone field holding only the +971 it keeps is not a number yet.
  put("phone", draft.phone.replace(/^\+971\s*$/, ""), 7);
  put("mobile", draft.mobile.replace(/^\+971\s*$/, ""), 7);
  put("taxRegistrationNumber", trnDigits(draft.taxRegistrationNumber).replace(/[ -]/g, "").length === 15 ? draft.taxRegistrationNumber : "", 15);
  if ([...query.keys()].length === 0) return null;
  if (except) query.set("except", except);
  return query.toString();
}
