import { createContext } from "react";

/**
 * Offered by a list to the record form it opens beside itself: "list the records that hold this
 * value of this field". The list closes the form, chooses the field as a column and filters by it.
 * Used by a record's custom fields (kernel/forms/customFields.tsx), whose columns the list shows
 * only once chosen. Null outside a list.
 */
export const FilterByFieldContext = createContext<((column: string, value: string) => void) | null>(null);
