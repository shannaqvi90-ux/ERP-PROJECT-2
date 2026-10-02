import { useEffect, useRef, useState, type FormEvent } from "react";
import { useI18n } from "../../kernel/i18n";
import { useSession, type Workspace } from "../../kernel/session";
import { LanguageToggle } from "./LanguageToggle";

const lastEmailKey = "erp.lastEmail";

function rememberedEmail(): string {
  try {
    return localStorage.getItem(lastEmailKey) ?? "";
  } catch {
    return "";
  }
}

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * The first screen. Keyboard first: the e-mail field has focus (or the password field, when this
 * device remembers the last e-mail), Enter signs in. The e-mail — never the password — is
 * remembered on this device.
 */
export function SignInPage() {
  const { t, language } = useI18n();
  const { signIn } = useSession();
  const remembered = rememberedEmail();
  const [email, setEmail] = useState(remembered);
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<{ email?: string; password?: string }>({});
  const [workspaces, setWorkspaces] = useState<Workspace[] | null>(null);
  const emailRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const workspaceRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    (remembered ? passwordRef : emailRef).current?.focus();
  }, [remembered]);

  useEffect(() => {
    if (workspaces) workspaceRef.current?.focus();
  }, [workspaces]);

  async function submit(workspace?: string) {
    const errors: { email?: string; password?: string } = {};
    if (!email.trim()) errors.email = t("shell.signIn.emailRequired");
    else if (!emailPattern.test(email.trim())) errors.email = t("shell.signIn.emailInvalid");
    if (!password) errors.password = t("shell.signIn.passwordRequired");
    setFieldErrors(errors);
    if (errors.email || errors.password) {
      (errors.email ? emailRef : passwordRef).current?.focus();
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const result = await signIn(email.trim(), password, workspace);
      if (result.kind === "ok") {
        try {
          localStorage.setItem(lastEmailKey, email.trim());
        } catch {
          // Not remembered on this device.
        }
        return;
      }
      if (result.kind === "chooseWorkspace") {
        setWorkspaces(result.workspaces);
        setError(result.message);
      } else {
        setWorkspaces(null);
        setError(result.message);
        setPassword("");
        passwordRef.current?.focus();
      }
    } catch {
      setError(t("shell.signIn.unreachable"));
    } finally {
      setBusy(false);
    }
  }

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    void submit();
  }

  return (
    <div className="signin">
      <div className="signin-panel">
        <div className="signin-header">
          <h1 className="signin-title">{t("shell.app.name")}</h1>
          <LanguageToggle />
        </div>
        <p className="signin-lead">{t("shell.signIn.lead")}</p>
        <form onSubmit={onSubmit} noValidate aria-describedby={error ? "signin-error" : undefined}>
          <label className="field">
            <span className="field-label">{t("shell.signIn.email")}</span>
            <input
              ref={emailRef}
              name="email"
              type="email"
              dir="ltr"
              autoComplete="username"
              inputMode="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              aria-invalid={fieldErrors.email ? true : undefined}
              aria-describedby={fieldErrors.email ? "email-error" : undefined}
            />
            {fieldErrors.email && (
              <span id="email-error" className="field-error">
                {fieldErrors.email}
              </span>
            )}
          </label>
          <label className="field">
            <span className="field-label">{t("shell.signIn.password")}</span>
            <input
              ref={passwordRef}
              name="password"
              type="password"
              dir="ltr"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              aria-invalid={fieldErrors.password ? true : undefined}
              aria-describedby={fieldErrors.password ? "password-error" : undefined}
            />
            {fieldErrors.password && (
              <span id="password-error" className="field-error">
                {fieldErrors.password}
              </span>
            )}
          </label>
          {error && (
            <div id="signin-error" className="alert" role="alert">
              {error}
            </div>
          )}
          {workspaces && workspaces.length > 0 && (
            <div className="workspaces" role="group" aria-label={t("shell.signIn.chooseWorkspace")}>
              {workspaces.map((w, i) => (
                <button
                  key={w.code}
                  ref={i === 0 ? workspaceRef : undefined}
                  type="button"
                  className="button"
                  disabled={busy}
                  onClick={() => void submit(w.code)}
                >
                  {language === "ar" ? w.nameAr : w.nameEn}
                </button>
              ))}
            </div>
          )}
          <button type="submit" className="button primary signin-submit" disabled={busy}>
            {busy ? t("shell.signIn.busy") : t("shell.signIn.submit")}
          </button>
        </form>
      </div>
    </div>
  );
}
