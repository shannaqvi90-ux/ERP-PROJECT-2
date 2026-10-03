import { useEffect, useRef, useState, type FormEvent } from "react";
import { useI18n, type Language } from "../../kernel/i18n";
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

/** The screen's own wording of the answers sign-in can give, so a message follows a language
 * switch instead of staying in the language it arrived in. */
const problemKeys: Record<string, string> = {
  "auth.signInFailed": "shell.signIn.problem.signInFailed",
  "auth.chooseWorkspace": "shell.signIn.problem.chooseWorkspace",
  "auth.passwordChangeRequired": "shell.signIn.problem.passwordChangeRequired",
  "request.tooMany": "shell.signIn.problem.tooMany",
};

/** A message shown on the sign-in screen: one of the screen's own strings (re-rendered in the
 * current language), or the server's text in the language it was written in. */
type Message = { key: string } | { text: string; language: Language };

const fromServer = (text: string, language: Language, code?: string): Message =>
  code && problemKeys[code] ? { key: problemKeys[code] } : { text, language };

/**
 * The first screen. Keyboard first: the e-mail field has focus (or the password field, when this
 * device remembers the last e-mail), Enter signs in. The e-mail — never the password — is
 * remembered on this device.
 */
export function SignInPage() {
  const { t, language } = useI18n();
  const { signIn } = useSession();
  // A set-up link may carry the e-mail (never the code); otherwise this device's last one.
  const remembered = new URLSearchParams(window.location.search).get("email") ?? rememberedEmail();
  const [email, setEmail] = useState(remembered);
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Message | null>(null);
  const [fieldErrors, setFieldErrors] = useState<{ email?: string; password?: string }>({});
  const [workspaces, setWorkspaces] = useState<Workspace[] | null>(null);
  // A one-time set-up code (or a temporary password) was accepted: choose your own password.
  const [changing, setChanging] = useState(false);
  const [newPassword, setNewPassword] = useState("");
  const [repeat, setRepeat] = useState("");
  const [newErrors, setNewErrors] = useState<{ newPassword?: string; repeat?: string }>({});
  const newPasswordRef = useRef<HTMLInputElement>(null);
  const emailRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const workspaceRef = useRef<HTMLButtonElement>(null);

  // Keyboard first: the first empty field has focus on arrival and again after switching
  // language, so the next keystroke always types into the form.
  useEffect(() => {
    (emailRef.current?.value ? passwordRef : emailRef).current?.focus();
  }, [language]);

  useEffect(() => {
    if (workspaces) workspaceRef.current?.focus();
  }, [workspaces]);

  useEffect(() => {
    if (changing) newPasswordRef.current?.focus();
  }, [changing]);

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
    if (changing) {
      const next: { newPassword?: string; repeat?: string } = {};
      if (newPassword.length < 10) next.newPassword = t("shell.signIn.newPasswordShort");
      else if (newPassword !== repeat) next.repeat = t("shell.signIn.passwordsDiffer");
      setNewErrors(next);
      if (next.newPassword || next.repeat) {
        newPasswordRef.current?.focus();
        return;
      }
    }
    setBusy(true);
    setError(null);
    try {
      const result = await signIn(email.trim(), password, workspace, changing ? newPassword : undefined);
      if (result.kind === "ok") {
        try {
          localStorage.setItem(lastEmailKey, email.trim());
        } catch {
          // Not remembered on this device.
        }
        return;
      }
      if (result.kind === "changePassword") {
        setChanging(true);
        setError(fromServer(result.message, language, "auth.passwordChangeRequired"));
        return;
      }
      if (result.kind === "invalid" && result.fieldErrors.newPassword?.[0]) {
        setNewErrors({ newPassword: result.fieldErrors.newPassword[0].message });
        newPasswordRef.current?.focus();
        return;
      }
      if (result.kind === "chooseWorkspace") {
        setWorkspaces(result.workspaces);
        setError(fromServer(result.message, language, "auth.chooseWorkspace"));
      } else {
        setWorkspaces(null);
        setError(fromServer(result.message, language, result.kind === "failed" ? result.code : undefined));
        setPassword("");
        passwordRef.current?.focus();
      }
    } catch {
      setError({ key: "shell.signIn.unreachable" });
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
          {!changing && <p className="muted signin-hint">{t("shell.signIn.setupHint")}</p>}
          {changing && (
            <>
              <label className="field">
                <span className="field-label">{t("shell.signIn.newPassword")}</span>
                <input
                  ref={newPasswordRef}
                  name="newPassword"
                  type="password"
                  dir="ltr"
                  autoComplete="new-password"
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  aria-invalid={newErrors.newPassword ? true : undefined}
                />
                {newErrors.newPassword && <span className="field-error">{newErrors.newPassword}</span>}
              </label>
              <label className="field">
                <span className="field-label">{t("shell.signIn.repeatPassword")}</span>
                <input
                  name="repeatPassword"
                  type="password"
                  dir="ltr"
                  autoComplete="new-password"
                  value={repeat}
                  onChange={(e) => setRepeat(e.target.value)}
                  aria-invalid={newErrors.repeat ? true : undefined}
                />
                {newErrors.repeat && <span className="field-error">{newErrors.repeat}</span>}
              </label>
            </>
          )}
          {error && (
            <div
              id="signin-error"
              className={changing ? "notice" : "alert"}
              role={changing ? "status" : "alert"}
              {...("key" in error ? {} : { lang: error.language, dir: "auto" })}
            >
              {"key" in error ? t(error.key) : error.text}
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
