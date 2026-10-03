import { useEffect, useId, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { api, ApiError } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";
import { useSession } from "../../kernel/session";
import { RolePicker } from "./RolePicker";
import {
  completeEmail,
  domainOf,
  isEmail,
  nameFromEmail,
  roleName,
  type AccessView,
  type ResetResult,
  type Role,
  type SignInHistory,
  type User,
} from "./model";

type Notice = { kind: "code"; code: string; expiresAt?: string; email: string } | { kind: "info"; text: string };

type Errors = Record<string, string>;

function fieldErrors(error: unknown): Errors {
  if (!(error instanceof ApiError)) return {};
  const out: Errors = {};
  for (const [field, list] of Object.entries(error.fieldErrors)) out[field] = list[0]?.message ?? error.message;
  return out;
}

/** Ctrl+Enter or Ctrl+S saves, Escape closes: the same in every identity form. */
export function formKeys(event: KeyboardEvent, save: () => void, close: () => void) {
  if ((event.ctrlKey || event.metaKey) && (event.key === "Enter" || event.key.toLowerCase() === "s")) {
    event.preventDefault();
    save();
  } else if (event.key === "Escape") {
    event.preventDefault();
    close();
  }
}

/** The set-up code to hand over once, with the sign-in address and its expiry. */
function CodeNotice({ notice }: { notice: Extract<Notice, { kind: "code" }> }) {
  const { t, formatDateTime } = useI18n();
  const [copied, setCopied] = useState(false);
  const text = t("identity.code.handover", { email: notice.email, code: notice.code, address: window.location.origin });
  return (
    <div className="id-notice" role="status">
      <p>{t("identity.code.intro")}</p>
      <p className="id-code" dir="ltr" data-testid="setup-code">
        {notice.code}
      </p>
      {notice.expiresAt && <p className="muted">{t("identity.code.expires", { time: formatDateTime(notice.expiresAt) })}</p>}
      <button
        type="button"
        className="button"
        onClick={() => {
          void navigator.clipboard?.writeText(text).then(() => setCopied(true), () => setCopied(false));
        }}
      >
        {copied ? t("identity.code.copied") : t("identity.code.copy")}
      </button>
    </div>
  );
}

/** New user: e-mail (completed with the workspace's domain), name suggested from it, language,
 * roles, and an invitation code or a password. */
export function NewUserForm({ roles, onCreated, onClose }: { roles: Role[]; onCreated: (user: User) => void; onClose: () => void }) {
  const { t, language } = useI18n();
  const { state, can } = useSession();
  const domain = state.status === "signedIn" ? domainOf(state.session.user.email) : null;
  const [email, setEmail] = useState("");
  const [name, setName] = useState("");
  const [nameTouched, setNameTouched] = useState(false);
  const [userLanguage, setUserLanguage] = useState<"en" | "ar">(language);
  const [roleIds, setRoleIds] = useState<string[]>([]);
  const [method, setMethod] = useState<"invite" | "password">("invite");
  const [password, setPassword] = useState("");
  const [mustChange, setMustChange] = useState(true);
  const [errors, setErrors] = useState<Errors>({});
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const emailRef = useRef<HTMLInputElement>(null);
  const formRef = useRef<HTMLFormElement>(null);
  const id = useId();

  useEffect(() => emailRef.current?.focus(), []);

  function commitEmail() {
    const full = completeEmail(email, domain);
    if (full !== email) setEmail(full);
    if (!nameTouched && full) setName(nameFromEmail(full));
    return full;
  }

  async function save() {
    if (busy) return;
    const full = commitEmail();
    const displayName = name.trim() || nameFromEmail(full);
    const local: Errors = {};
    if (!full) local.email = t("identity.form.emailRequired");
    else if (!isEmail(full)) local.email = t("identity.form.emailInvalid");
    if (!displayName) local.displayName = t("identity.form.nameRequired");
    if (method === "password" && password.length < 10) local.password = t("identity.form.passwordShort");
    setErrors(local);
    if (Object.keys(local).length > 0) return;
    setBusy(true);
    setError(null);
    try {
      const user = await api<User>("POST", "/api/identity/users", {
        email: full,
        displayName,
        language: userLanguage,
        roleIds,
        ...(method === "password" ? { password, mustChangePassword: mustChange } : {}),
      });
      onCreated(user);
    } catch (e) {
      setErrors(fieldErrors(e));
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  const granted = new Set(state.status === "signedIn" ? state.session.permissions : []);
  const canGrant = (role: Role) => role.permissions.every((p) => granted.has(p));

  return (
    <form
      ref={formRef}
      className="id-form"
      noValidate
      aria-labelledby={`${id}-title`}
      onSubmit={(e: FormEvent) => {
        e.preventDefault();
        void save();
      }}
      onKeyDown={(e) => formKeys(e, () => void save(), onClose)}
    >
      <h2 id={`${id}-title`}>{t("identity.users.new")}</h2>
      <label className="field">
        <span className="field-label">{t("identity.users.email")}</span>
        <span className="id-email">
          <input
            ref={emailRef}
            name="email"
            type="text"
            inputMode="email"
            dir="ltr"
            autoComplete="off"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            onBlur={commitEmail}
            aria-invalid={errors.email ? true : undefined}
            aria-describedby={`${id}-email-hint`}
          />
          {domain && !email.includes("@") && (
            <span className="id-suffix" dir="ltr" aria-hidden="true">
              @{domain}
            </span>
          )}
        </span>
        <span id={`${id}-email-hint`} className={errors.email ? "field-error" : "id-hint"}>
          {errors.email ?? (domain ? t("identity.form.domainHint", { domain }) : "")}
        </span>
      </label>
      <label className="field">
        <span className="field-label">{t("identity.users.name")}</span>
        <input
          name="displayName"
          value={name}
          onChange={(e) => {
            setName(e.target.value);
            setNameTouched(true);
          }}
          aria-invalid={errors.displayName ? true : undefined}
        />
        {errors.displayName && <span className="field-error">{errors.displayName}</span>}
      </label>
      <label className="field">
        <span className="field-label">{t("identity.users.language")}</span>
        <select name="language" value={userLanguage} onChange={(e) => setUserLanguage(e.target.value as "en" | "ar")}>
          <option value="en">{t("identity.language.en")}</option>
          <option value="ar">{t("identity.language.ar")}</option>
        </select>
      </label>
      {can("identity.roles.read") ? (
        <RolePicker roles={roles} selected={roleIds} onChange={setRoleIds} canGrant={canGrant} />
      ) : (
        <p className="muted">{t("identity.form.rolesNeedPermission")}</p>
      )}
      <fieldset className="id-method">
        <legend className="field-label">{t("identity.form.signInMethod")}</legend>
        <label>
          <input type="radio" name="method" checked={method === "invite"} onChange={() => setMethod("invite")} />
          {t("identity.form.invite")}
        </label>
        <label>
          <input type="radio" name="method" checked={method === "password"} onChange={() => setMethod("password")} />
          {t("identity.form.setPassword")}
        </label>
        {method === "password" && (
          <>
            <label className="field">
              <span className="field-label">{t("identity.form.password")}</span>
              <input name="password" type="password" dir="ltr" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} aria-invalid={errors.password ? true : undefined} />
              {errors.password && <span className="field-error">{errors.password}</span>}
            </label>
            <label>
              <input type="checkbox" checked={mustChange} onChange={(e) => setMustChange(e.target.checked)} />
              {t("identity.form.mustChange")}
            </label>
          </>
        )}
      </fieldset>
      {error && (
        <div className="alert" role="alert">
          {error}
        </div>
      )}
      <div className="id-actions">
        <button type="submit" className="button primary" disabled={busy} aria-keyshortcuts="Control+Enter">
          {t("identity.form.create")}
        </button>
        <button type="button" className="button" onClick={onClose} aria-keyshortcuts="Escape">
          {t("identity.form.cancel")}
        </button>
        <span className="muted id-hint">{t("identity.form.keys")}</span>
      </div>
    </form>
  );
}

type Tab = "details" | "access" | "history";

/** One user: details (editable with the right permission), what they can do and why, and their
 * sign-in history, plus the account actions (reset password, sign out everywhere, unblock). */
export function UserDetail({
  userId,
  roles,
  notice: initialNotice,
  onSaved,
  onClose,
}: {
  userId: string;
  roles: Role[];
  notice?: Notice;
  onSaved: (user: User) => void;
  onClose: () => void;
}) {
  const { t, language, formatDateTime } = useI18n();
  const { state, can } = useSession();
  const [user, setUser] = useState<User | null>(null);
  const [tab, setTab] = useState<Tab>("details");
  const [notice, setNotice] = useState<Notice | undefined>(initialNotice);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [userLanguage, setUserLanguage] = useState<"en" | "ar">("en");
  const [active, setActive] = useState(true);
  const [roleIds, setRoleIds] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const id = useId();
  const self = state.status === "signedIn" && state.session.user.id === userId;
  const editable = can("identity.users.update");

  useEffect(() => {
    let live = true;
    setUser(null);
    setError(null);
    api<User>("GET", `/api/identity/users/${userId}`)
      .then((u) => {
        if (!live) return;
        setUser(u);
        setName(u.displayName);
        setUserLanguage(u.language);
        setActive(u.isActive);
        setRoleIds(u.roleIds);
      })
      .catch((e: Error) => live && setError(e.message));
    return () => {
      live = false;
    };
  }, [userId]);

  useEffect(() => setNotice(initialNotice), [initialNotice, userId]);

  useEffect(() => {
    if (user) headingRef.current?.focus();
  }, [user?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const granted = new Set(state.status === "signedIn" ? state.session.permissions : []);
  const canGrant = (role: Role) => role.permissions.every((p) => granted.has(p));

  async function save() {
    if (!user || !editable || busy) return;
    setBusy(true);
    setError(null);
    try {
      const saved = await api<User>("PUT", `/api/identity/users/${user.id}`, {
        displayName: name.trim(),
        language: userLanguage,
        isActive: active,
        roleIds,
        version: user.version,
      });
      setUser(saved);
      setNotice({ kind: "info", text: t("identity.form.saved") });
      onSaved(saved);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  if (!user) {
    return (
      <section className="id-form" aria-busy={!error}>
        {error ? (
          <div className="alert" role="alert">
            {error}
          </div>
        ) : (
          <p className="muted">{t("identity.loading")}</p>
        )}
      </section>
    );
  }

  return (
    <section className="id-form" aria-labelledby={`${id}-title`} onKeyDown={(e) => formKeys(e, () => void save(), onClose)}>
      <h2 id={`${id}-title`} ref={headingRef} tabIndex={-1}>
        {user.displayName}
      </h2>
      <p className="muted" dir="ltr">
        {user.email}
      </p>
      <p className="id-badges">
        <span className={user.isActive ? "id-badge ok" : "id-badge off"}>{user.isActive ? t("identity.users.active") : t("identity.users.inactive")}</span>
        {user.pendingSetup && <span className="id-badge warn">{t("identity.users.pendingSetup")}</span>}
        <span className="muted">
          {t("identity.users.lastSignIn")}: {user.lastSignInAt ? formatDateTime(user.lastSignInAt) : t("identity.users.never")}
        </span>
      </p>
      {notice?.kind === "code" && <CodeNotice notice={notice} />}
      {notice?.kind === "info" && (
        <div className="id-notice" role="status">
          {notice.text}
        </div>
      )}
      <div role="tablist" className="id-tabs" aria-label={t("identity.users.sections")}>
        {(["details", "access", "history"] as Tab[])
          .filter((x) => x !== "history" || can("identity.signIns.read"))
          .map((x) => (
            <button key={x} type="button" role="tab" aria-selected={tab === x} className={tab === x ? "id-tab active" : "id-tab"} onClick={() => setTab(x)}>
              {t(`identity.tab.${x}`)}
            </button>
          ))}
      </div>
      {tab === "details" && (
        <form
          noValidate
          onSubmit={(e) => {
            e.preventDefault();
            void save();
          }}
        >
          <fieldset disabled={!editable} className="id-plain">
            <label className="field">
              <span className="field-label">{t("identity.users.name")}</span>
              <input name="displayName" value={name} onChange={(e) => setName(e.target.value)} />
            </label>
            <label className="field">
              <span className="field-label">{t("identity.users.language")}</span>
              <select value={userLanguage} onChange={(e) => setUserLanguage(e.target.value as "en" | "ar")}>
                <option value="en">{t("identity.language.en")}</option>
                <option value="ar">{t("identity.language.ar")}</option>
              </select>
            </label>
            <label className="id-check">
              <input type="checkbox" checked={active} disabled={self} onChange={(e) => setActive(e.target.checked)} />
              {t("identity.form.active")}
            </label>
          </fieldset>
          {can("identity.roles.read") ? (
            <RolePicker roles={roles} selected={roleIds} onChange={setRoleIds} canGrant={canGrant} disabled={!editable || self} />
          ) : (
            <p className="muted">{t("identity.form.rolesNeedPermission")}</p>
          )}
          {self && <p className="muted">{t("identity.form.selfNote")}</p>}
          {error && (
            <div className="alert" role="alert">
              {error}
            </div>
          )}
          <div className="id-actions">
            {editable && (
              <button type="submit" className="button primary" disabled={busy} aria-keyshortcuts="Control+Enter">
                {t("identity.form.save")}
              </button>
            )}
            <button type="button" className="button" onClick={onClose} aria-keyshortcuts="Escape">
              {t("identity.form.close")}
            </button>
          </div>
          {!self && <AccountActions user={user} onNotice={setNotice} />}
        </form>
      )}
      {tab === "access" && <AccessTab userId={user.id} roles={roles} language={language} />}
      {tab === "history" && <HistoryTab userId={user.id} self={self} />}
    </section>
  );
}

/** Reset the password (new set-up code or a temporary password) and sign out everywhere. */
function AccountActions({ user, onNotice }: { user: User; onNotice: (notice: Notice) => void }) {
  const { t } = useI18n();
  const { can } = useSession();
  const [mode, setMode] = useState<"closed" | "code" | "password">("closed");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  if (!can("identity.users.resetPassword") && !can("identity.users.update")) return null;

  async function reset() {
    setBusy(true);
    setError(null);
    try {
      const result = await api<ResetResult>("POST", `/api/identity/users/${user.id}/password`, mode === "password" ? { password, mustChangePassword: true } : {});
      onNotice(
        result.setupCode
          ? { kind: "code", code: result.setupCode, expiresAt: result.setupCodeExpiresAt, email: user.email }
          : { kind: "info", text: t("identity.reset.done", { count: result.sessionsEnded }) },
      );
      setMode("closed");
      setPassword("");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  async function signOutEverywhere() {
    setBusy(true);
    setError(null);
    try {
      const result = await api<{ sessionsEnded: number }>("POST", `/api/identity/users/${user.id}/sessions/revoke`);
      onNotice({ kind: "info", text: t("identity.sessions.ended", { count: result.sessionsEnded }) });
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="id-account">
      <h3>{t("identity.account.title")}</h3>
      <div className="id-actions">
        {can("identity.users.resetPassword") && (
          <button type="button" className="button" onClick={() => setMode(mode === "closed" ? "code" : "closed")} aria-expanded={mode !== "closed"}>
            {t("identity.reset.open")}
          </button>
        )}
        {can("identity.users.update") && (
          <button type="button" className="button" disabled={busy} onClick={() => void signOutEverywhere()}>
            {t("identity.sessions.revoke")}
          </button>
        )}
      </div>
      {mode !== "closed" && (
        <fieldset className="id-method">
          <legend className="field-label">{t("identity.reset.how")}</legend>
          <label>
            <input type="radio" name="reset" checked={mode === "code"} onChange={() => setMode("code")} />
            {t("identity.reset.code")}
          </label>
          <label>
            <input type="radio" name="reset" checked={mode === "password"} onChange={() => setMode("password")} />
            {t("identity.reset.temporary")}
          </label>
          {mode === "password" && (
            <label className="field">
              <span className="field-label">{t("identity.form.password")}</span>
              <input type="password" dir="ltr" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} />
            </label>
          )}
          <button type="button" className="button primary" disabled={busy || (mode === "password" && password.length < 10)} onClick={() => void reset()}>
            {t("identity.reset.confirm")}
          </button>
        </fieldset>
      )}
      {error && (
        <div className="alert" role="alert">
          {error}
        </div>
      )}
    </div>
  );
}

/** What the user can do, grouped by area, and which role grants each permission. */
function AccessTab({ userId, roles, language }: { userId: string; roles: Role[]; language: string }) {
  const { t } = useI18n();
  const [view, setView] = useState<AccessView | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    api<AccessView>("GET", `/api/identity/users/${userId}/access`).then(setView, (e: Error) => setError(e.message));
  }, [userId]);
  if (error)
    return (
      <div className="alert" role="alert">
        {error}
      </div>
    );
  if (!view) return <p className="muted">{t("identity.loading")}</p>;
  const names = new Map([...roles, ...view.roles].map((r) => [r.id, roleName(r, language)]));
  const modules = [...new Set(view.permissions.map((p) => p.moduleLabel))];
  return (
    <div className="id-access">
      <p>{t("identity.access.summary", { count: view.permissions.length, roles: view.roles.length })}</p>
      {view.permissions.length === 0 && <p className="muted">{t("identity.access.nothing")}</p>}
      {modules.map((module) => (
        <table key={module} className="grid">
          <caption>{module}</caption>
          <thead>
            <tr>
              <th scope="col">{t("identity.access.permission")}</th>
              <th scope="col">{t("identity.access.grantedBy")}</th>
            </tr>
          </thead>
          <tbody>
            {view.permissions
              .filter((p) => p.moduleLabel === module)
              .map((p) => (
                <tr key={p.key}>
                  <td title={p.key}>{p.label}</td>
                  <td>{p.grantedBy.map((r) => names.get(r) ?? r).join(language === "ar" ? "، " : ", ")}</td>
                </tr>
              ))}
          </tbody>
        </table>
      ))}
    </div>
  );
}

/** Sign-in history, newest first, and the clients paused now (with Unblock). */
function HistoryTab({ userId, self }: { userId: string; self: boolean }) {
  const { t, formatDateTime } = useI18n();
  const { can } = useSession();
  const [history, setHistory] = useState<SignInHistory | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  useEffect(() => {
    api<SignInHistory>("GET", `/api/identity/users/${userId}/sign-ins?take=100`).then(setHistory, (e: Error) => setError(e.message));
  }, [userId, reload]);

  async function unblock() {
    try {
      await api<void>("POST", `/api/identity/users/${userId}/unblock`);
      setReload((n) => n + 1);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  if (error)
    return (
      <div className="alert" role="alert">
        {error}
      </div>
    );
  if (!history) return <p className="muted">{t("identity.loading")}</p>;
  return (
    <div className="id-history">
      {history.paused.length > 0 && (
        <div className="id-notice warn" role="status">
          <p>{t("identity.history.paused", { count: history.paused.length })}</p>
          <ul>
            {history.paused.map((p) => (
              <li key={p.source} dir="ltr">
                {p.source} · {formatDateTime(p.until)}
              </li>
            ))}
          </ul>
          {!self && can("identity.users.update") && (
            <button type="button" className="button" onClick={() => void unblock()}>
              {t("identity.history.unblock")}
            </button>
          )}
        </div>
      )}
      <p className="muted">{t("identity.history.count", { count: history.total })}</p>
      <table className="grid">
        <thead>
          <tr>
            <th scope="col">{t("identity.history.when")}</th>
            <th scope="col">{t("identity.history.outcome")}</th>
            <th scope="col">{t("identity.history.address")}</th>
            <th scope="col">{t("identity.history.session")}</th>
          </tr>
        </thead>
        <tbody>
          {history.items.map((a) => (
            <tr key={a.id}>
              <td>{formatDateTime(a.occurredAt)}</td>
              <td>
                <span className={a.outcome === "succeeded" ? "id-badge ok" : "id-badge off"}>{t(`identity.outcome.${a.outcome}`)}</span>
              </td>
              <td dir="ltr" title={a.userAgent ?? undefined}>
                {a.ipAddress ?? "—"}
              </td>
              <td>{a.sessionActive ? t("identity.history.active") : ""}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export type { Notice };
