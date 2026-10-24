import type { RouteDef } from "../../kernel/router";
import { ContactsPage } from "./ContactsPage";

export const routes: RouteDef[] = [
  { path: "/contacts/contacts", titleKey: "contacts.menu.contacts", permission: "contacts.contacts.read", component: ContactsPage },
];
