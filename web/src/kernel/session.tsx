import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { api, ApiError, type FieldError } from "./api";

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
  | { status: "signedIn"; session: Session };

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

export function SessionProvider({ children, onSignedIn }: { children: ReactNode; onSignedIn?: (session: Session) => void }) {
  const [state, setState] = useState<SessionState>({ status: "loading" });

  const refresh = useCallback(async () => {
    try {
      const response = await api<SessionResponse>("GET", "/api/auth/session");
      setState(response.authenticated ? { status: "signedIn", session: response } : { status: "anonymous" });
    } catch {
      setState({ status: "anonymous" });
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    if (state.status === "signedIn") onSignedIn?.(state.session);
  }, [state, onSignedIn]);

  const signIn = useCallback(async (email: string, password: string, workspace?: string, newPassword?: string) => {
    const result = await requestSignIn(email, password, workspace, newPassword);
    if (result.kind === "ok") setState({ status: "signedIn", session: result.session });
    return result;
  }, []);

  const signOut = useCallback(async () => {
    try {
      await api<void>("POST", "/api/auth/sign-out");
    } finally {
      setState({ status: "anonymous" });
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
