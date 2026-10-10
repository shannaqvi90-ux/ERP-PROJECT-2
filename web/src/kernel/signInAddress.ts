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

/**
 * The workspaces this device signed in to, by e-mail domain, so that on the plain address too the
 * screen knows where a whole address ends and moves on to the password (round 9: a shared counter
 * or warehouse PC, where the next person signs in after the Sign out button, from the plain
 * address). Nothing is looked up: the device keeps only what its own sign-ins taught it.
 *
 * Kept like the language (a device setting, `erp.signInDomains`, kept across sign-outs; see
 * kernel/deviceState.ts), and it names nobody: no e-mail and no readable domain, only salted
 * 64-bit hashes (FNV-1a) of the domains of the last few successful sign-ins, with a random salt of
 * this device. A domain can be recognised (the screen moves on) but not read back.
 */
export const signInDomainsKey = "erp.signInDomains";

/** The most domains a device keeps (the most recent first). */
export const keptSignInDomains = 8;

type KnownDomains = { salt: string; hashes: string[] };

/** FNV-1a, 64 bits, of the text's UTF-16 code units, as 16 hexadecimal digits. */
export function domainHash(text: string): string {
  let hash = 0xcbf29ce484222325n;
  for (let i = 0; i < text.length; i++) {
    hash ^= BigInt(text.charCodeAt(i));
    hash = (hash * 0x100000001b3n) & 0xffffffffffffffffn;
  }
  return hash.toString(16).padStart(16, "0");
}

const hashPattern = /^[0-9a-f]{16}$/;

function readKnown(): KnownDomains | null {
  try {
    const raw = localStorage.getItem(signInDomainsKey);
    if (!raw) return null;
    const value = JSON.parse(raw) as Partial<KnownDomains>;
    if (typeof value.salt !== "string" || !/^[0-9a-f]{32}$/.test(value.salt) || !Array.isArray(value.hashes)) return null;
    const hashes = value.hashes.filter((h): h is string => typeof h === "string" && hashPattern.test(h)).slice(0, keptSignInDomains);
    return { salt: value.salt, hashes };
  } catch {
    return null;
  }
}

function newSalt(): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  return [...bytes].map((b) => b.toString(16).padStart(2, "0")).join("");
}

/** The domain of a whole e-mail address (one "@", something before it, no spaces), or null. */
export function domainOfWholeEmail(typed: string): string | null {
  const value = typed.trim().toLowerCase();
  const at = value.indexOf("@");
  if (at <= 0 || at !== value.lastIndexOf("@") || /\s/.test(value)) return null;
  const domain = value.slice(at + 1);
  return domainPattern.test(domain) ? domain : null;
}

/** After a successful sign-in: this device knows the e-mail's domain from now on. */
export function rememberSignInDomain(email: string): void {
  const domain = domainOfWholeEmail(email);
  if (!domain) return;
  const known = readKnown() ?? { salt: newSalt(), hashes: [] };
  const hash = domainHash(`${known.salt}\n${domain}`);
  const hashes = [hash, ...known.hashes.filter((h) => h !== hash)].slice(0, keptSignInDomains);
  try {
    localStorage.setItem(signInDomainsKey, JSON.stringify({ salt: known.salt, hashes }));
  } catch {
    // Not kept on this device: the plain address keeps Enter to go on.
  }
}

/** Whether the device signed in to any workspace before (the screen then says that a whole address moves on). */
export function deviceKnowsSignInDomains(): boolean {
  return (readKnown()?.hashes.length ?? 0) > 0;
}

/** Whether the field holds a whole address in a domain this device signed in with. */
export function completesKnownEmail(typed: string): boolean {
  const domain = domainOfWholeEmail(typed);
  const known = domain ? readKnown() : null;
  return !!known && known.hashes.includes(domainHash(`${known.salt}\n${domain}`));
}
