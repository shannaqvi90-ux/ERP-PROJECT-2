/**
 * The team's sign-in address: the product's address with the e-mail domain everyone in the team
 * signs in with (`/?domain=alnoor.example`). On it the sign-in screen fills in the domain, so a
 * person types only the part of the e-mail before "@", on any device, the first time too. Nothing
 * is looked up: the address carries the domain, so the screen reveals nothing about any workspace.
 */

/** A host name such as "alnoor.example": letters, digits and hyphens in dot-separated labels. */
const domainPattern = /^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]{0,62}$/;

/** The domain the address names, or null when it names none (or not a valid host name). */
export function teamDomain(search: string): string | null {
  const value = new URLSearchParams(search).get("domain")?.trim().toLowerCase() ?? "";
  return domainPattern.test(value) ? value : null;
}

/** What the person typed, completed with the team's domain when they typed no "@". */
export function fullEmail(typed: string, domain: string | null): string {
  const value = typed.trim();
  return domain && value && !value.includes("@") ? `${value}@${domain}` : value;
}

/**
 * Whether the field now holds a whole e-mail address in the team's domain ("sara@alnoor.example"
 * on the address of alnoor.example): nothing more can follow it, so the screen moves on to the
 * password, as Tab or Enter would. Only the team's own domain ends the field: elsewhere the screen
 * cannot know where an address ends.
 */
export function completesTeamEmail(typed: string, domain: string | null): boolean {
  if (!domain) return false;
  const value = typed.trim().toLowerCase();
  const at = value.indexOf("@");
  return at > 0 && at === value.lastIndexOf("@") && !/\s/.test(value) && value.slice(at + 1) === domain;
}

/** The text the e-mail field starts with: the part before "@" when the e-mail is in the team's domain. */
export function localPart(email: string, domain: string | null): string {
  return domain && email.toLowerCase().endsWith(`@${domain}`) ? email.slice(0, -domain.length - 1) : email;
}

/** The sign-in address for the team of this e-mail (the product's own address when it has no valid domain). */
export function teamSignInAddress(origin: string, email: string): string {
  const domain = email.slice(email.lastIndexOf("@") + 1).toLowerCase();
  return email.includes("@") && domainPattern.test(domain) ? `${origin}/?domain=${domain}` : `${origin}/`;
}
