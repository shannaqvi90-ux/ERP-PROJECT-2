import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { useDocumentTitle, useI18n, type Language } from "../../kernel/i18n";
import { rememberedEmailKey as lastEmailKey } from "../../kernel/deviceState";
import { completesTeamEmail, fullEmail, teamDomain } from "../../kernel/signInAddress";
import { freshPasskeyChallenge, useSession, type Workspace } from "../../kernel/session";
import { askForPasskey, cancelledByPerson, passkeyOffered, passkeysSupported, setPasskeyOffered, takeSignedOut, type PasskeySignIn } from "../../kernel/passkeys";
import { LanguageToggle } from "./LanguageToggle";

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
  "auth.passkeyFailed": "shell.signIn.problem.passkeyFailed",
  "request.tooMany": "shell.signIn.problem.tooMany",
};

/** A message shown on the sign-in screen: one of the screen's own strings (re-rendered in the
 * current language), or the server's text in the language it was written in. */
type Message = { key: string } | { text: string; language: Language };

const fromServer = (text: string, language: Language, code?: string): Message =>
  code && problemKeys[code] ? { key: problemKeys[code] } : { text, language };

/** Failed password sign-ins in a row on this screen after which it says that sign-in may pause. */
const failuresBeforeHint = 3;

/**
 * The first screen. Keyboard first: the e-mail field has focus (or the password field, when this
 * device remembers the last e-mail), Enter signs in. The e-mail — never the password — is
 * remembered on this device until the person signs out (see kernel/deviceState.ts).
 *
 * Passkeys: "Continue with a passkey" asks the device for any passkey of this site (no e-mail
 * typed). On a device where a passkey was added or used, the screen asks at once as it opens, from
 * any address (plain, the team's, a personal bookmark), so signing in is one confirmation on the
 * device; not right after the Sign out button, and cancelling leaves the e-mail and password.
 */
export function SignInPage() {
  const { t, language } = useI18n();
  const { signIn, signInWithPasskey, state } = useSession();
  const passkeyRequest = state.status === "anonymous" ? state.passkey : undefined;
  const [canUsePasskey] = useState(passkeysSupported);
  // Read once: this screen comes right after the Sign out button.
  const [justSignedOut] = useState(takeSignedOut);
  const [asking, setAsking] = useState(false);
  const askedOnArrival = useRef(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [failures, setFailures] = useState(0);
  useDocumentTitle("shell.signIn.title");
  // A set-up link may carry the e-mail (never the code); otherwise this device's last one. On the
  // team's sign-in address the domain is filled in: the person types only the part before "@".
  // An e-mail the screen already knows is shown whole (the domain is not repeated after it): it is
  // the person's own sign-in, exactly as it will be sent.
  const [domain] = useState(() => teamDomain(window.location.search));
  const remembered = new URLSearchParams(window.location.search).get("email") ?? rememberedEmail();
  const [email, setEmail] = useState(remembered);
  const suffix = domain && !email.includes("@") ? `@${domain}` : null;
  const [password, setPassword] = useState("");
  // The password may be shown while it is typed (checked before sending); Caps Lock is announced.
  const [showPassword, setShowPassword] = useState(false);
  const [capsLock, setCapsLock] = useState(false);
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
  // The screen moved the focus on to the password by itself (the whole team e-mail was typed).
  const movedOn = useRef(false);

  // Keyboard first: the first empty field has focus on arrival and again after switching
  // language, so the next keystroke always types into the form.
  useEffect(() => {
    (emailRef.current?.value ? passwordRef : emailRef).current?.focus();
  }, [language]);

  useEffect(() => {
    if (workspaces) workspaceRef.current?.focus();
  }, [workspaces]);

  // A device that holds a passkey of this site is asked as soon as the screen opens.
  useEffect(() => {
    if (askedOnArrival.current || !passkeyRequest || !canUsePasskey || justSignedOut || !passkeyOffered()) return;
    askedOnArrival.current = true;
    void passkeySignIn(passkeyRequest);
    // Once, on arrival: the challenge the session answer carried.
  }, [passkeyRequest]);

  /** Asks the device for a passkey and signs in with its answer. */
  async function passkeySignIn(request?: PasskeySignIn) {
    if (asking || busy) return;
    setAsking(true);
    setError(null);
    setNotice(null);
    let signedIn = false;
    try {
      const challenge = request ?? (await freshPasskeyChallenge());
      if (!challenge) {
        setError({ key: "shell.signIn.unreachable" });
        return;
      }
      const assertion = await askForPasskey(challenge);
      setBusy(true);
      const result = await signInWithPasskey(assertion);
      if (result.kind === "ok") {
        signedIn = true;
        setPasskeyOffered(true);
        try {
          localStorage.setItem(lastEmailKey, result.session.user.email);
        } catch {
          // Not remembered on this device.
        }
        return;
      }
      setError(fromServer(result.message, language, result.kind === "failed" ? result.code : undefined));
    } catch (problem) {
      if (cancelledByPerson(problem)) setNotice("shell.signIn.passkeyCancelled");
      else setError({ key: "shell.signIn.passkeyUnavailable" });
    } finally {
      setAsking(false);
      setBusy(false);
      if (!signedIn) (emailRef.current?.value ? passwordRef : emailRef).current?.focus();
    }
  }

  useEffect(() => {
    if (changing) newPasswordRef.current?.focus();
  }, [changing]);

  function emailProblem(): string | undefined {
    const value = fullEmail(email, domain);
    if (!value) return t("shell.signIn.emailRequired");
    if (!emailPattern.test(value)) return t("shell.signIn.emailInvalid");
    return undefined;
  }

  /** Enter in the e-mail field while the password is still empty goes on to the password, as
   * Tab does, without calling the problem of a missing password an error. */
  function onEmailKey(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key !== "Enter" || password || changing || event.nativeEvent.isComposing) return;
    event.preventDefault();
    const problem = emailProblem();
    setFieldErrors(problem ? { email: problem } : {});
    if (!problem) passwordRef.current?.focus();
  }

  /** On the team's address, typing the whole e-mail (the team's domain included) ends the field:
   * the screen moves on to the password, as Tab or Enter would (the note under the field says so
   * beforehand). Only while the password is empty, and only when the address becomes whole. */
  function onEmailChange(value: string) {
    const wasWhole = completesTeamEmail(email, domain);
    setEmail(value);
    if (wasWhole || changing || password || !completesTeamEmail(value, domain)) return;
    movedOn.current = true;
    setFieldErrors((errors) => ({ ...errors, email: undefined }));
    passwordRef.current?.focus();
  }

  /** Caps Lock as the last key event in the password field reports it (no other way to read it). */
  function onPasswordKey(event: KeyboardEvent<HTMLInputElement>) {
    setCapsLock(event.getModifierState?.("CapsLock") === true);
  }

  /** Backspace in the still-empty password after the screen moved on goes back to the e-mail's end. */
  function onPasswordKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    onPasswordKey(event);
    if (event.key !== "Backspace" || password || !movedOn.current) return;
    event.preventDefault();
    movedOn.current = false;
    const field = emailRef.current;
    field?.focus();
    field?.setSelectionRange(field.value.length, field.value.length);
  }

  async function submit(workspace?: string) {
    const errors: { email?: string; password?: string } = {};
    const signInEmail = fullEmail(email, domain);
    errors.email = emailProblem();
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
    setNotice(null);
    try {
      const result = await signIn(signInEmail, password, workspace, changing ? newPassword : undefined);
      if (result.kind === "failed" && result.code === "auth.signInFailed") setFailures((count) => count + 1);
      if (result.kind === "ok") {
        try {
          localStorage.setItem(lastEmailKey, signInEmail);
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
            <span id="signin-email-label" className="field-label">{t("shell.signIn.email")}</span>
            <span className={suffix ? "signin-email has-domain" : "signin-email"} dir="ltr">
              <input
                ref={emailRef}
                name="email"
                // Named by its label alone: the domain, the note and an error inside the label describe it.
                aria-labelledby="signin-email-label"
                type={domain ? "text" : "email"}
                dir="ltr"
                autoComplete={suffix ? "off" : "username"}
                autoCapitalize="off"
                spellCheck={false}
                inputMode="email"
                value={email}
                onChange={(e) => onEmailChange(e.target.value)}
                onKeyDown={onEmailKey}
                aria-invalid={fieldErrors.email ? true : undefined}
                aria-describedby={[suffix ? "email-domain" : "", domain && !changing ? "email-moves-on" : "", fieldErrors.email ? "email-error" : ""].filter(Boolean).join(" ") || undefined}
              />
              {suffix && (
                <span id="email-domain" className="signin-domain" title={t("shell.signIn.domainHint")}>
                  {suffix}
                  <span className="visually-hidden"> {t("shell.signIn.domainHint")}</span>
                </span>
              )}
            </span>
            {domain && !changing && (
              <span id="email-moves-on" className="muted signin-note">
                {t("shell.signIn.domainMovesOn")}
              </span>
            )}
            {fieldErrors.email && (
              <span id="email-error" className="field-error">
                {fieldErrors.email}
              </span>
            )}
          </label>
          {suffix && (
            // Password managers save and fill the whole e-mail, not the part typed in the field.
            <input className="visually-hidden" type="email" name="username" aria-label={t("shell.signIn.email")} autoComplete="username" value={fullEmail(email, domain)} readOnly tabIndex={-1} aria-hidden="true" />
          )}
          <div className="field">
            <label className="field-label" htmlFor="signin-password">{t("shell.signIn.password")}</label>
            <span className="signin-password" dir="ltr">
              <input
                ref={passwordRef}
                id="signin-password"
                name="password"
                type={showPassword ? "text" : "password"}
                dir="ltr"
                autoComplete="current-password"
                autoCapitalize="off"
                spellCheck={false}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                onKeyDown={onPasswordKeyDown}
                onKeyUp={onPasswordKey}
                onBlur={() => setCapsLock(false)}
                aria-invalid={fieldErrors.password ? true : undefined}
                aria-describedby={[capsLock ? "caps-lock" : "", fieldErrors.password ? "password-error" : ""].filter(Boolean).join(" ") || undefined}
              />
              <button
                type="button"
                className="signin-reveal"
                aria-label={t(showPassword ? "shell.signIn.hidePassword" : "shell.signIn.showPassword")}
                title={t(showPassword ? "shell.signIn.hidePassword" : "shell.signIn.showPassword")}
                onClick={() => {
                  setShowPassword((shown) => !shown);
                  passwordRef.current?.focus();
                }}
              >
                {t(showPassword ? "shell.signIn.hide" : "shell.signIn.show")}
              </button>
            </span>
            {capsLock && (
              <span id="caps-lock" className="signin-caps" role="status">
                {t("shell.signIn.capsLock")}
              </span>
            )}
            {fieldErrors.password && (
              <span id="password-error" className="field-error">
                {fieldErrors.password}
              </span>
            )}
          </div>
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
              {failures >= failuresBeforeHint && !changing && <p className="signin-pause-hint">{t("shell.signIn.repeatedFailures")}</p>}
            </div>
          )}
          {notice && !error && (
            <div className="notice" role="status">
              {t(notice)}
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
          <button type="submit" className="button primary signin-submit" disabled={busy || asking}>
            {busy ? t("shell.signIn.busy") : t("shell.signIn.submit")}
          </button>
          {canUsePasskey && passkeyRequest && !changing && (
            <>
              <div className="signin-or">
                <span>{t("shell.signIn.or")}</span>
              </div>
              <button type="button" className="button signin-passkey" disabled={busy || asking} aria-describedby={asking ? "passkey-asking" : undefined} onClick={() => void passkeySignIn()}>
                {t("shell.signIn.passkey")}
              </button>
              {asking && (
                <span id="passkey-asking" className="muted signin-note" role="status">
                  {t("shell.signIn.passkeyAsking")}
                </span>
              )}
            </>
          )}
        </form>
      </div>
    </div>
  );
}
