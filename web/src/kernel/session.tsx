import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { api, ApiError, sessionEndedEvent, type FieldError } from "./api";
import { forgetIdentity, startOver } from "./deviceState";

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
  menu: MenuItem[];
  expiresAt: string | null;
};

type SessionResponse = Session | { authenticated: false };

export type Workspace = { code: string; nameEn: string; nameAr: string };

export type SignInResult =
  | { kind: "ok"; session: Session }
  | { kind: "failed"; message: string }
  | { kind: "invalid"; message: string; fieldErrors: Record<string, FieldError[]> }
  | { kind: "chooseWorkspace"; message: string; workspaces: Workspace[] }
  | { kind: "changePassword"; message: string };

type SessionState =
  | { status: "loading" }
  | { status: "anonymous" }
  | { status: "signedIn"; session: Session }
  /** The identity ended: this browser forgets it and the document is replaced (kernel/deviceState). */
  | { status: "leaving" };

/** Who the document holds: the same user in the same tenant. Anything else is a new identity. */
export const identityOf = (session: Session) => `${session.tenant.id}/${session.user.id}`;

type SessionApi = {
  state: SessionState;
  /** With `newPassword`, the password is changed as part of signing in (one-time set-up codes). */
  signIn: (email: string, password: string, workspace?: string, newPassword?: string) => Promise<SignInResult>;
  signOut: () => Promise<void>;
  refresh: () => Promise<void>;
  /** True when the signed-in user's roles grant the permission. The API enforces it anyway. */
  can: (permission: string) => boolean;
};

const SessionContext = createContext<SessionApi | null>(null);

export async function requestSignIn(email: string, password: string, workspace?: string, newPassword?: string): Promise<SignInResult> {
  try {
    const session = await api<Session>("POST", "/api/auth/sign-in", newPassword ? { email, password, workspace, newPassword } : { email, password, workspace });
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
    return { kind: "failed", message: error.message };
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

  const refresh = useCallback(async () => {
    if (ending.current) return;
    try {
      const response = await api<SessionResponse>("GET", "/api/auth/session");
      if (ending.current) return;
      setState(response.authenticated ? { status: "signedIn", session: response } : { status: "anonymous" });
    } catch {
      if (!ending.current) setState({ status: "anonymous" });
    }
  }, []);

  /** End the identity: forget it on this device and start over in a fresh document. */
  const leave = useCallback(async (keepEmail: boolean) => {
    if (ending.current) return;
    ending.current = true;
    setState({ status: "leaving" });
    await forgetIdentity({ keepEmail });
    startOver("/");
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // Any request answered 401 while signed in: check the session; if it ended, start over.
  useEffect(() => {
    const check = () => {
      if (held.current !== null) void refresh();
    };
    window.addEventListener(sessionEndedEvent, check);
    return () => window.removeEventListener(sessionEndedEvent, check);
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
      await forgetIdentity({ keepEmail: false });
      startOver("/");
    }
  }, []);

  const value = useMemo<SessionApi>(() => {
    const granted = new Set(state.status === "signedIn" ? state.session.permissions : []);
    return { state, signIn, signOut, refresh, can: (permission) => granted.has(permission) };
  }, [state, signIn, signOut, refresh]);

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
