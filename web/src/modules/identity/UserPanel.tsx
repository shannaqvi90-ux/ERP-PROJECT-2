import { useEffect, useId, useRef, useState } from "react";
import { api, ApiError } from "../../kernel/api";
import { BooleanField, SelectField, TextField } from "../../kernel/forms/fields";
import { FormSection, FormTabs, formKeys, RecordForm, type RecordNavigation } from "../../kernel/forms/RecordForm";
import { useRecordForm, type FieldBinding, type FormErrors } from "../../kernel/forms/useRecordForm";
import { teamSignInAddress } from "../../kernel/signInAddress";
import { useI18n } from "../../kernel/i18n";
import { useSession } from "../../kernel/session";
import { CompanyRolesEditor, DefaultCompanyField } from "./CompanyRoles";
import { RolePicker } from "./RolePicker";
import {
  companyName,
  sameCompanyRoles,
  type Company,
  type CompanyRole,
  type DefaultCompany,
  completeEmail,
  domainOf,
  isEmail,
  nameFromEmail,
  roleName,
  userActions,
  userName,
  type AccessView,
  type ResetResult,
  type Role,
  type SignInHistory,
  type User,
} from "./model";

/** A one-time notice in a user's panel; <c>warning</c>: what could not be saved after the account was created. */
type Notice = ({ kind: "code"; code: string; expiresAt?: string; email: string } | { kind: "info"; text: string }) & { warning?: string };

/** Ctrl+Enter or Ctrl+S saves, Escape closes: the kernel's form keys (kernel/forms), kept here
 * under their old name for the identity forms that are not record forms. */
export { formKeys };

/** The set-up code to hand over once, with the sign-in address and its expiry. */
function CodeNotice({ notice }: { notice: Extract<Notice, { kind: "code" }> }) {
  const { t, formatDateTime } = useI18n();
  const [copied, setCopied] = useState(false);
  const text = t("identity.code.handover", { email: notice.email, code: notice.code, address: teamSignInAddress(window.location.origin, notice.email) });
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

type NewUserDraft = {
  email: string;
  displayName: string;
  language: "en" | "ar";
  roleIds: string[];
  /** Roles that apply in one company only. */
  companyRoles: CompanyRole[];
  /** The companies the new user works in (every branch), when the signed-in user may give access. */
  worksIn: string[];
  /** The companies were chosen by hand: until then the company the signed-in user works in now is
   * ticked too (critic p03 round 7: a user created from the keyboard landed on "No company"). */
  worksInChosen: boolean;
  /** Where they start work (null: their first company by code). */
  startsIn: string | null;
  method: "invite" | "password";
  password: string;
  mustChangePassword: boolean;
};

/** A user just created, and what could not be saved after the account itself (company access or
 * the starting company), to be said in their panel. */
export type CreatedUser = User & { followUpError?: string };

/** New user (the shared record form): e-mail (completed with the workspace's domain), name
 * suggested from it, language, roles in every company and in one company, the companies they work
 * in and the one they start in (critic p03 round 5: these were only in the panel after creation,
 * and the starting company needed company access given first on another screen), and an
 * invitation code or a password. The account and its roles are created in one request; company
 * access and the starting company follow it, and a refusal of either is shown in the new user's
 * panel (the account exists by then). */
export function NewUserForm({ roles, onCreated, onClose }: { roles: Role[]; onCreated: (user: CreatedUser) => void; onClose: () => void }) {
  const { t, language } = useI18n();
  const { state, can } = useSession();
  const domain = state.status === "signedIn" ? domainOf(state.session.user.email) : null;
  const [nameTouched, setNameTouched] = useState(false);
  const [companies, setCompanies] = useState<Company[]>([]);
  const companiesRef = useRef<Company[]>([]);
  companiesRef.current = companies;
  const id = useId();
  const givesAccess = can("tenancy.access.read") && can("tenancy.access.update");
  // The company the signed-in user works in now: a new user works there too unless the form says otherwise.
  const [working, setWorking] = useState<string | null>(null);
  const workingRef = useRef<string | null>(null);
  workingRef.current = working;
  useEffect(() => {
    let live = true;
    api<Company[]>("GET", "/api/identity/companies").then(
      (list) => live && setCompanies(Array.isArray(list) ? list : []),
      () => live && setCompanies([]),
    );
    if (givesAccess) {
      api<{ companyId?: string | null }>("GET", "/api/tenancy/workplace").then(
        (w) => live && setWorking(typeof w?.companyId === "string" ? w.companyId : null),
        () => undefined,
      );
    }
    return () => {
      live = false;
    };
  }, [givesAccess]);
  /** The companies ticked: those chosen, or, until any is chosen by hand, the working one and those a company role needs. */
  const worksInOf = (d: NewUserDraft) => (d.worksInChosen || !workingRef.current ? d.worksIn : [...new Set([workingRef.current, ...d.worksIn])]);
  const form = useRecordForm<CreatedUser, NewUserDraft>({
    initial: () => ({ email: "", displayName: "", language, roleIds: [], companyRoles: [], worksIn: [], worksInChosen: false, startsIn: null, method: "invite", password: "", mustChangePassword: true }),
    canEdit: can("identity.users.create"),
    validate: (draft) => {
      const full = completeEmail(draft.email, domain);
      const local: FormErrors = {};
      if (!full) local.email = [t("identity.form.emailRequired")];
      else if (!isEmail(full)) local.email = [t("identity.form.emailInvalid")];
      if (!(draft.displayName.trim() || nameFromEmail(full))) local.displayName = [t("identity.form.nameRequired")];
      if (draft.method === "password" && draft.password.length < 10) local.password = [t("identity.form.passwordShort")];
      return local;
    },
    save: async (draft) => {
      const full = completeEmail(draft.email, domain);
      const user = await api<User>("POST", "/api/identity/users", {
        email: full,
        displayName: draft.displayName.trim() || nameFromEmail(full),
        language: draft.language,
        roleIds: draft.roleIds,
        ...(draft.companyRoles.length > 0 ? { companyRoles: draft.companyRoles } : {}),
        ...(draft.method === "password" ? { password: draft.password, mustChangePassword: draft.mustChangePassword } : {}),
      });
      const worksIn = worksInOf(draft).filter((c) => companiesRef.current.some((x) => x.id === c));
      if (!givesAccess || worksIn.length === 0) return user;
      try {
        const access = await api<{ version: number }>("GET", `/api/tenancy/access/${user.id}`);
        await api("PUT", `/api/tenancy/access/${user.id}`, {
          companies: worksIn.map((companyId) => ({ companyId, allBranches: true, branchIds: [] })),
          version: access.version,
        });
        if (draft.startsIn && worksIn.includes(draft.startsIn)) {
          const current = await api<DefaultCompany>("GET", `/api/identity/users/${user.id}/default-company`);
          await api("PUT", `/api/identity/users/${user.id}/default-company`, { companyId: draft.startsIn, version: current.version });
        }
        return user;
      } catch (error) {
        return { ...user, followUpError: error instanceof ApiError ? error.message : String(error) };
      }
    },
    onSaved: (user) => onCreated(user),
  });
  const { draft, set, errors } = form;

  function commitEmail() {
    const full = completeEmail(draft.email, domain);
    form.update((d) => ({ ...d, email: full, displayName: !nameTouched && full ? nameFromEmail(full) : d.displayName }));
  }

  const granted = new Set(state.status === "signedIn" ? state.session.permissions : []);
  const canGrant = (role: Role) => role.permissions.every((p) => granted.has(p));
  // A role in every company needs its grants in every company (roles in the working company's do not count).
  const everywhere = new Set(state.status === "signedIn" ? (state.session.workspacePermissions ?? state.session.permissions) : []);
  const canGrantEverywhere = (role: Role) => role.permissions.every((p) => everywhere.has(p));

  return (
    <RecordForm form={form} title={t("identity.users.new")} onClose={onClose} saveLabel={t("identity.form.create")}>
      <div className="id-form">
        <FormSection columns={false}>
          <div className="field" data-field="email">
            <label className="field-label" htmlFor={`${id}-email`}>
              {t("identity.users.email")}
            </label>
            <span className="id-email">
              <input
                id={`${id}-email`}
                name="email"
                type="text"
                inputMode="email"
                dir="ltr"
                autoComplete="off"
                autoFocus
                value={draft.email}
                onChange={(e) => set("email")(e.target.value)}
                onBlur={commitEmail}
                aria-invalid={errors.email ? true : undefined}
                aria-describedby={`${id}-email-hint`}
              />
              {domain && !draft.email.includes("@") && (
                <span className="id-suffix" dir="ltr" aria-hidden="true">
                  @{domain}
                </span>
              )}
            </span>
            <span id={`${id}-email-hint`} className={errors.email ? "field-error" : "id-hint"}>
              {errors.email?.join(" ") ?? (domain ? t("identity.form.domainHint", { domain }) : "")}
            </span>
          </div>
          <TextField
            field={{ ...form.bind("displayName"), onChange: (v) => { setNameTouched(true); set("displayName")(v); } }}
            label={t("identity.users.name")}
          />
          <SelectField
            field={form.bind("language") as FieldBinding<"en" | "ar" | "">}
            label={t("identity.users.language")}
            options={[
              { value: "en" as const, label: t("identity.language.en") },
              { value: "ar" as const, label: t("identity.language.ar") },
            ]}
          />
          {can("identity.roles.read") ? (
            <RolePicker roles={roles} selected={draft.roleIds} onChange={set("roleIds")} canGrant={canGrantEverywhere} />
          ) : (
            <p className="muted">{t("identity.form.rolesNeedPermission")}</p>
          )}
          {can("identity.roles.read") && companies.length > 0 && (
            <CompanyRolesEditor
              companies={companies}
              roles={roles}
              value={draft.companyRoles}
              onChange={(next) =>
                // A role in a company is only of use while working there: that company is ticked too.
                form.update((d) => ({
                  ...d,
                  companyRoles: next,
                  worksIn: givesAccess ? [...new Set([...d.worksIn, ...next.map((c) => c.companyId)])] : d.worksIn,
                }))
              }
              canGrant={canGrant}
            />
          )}
          {givesAccess && companies.length > 0 && (
            <>
              <fieldset className="id-works-in">
                <legend className="field-label">{t("identity.form.worksIn")}</legend>
                {companies.map((c) => (
                  <label key={c.id}>
                    <input
                      type="checkbox"
                      name="worksIn"
                      checked={worksInOf(draft).includes(c.id)}
                      onChange={(e) =>
                        form.update((d) => {
                          const shown = worksInOf(d);
                          const worksIn = e.target.checked ? [...new Set([...shown, c.id])] : shown.filter((x) => x !== c.id);
                          return { ...d, worksIn, worksInChosen: true, startsIn: d.startsIn && worksIn.includes(d.startsIn) ? d.startsIn : null };
                        })
                      }
                    />
                    {companyName(c, language)}
                  </label>
                ))}
                <span className="id-hint">{t("identity.form.worksInHint")}</span>
              </fieldset>
              {worksInOf(draft).length > 0 && (
                <DefaultCompanyField companies={companies.filter((c) => worksInOf(draft).includes(c.id))} value={draft.startsIn} onChange={set("startsIn")} />
              )}
            </>
          )}
          <fieldset className="id-method">
            <legend className="field-label">{t("identity.form.signInMethod")}</legend>
            <label>
              <input type="radio" name="method" checked={draft.method === "invite"} onChange={() => set("method")("invite")} />
              {t("identity.form.invite")}
            </label>
            <label>
              <input type="radio" name="method" checked={draft.method === "password"} onChange={() => set("method")("password")} />
              {t("identity.form.setPassword")}
            </label>
            {draft.method === "password" && (
              <>
                <div className="field" data-field="password">
                  <label className="field-label" htmlFor={`${id}-password`}>
                    {t("identity.form.password")}
                  </label>
                  <input id={`${id}-password`} name="password" type="password" dir="ltr" autoComplete="new-password" value={draft.password}
                    onChange={(e) => set("password")(e.target.value)} aria-invalid={errors.password ? true : undefined} />
                  {errors.password && <span className="field-error">{errors.password.join(" ")}</span>}
                </div>
                <label>
                  <input type="checkbox" checked={draft.mustChangePassword} onChange={(e) => set("mustChangePassword")(e.target.checked)} />
                  {t("identity.form.mustChange")}
                </label>
              </>
            )}
          </fieldset>
          <p className="muted id-hint">{t("identity.form.keys")}</p>
        </FormSection>
      </div>
    </RecordForm>
  );
}

type DetailDraft = {
  email: string;
  displayName: string;
  displayNameAr: string;
  language: "en" | "ar";
  isActive: boolean;
  roleIds: string[];
  /** Roles that apply in one company only. */
  companyRoles: CompanyRole[];
  /** Where the user starts work (null: their first company by code). */
  startsIn: string | null;
};

/** The user as the form holds it: the record, and where they start work when the caller may see
 * it (null when the caller may not, or it could not be read: the field is then left out). */
type UserRecord = User & { defaultCompany: DefaultCompany | null };

/** One user (the shared record form): details (editable with the right permission), what they can
 * do and why, and their sign-in history, plus the account actions (reset password, sign out
 * everywhere, unblock, delete). */
export function UserDetail({
  userId,
  roles,
  notice: initialNotice,
  nav,
  onSaved,
  onClose,
  onDeleted,
}: {
  userId: string;
  roles: Role[];
  notice?: Notice;
  nav?: RecordNavigation;
  onSaved: (user: User) => void;
  onClose: () => void;
  onDeleted?: (user: User) => void;
}) {
  const { t, language, formatDateTime } = useI18n();
  const { state, can } = useSession();
  const [notice, setNotice] = useState<Notice | undefined>(initialNotice);
  const granted = new Set(state.status === "signedIn" ? state.session.permissions : []);
  const canGrant = (role: Role) => role.permissions.every((p) => granted.has(p));
  // A role in every company needs its grants in every company (roles in the working company's do not count).
  const everywhere = new Set(state.status === "signedIn" ? (state.session.workspacePermissions ?? state.session.permissions) : []);
  const canGrantEverywhere = (role: Role) => role.permissions.every((p) => everywhere.has(p));
  const selfId = state.status === "signedIn" ? state.session.user.id : null;
  const self = selfId === userId;
  const [loaded, setLoaded] = useState<User | null>(null);
  const [companies, setCompanies] = useState<Company[]>([]);
  const allowed = userActions(loaded ?? { id: userId, roleIds: [], lastSignInAt: null }, roles, granted, selfId);
  const form = useRecordForm<UserRecord, DetailDraft>({
    load: async (signal) => {
      // Where the user starts work is not needed to show the record, so a failure leaves it out.
      const [user, defaultCompany] = await Promise.all([
        api<User>("GET", `/api/identity/users/${userId}`, undefined, { signal }),
        api<DefaultCompany>("GET", `/api/identity/users/${userId}/default-company`, undefined, { signal }).then(
          (d) => (d && Array.isArray(d.companies) ? d : null),
          () => null,
        ),
      ]);
      setLoaded(user);
      return { ...user, defaultCompany };
    },
    initial: (u) => ({
      email: u?.email ?? "",
      displayName: u?.displayName ?? "",
      displayNameAr: u?.displayNameAr ?? "",
      language: u?.language ?? "en",
      isActive: u?.isActive ?? true,
      roleIds: u?.roleIds ?? [],
      companyRoles: u?.companyRoles ?? [],
      startsIn: u?.defaultCompany?.companyId ?? null,
    }),
    canEdit: Boolean(loaded) && allowed.edit,
    save: async (draft, user) => {
      const saved = await api<User>("PUT", `/api/identity/users/${userId}`, {
        displayName: draft.displayName.trim(),
        displayNameAr: draft.displayNameAr.trim(),
        language: draft.language,
        isActive: draft.isActive,
        roleIds: draft.roleIds,
        version: user?.version,
        ...(!self && user && draft.email.trim() !== user.email ? { email: draft.email.trim() } : {}),
        ...(!self && user && !sameCompanyRoles(draft.companyRoles, user.companyRoles ?? []) ? { companyRoles: draft.companyRoles } : {}),
      });
      let defaultCompany = user?.defaultCompany ?? null;
      if (!self && defaultCompany && draft.startsIn !== defaultCompany.companyId) {
        defaultCompany = await api<DefaultCompany>("PUT", `/api/identity/users/${userId}/default-company`, {
          companyId: draft.startsIn,
          version: defaultCompany.version,
        });
      }
      return { ...saved, defaultCompany };
    },
    onSaved: (saved) => {
      setLoaded(saved);
      setNotice(undefined);
      onSaved(saved);
    },
  }, userId);
  const user = form.record;

  useEffect(() => setNotice(initialNotice), [initialNotice, userId]);

  // Companies the signed-in user works in: roles in one company are given there. Not needed to
  // show the record, so a failure leaves the editor to the roles the user already holds.
  useEffect(() => {
    let live = true;
    api<Company[]>("GET", "/api/identity/companies").then(
      (list) => live && setCompanies(Array.isArray(list) ? list : []),
      () => live && setCompanies([]),
    );
    return () => {
      live = false;
    };
  }, []);

  if (!user) {
    return <RecordForm form={form} title="" onClose={onClose}>{null}</RecordForm>;
  }

  const bind = form.bind;
  // Why the account is read-only: it reaches companies the signed-in user does not work in, or it
  // grants more than they hold.
  const beyondCompanies =
    loaded?.rolesElsewhere === true || loaded?.refused === "identity.userBeyondOwnCompanies" || loaded?.refused === "identity.userWorksBeyondOwnCompanies";
  const beyondNote = beyondCompanies ? t("identity.users.beyondCompaniesNote") : t("identity.users.beyondOwnNote");
  const details = (
    <>
      {allowed.beyondOwn && <p className="muted">{beyondNote}</p>}
      <FormSection columns={false}>
        <TextField field={bind("email")} label={t("identity.users.email")} type="email" dir="ltr" disabled={self} />
        <TextField field={bind("displayName")} label={t("identity.users.name")} />
        <TextField field={bind("displayNameAr")} label={t("identity.users.nameAr")} dir="rtl" />
        <SelectField
          field={bind("language") as FieldBinding<"en" | "ar" | "">}
          label={t("identity.users.language")}
          options={[
            { value: "en" as const, label: t("identity.language.en") },
            { value: "ar" as const, label: t("identity.language.ar") },
          ]}
        />
        <BooleanField field={bind("isActive")} label={t("identity.form.active")} disabled={self} />
        {can("identity.roles.read") ? (
          <RolePicker roles={roles} selected={form.draft.roleIds} onChange={form.set("roleIds")} canGrant={canGrantEverywhere} disabled={form.readOnly || self} />
        ) : (
          <p className="muted">{t("identity.form.rolesNeedPermission")}</p>
        )}
        {can("identity.roles.read") && (companies.length > 0 || form.draft.companyRoles.length > 0 || user.rolesElsewhere) && (
          <CompanyRolesEditor
            companies={companies}
            roles={roles}
            value={form.draft.companyRoles}
            onChange={form.set("companyRoles")}
            canGrant={canGrant}
            disabled={form.readOnly || self}
            rolesElsewhere={user.rolesElsewhere}
          />
        )}
        {user.defaultCompany && (
          <DefaultCompanyField
            companies={user.defaultCompany.companies}
            value={form.draft.startsIn}
            onChange={form.set("startsIn")}
            disabled={form.readOnly || self}
          />
        )}
        {self && <p className="muted">{t("identity.form.selfNote")}</p>}
      </FormSection>
      {(allowed.resetPassword || allowed.signOutEverywhere || allowed.delete) && (
        <AccountActions user={user} allowed={allowed} onNotice={setNotice} onDeleted={() => onDeleted?.(user)} />
      )}
    </>
  );

  return (
    <div className="id-form">
      <RecordForm
        form={form}
        title={userName(user, language)}
        subtitle={
          <>
            <span dir="ltr">{user.email}</span>
            <span className="id-badges">
              <span className={user.isActive ? "id-badge ok" : "id-badge off"}>{user.isActive ? t("identity.users.active") : t("identity.users.inactive")}</span>
              {user.pendingSetup && <span className="id-badge warn">{t("identity.users.pendingSetup")}</span>}
              <span className="muted">
                {t("identity.users.lastSignIn")}: {user.lastSignInAt ? formatDateTime(user.lastSignInAt) : t("identity.users.never")}
              </span>
            </span>
          </>
        }
        onClose={onClose}
        nav={nav}
        readOnlyReason={allowed.beyondOwn ? beyondNote : undefined}
      >
        {notice?.kind === "code" && <CodeNotice notice={notice} />}
        {notice?.warning && (
          <div className="id-notice warn" role="alert">
            {t("identity.form.followUpFailed", { reason: notice.warning })}
          </div>
        )}
        {notice?.kind === "info" && (
          <div className="id-notice" role="status">
            {notice.text}
          </div>
        )}
        <FormTabs
          label={t("identity.users.sections")}
          tabs={[
            { key: "details", label: t("identity.tab.details"), content: details },
            { key: "access", label: t("identity.tab.access"), content: <AccessTab userId={user.id} roles={roles} language={language} /> },
            { key: "history", label: t("identity.tab.history"), content: <HistoryTab userId={user.id} canUnblock={allowed.unblock} />, hidden: !can("identity.signIns.read") },
          ]}
        />
      </RecordForm>
    </div>
  );
}

/** Reset the password (new set-up code or a temporary password), sign out everywhere, and delete
 * someone who has never signed in. Only the actions the caller may take on this user are shown. */
function AccountActions({
  user,
  allowed,
  onNotice,
  onDeleted,
}: {
  user: User;
  allowed: ReturnType<typeof userActions>;
  onNotice: (notice: Notice) => void;
  onDeleted: () => void;
}) {
  const { t, language } = useI18n();
  const [mode, setMode] = useState<"closed" | "code" | "password">("closed");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);

  async function remove() {
    setBusy(true);
    setError(null);
    try {
      await api<void>("DELETE", `/api/identity/users/${user.id}`);
      onDeleted();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setConfirmDelete(false);
    } finally {
      setBusy(false);
    }
  }

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
        {allowed.resetPassword && (
          <button type="button" className="button" onClick={() => setMode(mode === "closed" ? "code" : "closed")} aria-expanded={mode !== "closed"}>
            {t("identity.reset.open")}
          </button>
        )}
        {allowed.signOutEverywhere && (
          <button type="button" className="button" disabled={busy} onClick={() => void signOutEverywhere()}>
            {t("identity.sessions.revoke")}
          </button>
        )}
        {allowed.delete && !confirmDelete && (
          <button type="button" className="button danger" onClick={() => setConfirmDelete(true)}>
            {t("identity.users.delete")}
          </button>
        )}
      </div>
      {confirmDelete && (
        <p className="id-confirm" role="alert">
          {t("identity.users.deleteConfirm", { name: userName(user, language) })}
          <button type="button" className="button danger" disabled={busy} onClick={() => void remove()}>
            {t("identity.users.deleteYes")}
          </button>
          <button type="button" className="button" onClick={() => setConfirmDelete(false)}>
            {t("identity.form.cancel")}
          </button>
        </p>
      )}
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
  const companiesById = new Map((view.companies ?? []).map((c) => [c.id, c]));
  const where = (companyId: string | null | undefined) =>
    companyId ? (companiesById.get(companyId) ? companyName(companiesById.get(companyId)!, language) : companyId) : t("identity.access.everyCompany");
  // Each reason a permission is held: the role and where it applies.
  const reasons = (p: AccessView["permissions"][number]) =>
    (p.grants ?? p.grantedBy.map((roleId) => ({ roleId, companyId: null }))).map((g) =>
      g.companyId ? t("identity.access.inCompany", { role: names.get(g.roleId) ?? g.roleId, company: where(g.companyId) }) : names.get(g.roleId) ?? g.roleId,
    );
  const modules = [...new Set(view.permissions.map((p) => p.moduleLabel))];
  const distinctRoles = new Set(view.roles.map((r) => `${r.id}/${r.companyId ?? ""}`)).size;
  return (
    <div className="id-access">
      <p>{t("identity.access.summary", { count: view.permissions.length, roles: distinctRoles })}</p>
      {view.roles.some((r) => r.companyId) && <p className="muted">{t("identity.access.companyNote")}</p>}
      {view.rolesElsewhere && <p className="muted">{t("identity.companyRoles.elsewhere")}</p>}
      {view.permissions.length === 0 && <p className="muted">{t("identity.access.nothing")}</p>}
      {modules.map((module) => (
        <table key={module} className="grid id-access-table">
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
                  <td>{reasons(p).join(language === "ar" ? "، " : ", ")}</td>
                </tr>
              ))}
          </tbody>
        </table>
      ))}
    </div>
  );
}

/** Sign-in history, newest first, and the clients paused now (with Unblock). */
function HistoryTab({ userId, canUnblock }: { userId: string; canUnblock: boolean }) {
  const { t, formatDateTime } = useI18n();
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
              <li key={p.source}>
                {p.source.startsWith("device:") ? <bdi>{t("identity.history.knownDevice")}</bdi> : <bdi dir="ltr">{p.source}</bdi>} ·{" "}
                <bdi>{t("identity.history.pausedUntil", { time: formatDateTime(p.until) })}</bdi>
              </li>
            ))}
          </ul>
          {canUnblock && (
            <button type="button" className="button" onClick={() => void unblock()}>
              {t("identity.history.unblock")}
            </button>
          )}
        </div>
      )}
      <p className="muted">{t("identity.history.count", { count: history.total })}</p>
      <div className="id-scroll">
      <table className="grid id-history-table">
        <thead>
          <tr>
            <th scope="col">
              {t("identity.history.when")}
              <span className="id-sub">{t("identity.history.address")}</span>
            </th>
            <th scope="col">
              {t("identity.history.outcome")}
              <span className="id-sub">{t("identity.history.session")}</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {history.items.map((a) => (
            <tr key={a.id}>
              <td>
                {formatDateTime(a.occurredAt)}
                <span className="id-sub" title={a.userAgent ?? undefined}>
                  <bdi dir="ltr">{a.ipAddress ?? "—"}</bdi>
                </span>
              </td>
              <td>
                <span className={a.outcome === "succeeded" ? "id-badge ok" : "id-badge off"}>{t(`identity.outcome.${a.outcome}`)}</span>
                {a.sessionActive && <span className="id-sub">{t("identity.history.active")}</span>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      </div>
    </div>
  );
}

export type { Notice };
