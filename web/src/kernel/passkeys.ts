/**
 * Passkeys in the browser (WebAuthn). The server's options and the device's answers travel as
 * JSON with every binary value in base64url; this file turns them into the ArrayBuffers
 * `navigator.credentials` takes and back, and keeps this device's one passkey setting.
 *
 * The device setting `erp.passkeyOffer` ("1" or "0") says whether the sign-in screen asks this
 * device for a passkey as soon as it opens. It names nobody (no e-mail, no id): it is set when a
 * passkey is added or used on this device and kept across sign-outs, like the language. Right
 * after the Sign out button the screen does not ask (the person just left); a fresh visit does.
 */

/** The sign-in challenge the anonymous session answer carries. */
export type PasskeySignIn = { challenge: string; rpId: string; timeout: number };

/** A device's answer to a sign-in challenge, as POST /api/auth/sign-in takes it (`passkey`). */
export type PasskeyAssertion = { credentialId: string; clientDataJson: string; authenticatorData: string; signature: string; userHandle: string };

/** POST /api/identity/me/passkeys/options, WebAuthn's creation options in JSON. */
export type PasskeyCreationOptions = {
  challenge: string;
  rp: { id: string; name: string };
  user: { id: string; name: string; displayName: string };
  pubKeyCredParams: { type: "public-key"; alg: number }[];
  excludeCredentials: { type: "public-key"; id: string; transports?: string[] | null }[];
  timeout: number;
  authenticatorSelection: { residentKey: string; requireResidentKey: boolean; userVerification: string };
  attestation: string;
};

/** A new passkey as POST /api/identity/me/passkeys takes it (without its name). */
export type PasskeyRegistration = { clientDataJson: string; attestationObject: string; transports: string[] };

export const passkeyOfferKey = "erp.passkeyOffer";
const signedOutKey = "erp.signedOut";

export function toBase64Url(buffer: ArrayBuffer | ArrayBufferView): string {
  const bytes = buffer instanceof ArrayBuffer ? new Uint8Array(buffer) : new Uint8Array(buffer.buffer, buffer.byteOffset, buffer.byteLength);
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

export function fromBase64Url(text: string): ArrayBuffer {
  const standard = text.replace(/-/g, "+").replace(/_/g, "/");
  const binary = atob(standard + "=".repeat((4 - (standard.length % 4)) % 4));
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes.buffer;
}

/** Whether this browser can use passkeys at all. */
export function passkeysSupported(): boolean {
  return typeof window !== "undefined" && typeof window.PublicKeyCredential === "function" && typeof navigator.credentials?.get === "function";
}

/** Whether the sign-in screen asks this device for a passkey as soon as it opens. */
export function passkeyOffered(): boolean {
  try {
    return localStorage.getItem(passkeyOfferKey) === "1";
  } catch {
    return false;
  }
}

export function setPasskeyOffered(on: boolean): void {
  try {
    localStorage.setItem(passkeyOfferKey, on ? "1" : "0");
  } catch {
    // Not kept on this device: the screen offers the passkey button instead.
  }
}

/** Marks the next sign-in screen of this tab as the one right after the Sign out button. */
export function markSignedOut(): void {
  try {
    sessionStorage.setItem(signedOutKey, "1");
  } catch {
    // Without it the next screen asks for the passkey; the person can cancel.
  }
}

/** Whether this screen comes right after the Sign out button (read once; the mark is removed). */
export function takeSignedOut(): boolean {
  try {
    const marked = sessionStorage.getItem(signedOutKey) === "1";
    sessionStorage.removeItem(signedOutKey);
    return marked;
  } catch {
    return false;
  }
}

/** Why asking the device ended without an answer: the person cancelled (or let it time out), or something else. */
export function cancelledByPerson(error: unknown): boolean {
  return error instanceof DOMException && (error.name === "NotAllowedError" || error.name === "AbortError");
}

/** Asks the device for a passkey of this site to answer the challenge; any passkey of the site may answer. */
export async function askForPasskey(request: PasskeySignIn, signal?: AbortSignal): Promise<PasskeyAssertion> {
  const credential = (await navigator.credentials.get({
    publicKey: {
      challenge: fromBase64Url(request.challenge),
      rpId: request.rpId,
      timeout: request.timeout,
      userVerification: "required",
      allowCredentials: [],
    },
    signal,
  })) as PublicKeyCredential | null;
  if (!credential) throw new DOMException("No passkey was chosen.", "NotAllowedError");
  const response = credential.response as AuthenticatorAssertionResponse;
  if (!response.userHandle) throw new DOMException("The passkey gave no user handle.", "NotAllowedError");
  return {
    credentialId: toBase64Url(credential.rawId),
    clientDataJson: toBase64Url(response.clientDataJSON),
    authenticatorData: toBase64Url(response.authenticatorData),
    signature: toBase64Url(response.signature),
    userHandle: toBase64Url(response.userHandle),
  };
}

/** Asks the device to make a passkey for the signed-in user from the server's creation options. */
export async function createPasskey(options: PasskeyCreationOptions): Promise<PasskeyRegistration> {
  const credential = (await navigator.credentials.create({
    publicKey: {
      challenge: fromBase64Url(options.challenge),
      rp: options.rp,
      user: { id: fromBase64Url(options.user.id), name: options.user.name, displayName: options.user.displayName },
      pubKeyCredParams: options.pubKeyCredParams,
      excludeCredentials: options.excludeCredentials.map((c) => ({
        type: c.type,
        id: fromBase64Url(c.id),
        ...(c.transports ? { transports: c.transports as AuthenticatorTransport[] } : {}),
      })),
      timeout: options.timeout,
      authenticatorSelection: {
        residentKey: options.authenticatorSelection.residentKey as ResidentKeyRequirement,
        requireResidentKey: options.authenticatorSelection.requireResidentKey,
        userVerification: options.authenticatorSelection.userVerification as UserVerificationRequirement,
      },
      attestation: options.attestation as AttestationConveyancePreference,
    },
  })) as PublicKeyCredential | null;
  if (!credential) throw new DOMException("No passkey was made.", "NotAllowedError");
  const response = credential.response as AuthenticatorAttestationResponse;
  return {
    clientDataJson: toBase64Url(response.clientDataJSON),
    attestationObject: toBase64Url(response.attestationObject),
    transports: typeof response.getTransports === "function" ? response.getTransports() : [],
  };
}

/** A name for a passkey made on this device: the browser and system, as far as they say. */
export function deviceName(userAgent: string = navigator.userAgent): string {
  const browser = /Edg\//.test(userAgent) ? "Edge" : /Firefox\//.test(userAgent) ? "Firefox" : /Chrome\//.test(userAgent) ? "Chrome" : /Safari\//.test(userAgent) ? "Safari" : "";
  const system = /Windows/.test(userAgent)
    ? "Windows"
    : /iPhone|iPad/.test(userAgent)
      ? "iOS"
      : /Mac OS X/.test(userAgent)
        ? "macOS"
        : /Android/.test(userAgent)
          ? "Android"
          : /Linux/.test(userAgent)
            ? "Linux"
            : "";
  return [browser, system].filter(Boolean).join(" · ");
}
