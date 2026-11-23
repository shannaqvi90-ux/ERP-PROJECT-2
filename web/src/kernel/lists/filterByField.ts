import { createContext } from "react";

/**
 * Offered by a list to the record form it opens beside itself: "list the records that hold this
 * value of this field". The list closes the form, chooses the field as a column and filters by it.
 * Used by a record's custom fields (kernel/forms/customFields.tsx), whose columns the list shows
 * only once chosen. Null outside a list.
 */
export const FilterByFieldContext = createContext<((column: string, value: string) => void) | null>(null);

/**
 * Offered by a list to the cells it draws: "list the records whose column contains this word". A cell that shows words of its own
 * (a contact's tags) draws each as a button that calls it; the list keeps its other filters, replaces the column's own and filters.
 * Null outside a list.
 */
export const FilterByWordContext = createContext<((column: string, word: string) => void) | null>(null);
