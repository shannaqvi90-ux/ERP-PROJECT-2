import { HomeCard } from "../../kernel/home";

/** Home card: how many contacts the workspace holds and how many are archived. */
export function ContactsHomeCard() {
  return (
    <HomeCard
      titleKey="contacts.home.title"
      figures={[
        { key: "contacts.all", labelKey: "contacts.home.all", endpoint: "/api/contacts/contacts", filter: "isActive eq true", path: "/contacts/contacts", trendColumn: "createdAt" },
        { key: "contacts.archived", labelKey: "contacts.home.archived", endpoint: "/api/contacts/contacts", filter: "isActive eq false", path: "/contacts/contacts" },
      ]}
      action={{ labelKey: "contacts.home.new", path: "/contacts/contacts" }}
    />
  );
}
