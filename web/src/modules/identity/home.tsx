import { HomeCard } from "../../kernel/home";

/** Home card: users who were invited and never signed in (work waiting for the administrator), and inactive users. */
export function IdentityHomeCard() {
  return (
    <HomeCard
      titleKey="identity.home.title"
      figures={[
        { key: "identity.neverSignedIn", labelKey: "identity.home.neverSignedIn", endpoint: "/api/identity/users", filter: "lastSignInAt is null and isActive eq true", path: "/identity/users", attention: true },
        { key: "identity.inactive", labelKey: "identity.home.inactive", endpoint: "/api/identity/users", filter: "isActive eq false", path: "/identity/users" },
      ]}
      action={{ labelKey: "identity.home.open", path: "/identity/users" }}
    />
  );
}
