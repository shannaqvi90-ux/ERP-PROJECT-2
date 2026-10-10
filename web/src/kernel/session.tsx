import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { api, ApiError, sessionEndedEvent, type FieldError } from "./api";
import { forgetIdentity, startOver } from "./deviceState";
import { markSignedOut, type PasskeyAssertion, type PasskeySignIn } from "./passkeys";

export type SessionUser = { id: string; email: string; displayName: string; language: "en" | "ar"; numerals?: "latn" | "arab"; displayNameAr?: string | null };

/** The signed-in user's name in the screen's language: the Arabic name on Arabic screens when they have one. */
export const sessionUserName = (user: Pick<SessionUser, "displayName" | "displayNameAr">, language: string) =>
  language === "ar" && user.displayNameAr ? user.displayNameAr : user.displayName;
export type SessionTenant = { id: string; code: string; nameEn: string; nameAr: string };
export type MenuItem = { key: string; labelKey: string; path: string; group: string | null };

export type Session = {
  authenticated: true;
  user: SessionUser;
  tenant: SessionTenant;
  permissions: string[];
  /** What the user's roles grant in every company (roles held in the working company left out):
   * what changing a role, or giving one in every company, needs. */
  workspacePermissions?: string[];
  menu: MenuItem[];
  expiresAt: string | null;
};

type SessionResponse = Session | { authenticated: false; passkey?: PasskeySignIn };

export type Workspace = { code: string; nameEn: string; nameAr: string };

export type SignInResult =
  | { kind: "ok"; session: Session }
  | { kind: "failed"; message: string; code?: string }
  | { kind: "invalid"; message: string; fieldErrors: Record<string, FieldError[]> }
  | { kind: "chooseWorkspace"; message: string; workspaces: Workspace[] }
  | { kind: "changePassword"; message: string };

type SessionState =
  | { status: "loading" }
  /** Nobody is signed in; `passkey` is a fresh challenge for signing in with a passkey. */
  | { status: "anonymous"; passkey?: PasskeySignIn }
  | { status: "signedIn"; session: Session }
  /** The identity ended: this browser forgets it and the document is replaced (kernel/deviceState). */
  | { status: "leaving" };

/** Who the document holds: the same user in the same tenant. Anything else is a new identity. */
export const identityOf = (session: Session) => `${session.tenant.id}/${session.user.id}`;

/**
 * The identity signed in on this browser, shared by its tabs through localStorage (forgotten with
 * everything else when an identity ends). Another tab signing in, out or as someone else changes
 * it, and the storage event makes every other tab check its session and start over if it changed.
 */
export const sessionMarkKey = "erp.session";

/**
 * Dispatched on window when the session's scope changes without a new sign-in: the working
 * company (the tenancy module's switcher sends it). Roles can be held in one company, so the
 * session's permissions and menu are read again.
 */
export const sessionScopeChangedEvent = "erp:workplace-changed";

type SessionApi = {
  state: SessionState;
  /** With `newPassword`, the password is changed as part of signing in (one-time set-up codes). */
  signIn: (email: string, password: string, workspace?: string, newPassword?: string) => Promise<SignInResult>;
  /** Signs in with a device's answer to a passkey challenge. */
  signInWithPasskey: (assertion: PasskeyAssertion) => Promise<SignInResult>;
  signOut: () => Promise<void>;
  refresh: () => Promise<void>;
  /** True when the signed-in user's roles grant the permission. The API enforces it anyway. */
  can: (permission: string) => boolean;
};

const SessionContext = createContext<SessionApi | null>(null);

export async function requestSignIn(email: string, password: string, workspace?: string, newPassword?: string): Promise<SignInResult> {
  return send(newPassword ? { email, password, workspace, newPassword } : { email, password, workspace });
}

export async function requestPasskeySignIn(passkey: PasskeyAssertion): Promise<SignInResult> {
  return send({ passkey });
}

/** A fresh passkey sign-in challenge (from the anonymous session answer), or null when signed in or unreachable. */
export async function freshPasskeyChallenge(): Promise<PasskeySignIn | null> {
  try {
    const response = await api<SessionResponse>("GET", "/api/auth/session");
    return !response.authenticated && response.passkey ? response.passkey : null;
  } catch {
    return null;
  }
}

async function send(body: Record<string, unknown>): Promise<SignInResult> {
  try {
    const session = await api<Session>("POST", "/api/auth/sign-in", body);
    return { kind: "ok", session };
  } catch (error) {
    if (!(error instanceof ApiError)) throw error;
    if (error.status === 409 && error.code === "auth.passwordChangeRequired") {
      return { kind: "changePassword", message: error.message };
    }
    if (error.status === 409 && error.code === "auth.chooseWorkspace") {
      return { kind: "chooseWorkspace", message: error.message, workspaces: (error.body.workspaces as Workspace[]) ?? [] };
    }
    if (error.status === 400) {
      return { kind: "invalid", message: error.message, fieldErrors: error.fieldErrors };
    }
    return { kind: "failed", message: error.message, code: error.code };
  }
}

/**
 * The session of this document. A document holds one identity (user and tenant) for its whole
 * life: signing out, a session that ends (the server answers 401, or the session check finds none)
 * and a change to another identity (another user or tenant, for example after switching
 * workspace) all make this browser forget what it stored for the identity and replace the
 * document with a fresh one, so nothing in memory reaches the next person (kernel/deviceState).
 */
export function SessionProvider({ children, onSignedIn }: { children: ReactNode; onSignedIn?: (session: Session) => void }) {
  const [state, setState] = useState<SessionState>({ status: "loading" });
  // The identity this document has held, once it has held one; and whether it is ending.
  const held = useRef<string | null>(null);
  const ending = useRef(false);
  // The in-app paths of the identity's screens: cookies scoped to any of them are forgotten too.
  const visited = useRef<string[]>([]);

  const refresh = useCallback(async () => {
    if (ending.current) return;
    try {
      const response = await api<SessionResponse>("GET", "/api/auth/session");
      if (ending.current) return;
      setState(response.authenticated ? { status: "signedIn", session: response } : { status: "anonymous", passkey: response.passkey });
    } catch {
      if (!ending.current) setState({ status: "anonymous" });
    }
  }, []);

  /** End the identity: forget it on this device and start over in a fresh document. */
  const leave = useCallback(async (keepEmail: boolean) => {
    if (ending.current) return;
    ending.current = true;
    setState({ status: "leaving" });
    await forgetIdentity({ keepEmail, appPaths: visited.current });
    startOver("/");
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // Another tab of this browser signed in, out or as someone else: check the session.
  useEffect(() => {
    const onStorage = (event: StorageEvent) => {
      if (held.current !== null && (event.key === null || event.key === sessionMarkKey)) void refresh();
    };
    window.addEventListener("storage", onStorage);
    return () => window.removeEventListener("storage", onStorage);
  }, [refresh]);

  // Any request answered 401 while signed in: check the session; if it ended, start over.
  useEffect(() => {
    const check = () => {
      if (held.current !== null) void refresh();
    };
    window.addEventListener(sessionEndedEvent, check);
    return () => window.removeEventListener(sessionEndedEvent, check);
  }, [refresh]);

  // The working company changed: what the user may do there can differ (roles held in one company).
  useEffect(() => {
    const rescope = () => {
      if (held.current !== null) void refresh();
    };
    window.addEventListener(sessionScopeChangedEvent, rescope);
    return () => window.removeEventListener(sessionScopeChangedEvent, rescope);
  }, [refresh]);

  useEffect(() => {
    if (ending.current) return;
    if (state.status === "signedIn") {
      const identity = identityOf(state.session);
      if (held.current !== null && held.current !== identity) {
        // Another identity in a document that held one: never show it here.
        void leave(true);
        return;
      }
      held.current = identity;
      visited.current = state.session.menu.map((item) => item.path);
      try {
        if (localStorage.getItem(sessionMarkKey) !== identity) localStorage.setItem(sessionMarkKey, identity);
      } catch {
        // Storage unavailable: other tabs learn of a change at their next request instead.
      }
      onSignedIn?.(state.session);
    } else if (state.status === "anonymous" && held.current !== null) {
      void leave(true);
    }
  }, [state, onSignedIn, leave]);

  const signIn = useCallback(async (email: string, password: string, workspace?: string, newPassword?: string) => {
    const result = await requestSignIn(email, password, workspace, newPassword);
    if (result.kind === "ok" && !ending.current) setState({ status: "signedIn", session: result.session });
    return result;
  }, []);

  const signInWithPasskey = useCallback(async (assertion: PasskeyAssertion) => {
    const result = await requestPasskeySignIn(assertion);
    if (result.kind === "ok" && !ending.current) setState({ status: "signedIn", session: result.session });
    return result;
  }, []);

  const signOut = useCallback(async () => {
    if (ending.current) return;
    ending.current = true;
    setState({ status: "leaving" });
    try {
      // keepalive: the sign-out reaches the server even when the tab is closed or reloaded right
      // after the click (a shared device must not stay signed in).
      await api<void>("POST", "/api/auth/sign-out", undefined, { keepalive: true });
    } catch {
      // The document is replaced anyway; the fresh one asks the server who is signed in.
    } finally {
      // Signing out forgets the remembered e-mail too: the next person sees an empty sign-in.
      await forgetIdentity({ keepEmail: false, appPaths: visited.current });
      // The person just left: the next screen does not ask this device for a passkey at once.
      markSignedOut();
      startOver("/");
    }
  }, []);

  const value = useMemo<SessionApi>(() => {
    const granted = new Set(state.status === "signedIn" ? state.session.permissions : []);
    return { state, signIn, signInWithPasskey, signOut, refresh, can: (permission) => granted.has(permission) };
  }, [state, signIn, signInWithPasskey, signOut, refresh]);

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): SessionApi {
  const context = useContext(SessionContext);
  if (!context) throw new Error("useSession must be used inside SessionProvider");
  return context;
}

/** Renders children only when the user holds the permission. */
export function Can({ permission, children }: { permission: string; children: ReactNode }) {
  return useSession().can(permission) ? <>{children}</> : null;
}
