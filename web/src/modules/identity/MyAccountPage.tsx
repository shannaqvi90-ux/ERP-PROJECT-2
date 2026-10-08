import { useState, type FormEvent } from "react";
import { api, ApiError } from "../../kernel/api";
import { useI18n, type Language } from "../../kernel/i18n";
import { useSession } from "../../kernel/session";
import { teamSignInAddress } from "../../kernel/signInAddress";
import { formKeys } from "../../kernel/forms/RecordForm";
import { Field } from "../../kernel/forms/fields";
import { PasskeysSection } from "./PasskeysSection";
import "./identity.css";

/**
 * The signed-in user's own account: interface language, password and passkeys. Changing the password
 * proves the current one (through sign-in) and ends every other session of the account.
 * Not a record form (nothing to load, no version): it uses the kernel's field shape (`Field`:
 * label, hint and message linked to the input) and the kernel's save keys (`formKeys`).
 */
export function MyAccountPage() {
  const { t, language, setLanguage } = useI18n();
  const { state, refresh } = useSession();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [repeat, setRepeat] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [copied, setCopied] = useState(false);
  if (state.status !== "signedIn") return null;
  const { user, tenant } = state.session;
  const teamAddress = teamSignInAddress(window.location.origin, user.email);

  async function chooseLanguage(value: Language) {
    setLanguage(value);
    try {
      await api("PUT", "/api/identity/me/preferences", { language: value });
      setDone(t("identity.me.languageSaved"));
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  async function changePassword(event: FormEvent) {
    event.preventDefault();
    const local: Record<string, string> = {};
    if (!current) local.current = t("identity.me.currentRequired");
    if (next.length < 10) local.next = t("identity.form.passwordShort");
    else if (next !== repeat) local.repeat = t("identity.me.mismatch");
    setErrors(local);
    setDone(null);
    setError(null);
    if (Object.keys(local).length) return;
    setBusy(true);
    try {
      await api("POST", "/api/auth/sign-in", { email: user.email, password: current, newPassword: next, workspace: tenant.code });
      setCurrent("");
      setNext("");
      setRepeat("");
      setDone(t("identity.me.passwordChanged"));
      await refresh();
    } catch (e) {
      const refused = e instanceof ApiError ? e.fieldErrors.newPassword?.[0] : undefined;
      if (refused) setErrors({ next: refused.message });
      else setError(e instanceof ApiError && e.status === 401 ? t("identity.me.currentWrong") : e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="id-me">
      <h1>{t("identity.me.title")}</h1>
      <dl className="facts">
        <dt>{t("identity.users.name")}</dt>
        <dd lang="en" dir="auto">{user.displayName}</dd>
        {user.displayNameAr && (
          <>
            <dt>{t("identity.users.nameAr")}</dt>
            <dd lang="ar" dir="rtl">{user.displayNameAr}</dd>
          </>
        )}
        <dt>{t("identity.users.email")}</dt>
        <dd dir="ltr">{user.email}</dd>
        <dt>{t("identity.me.teamAddress")}</dt>
        <dd>
          <span className="id-team-address" dir="ltr" data-testid="team-address">
            {teamAddress}
          </span>{" "}
          <button
            type="button"
            className="button"
            onClick={() => {
              void navigator.clipboard?.writeText(teamAddress).then(() => setCopied(true), () => setCopied(false));
            }}
          >
            {copied ? t("identity.code.copied") : t("identity.me.copyAddress")}
          </button>
          <div className="muted">{t("identity.me.teamAddressHint")}</div>
        </dd>
      </dl>
      {done && (
        <div className="id-notice" role="status">
          {done}
        </div>
      )}
      {error && (
        <div className="alert" role="alert">
          {error}
        </div>
      )}
      <fieldset className="id-method">
        <legend className="field-label">{t("identity.users.language")}</legend>
        <label>
          <input type="radio" name="language" checked={language === "en"} onChange={() => void chooseLanguage("en")} />
          {t("identity.language.en")}
        </label>
        <label>
          <input type="radio" name="language" checked={language === "ar"} onChange={() => void chooseLanguage("ar")} />
          {t("identity.language.ar")}
        </label>
      </fieldset>
      <form
        className="id-form"
        noValidate
        onSubmit={(e) => void changePassword(e)}
        onKeyDown={(e) => formKeys(e, () => e.currentTarget.requestSubmit(), () => undefined)}
      >
        <h2>{t("identity.me.changePassword")}</h2>
        <Field name="current" label={t("identity.me.current")} errors={errors.current ? [errors.current] : []}>
          {(a) => <input {...a} name="current" type="password" dir="ltr" autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} />}
        </Field>
        <Field name="new" label={t("identity.me.new")} errors={errors.next ? [errors.next] : []}>
          {(a) => <input {...a} name="new" type="password" dir="ltr" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} />}
        </Field>
        <Field name="repeat" label={t("identity.me.repeat")} errors={errors.repeat ? [errors.repeat] : []}>
          {(a) => <input {...a} name="repeat" type="password" dir="ltr" autoComplete="new-password" value={repeat} onChange={(e) => setRepeat(e.target.value)} />}
        </Field>
        <p className="muted">{t("identity.me.othersEnd")}</p>
        <button type="submit" className="button primary" disabled={busy}>
          {t("identity.me.changePassword")}
        </button>
      </form>
      <PasskeysSection />
    </section>
  );
}
