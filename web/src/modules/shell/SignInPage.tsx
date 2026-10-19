import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { useDocumentTitle, useI18n, type Language } from "../../kernel/i18n";
import { rememberedEmailKey as lastEmailKey } from "../../kernel/deviceState";
import { completesKnownEmail, completesTeamEmail, deviceKnowsSignInDomains, fullEmail, rememberSignInDomain, teamDomain } from "../../kernel/signInAddress";
import { freshPasskeyChallenge, useSession, type Workspace } from "../../kernel/session";
import { askForPasskey, cancelledByPerson, notePasswordSignIn, passkeyAskedOnArrival, passkeysSupported, setPasskeyOffered, takeSignedOut, type PasskeySignIn } from "../../kernel/passkeys";
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
 * current language), or the server's text in the language it was written in. `pausedUntil`: sign-in
 * with the address is paused until then (this device's clock), shown with the time and minutes. */
type Message = { key: string; pausedUntil?: number; deviceCode?: string } | { text: string; language: Language };

const fromServer = (text: string, language: Language, code?: string): Message =>
  code && problemKeys[code] ? { key: problemKeys[code] } : { text, language };

/** The screen's message for a failed sign-in: a pause says until when (critic p00 rounds 7 to 10). */
const failureMessage = (result: { message: string; code?: string; pausedFor?: number; deviceCode?: string }, language: Language): Message =>
  result.code === "auth.signInPaused" && result.pausedFor !== undefined
    ? result.deviceCode
      ? // A shared address (critic p03 round 12): the failures may be anyone's behind it, so the
        // screen says so and shows this browser's device code to be let in with.
        { key: "shell.signIn.problem.signInPausedShared", pausedUntil: Date.now() + result.pausedFor * 1000, deviceCode: result.deviceCode }
      : { key: "shell.signIn.problem.signInPaused", pausedUntil: Date.now() + result.pausedFor * 1000 }
    : fromServer(result.message, language, result.code);

/** Failed password sign-ins in a row on this screen after which it says that sign-in may pause. */
const failuresBeforeHint = 3;

/**
 * The first screen. Keyboard first: the e-mail field has focus (or the password field, when this
 * device remembers the last e-mail), Enter signs in. The e-mail — never the password — is
 * remembered on this device until the person signs out (see kernel/deviceState.ts).
 *
 * Passkeys: "Continue with a passkey" asks the device for any passkey of this site (no e-mail
 * typed). On a device whose last sign-in used a passkey, the screen asks at once as it opens, from
 * any address (plain, the team's, a personal bookmark), so signing in is one confirmation on the
 * device; not right after the Sign out button, and cancelling leaves the e-mail and password.
 * Asking never blocks the password (round 9, a shared PC): the Sign in button stays enabled, and
 * typing in either field or sending the form withdraws the request (the browser closes its
 * prompt) without a message and without moving the keyboard. After a password sign-in the next
 * screen of the device does not ask as it opens (kernel/passkeys.ts).
 *
 * The whole address moves on to the password wherever the screen knows where it ends: on the
 * team's address (its domain), and on any address for a domain this device signed in with before
 * (kernel/signInAddress.ts, nothing looked up), so the next person at a shared PC types the whole
 * e-mail, the password and Enter from the plain address too.
 */
export function SignInPage() {
  const { t, language, format } = useI18n();
  const { signIn, signInWithPasskey, state } = useSession();
  const passkeyRequest = state.status === "anonymous" ? state.passkey : undefined;
  const [canUsePasskey] = useState(passkeysSupported);
  // Read once: this screen comes right after the Sign out button.
  const [justSignedOut] = useState(takeSignedOut);
  const [asking, setAsking] = useState(false);
  const askedOnArrival = useRef(false);
  // The device request in progress, withdrawn when the person turns to the password instead.
  const passkeyAbort = useRef<AbortController | null>(null);
  const withdrawn = useRef(false);
  // Read once: whether this device signed in to a workspace before (a whole address then moves on).
  const [knowsDomains] = useState(deviceKnowsSignInDomains);
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
  // The screen moved the focus on to the password by itself (a whole address it knows was typed).
  const movedOn = useRef(false);
  // The key a person presses by habit to leave the e-mail field (Tab, or Enter), right after the
  // screen moved on by itself, is absorbed once: it would otherwise skip past the password.
  const absorbLeavingKey = useRef(false);

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
    if (askedOnArrival.current || !passkeyRequest || !canUsePasskey || justSignedOut || !passkeyAskedOnArrival()) return;
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
    const controller = new AbortController();
    passkeyAbort.current = controller;
    withdrawn.current = false;
    let signedIn = false;
    let sending = false;
    try {
      const challenge = request ?? (await freshPasskeyChallenge());
      if (controller.signal.aborted) return;
      if (!challenge) {
        setError({ key: "shell.signIn.unreachable" });
        return;
      }
      const assertion = await askForPasskey(challenge, controller.signal);
      if (controller.signal.aborted) return;
      passkeyAbort.current = null;
      sending = true;
      setBusy(true);
      const result = await signInWithPasskey(assertion);
      if (result.kind === "ok") {
        signedIn = true;
        setPasskeyOffered(true);
        rememberSignInDomain(result.session.user.email);
        try {
          localStorage.setItem(lastEmailKey, result.session.user.email);
        } catch {
          // Not remembered on this device.
        }
        return;
      }
      setError(fromServer(result.message, language, result.kind === "failed" ? result.code : undefined));
    } catch (problem) {
      // Withdrawn because the person turned to the password: nothing to say.
      if (withdrawn.current) return;
      if (cancelledByPerson(problem)) setNotice("shell.signIn.passkeyCancelled");
      else setError({ key: "shell.signIn.passkeyUnavailable" });
    } finally {
      if (passkeyAbort.current === controller) passkeyAbort.current = null;
      setAsking(false);
      if (sending) setBusy(false);
      // The keyboard stays where the person is typing when they withdrew the request.
      if (!signedIn && !withdrawn.current) (emailRef.current?.value ? passwordRef : emailRef).current?.focus();
      withdrawn.current = false;
    }
  }

  /** The person turned to the password (typed in a field, or sent the form): the device request is
   * withdrawn, so the browser's prompt closes and nothing waits on it. */
  function withdrawPasskeyRequest() {
    const controller = passkeyAbort.current;
    if (!controller) return;
    passkeyAbort.current = null;
    withdrawn.current = true;
    controller.abort();
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
   * beforehand). The same on any address for a domain this device signed in with before. Only
   * while the password is empty, and only when the address becomes whole. */
  function onEmailChange(value: string) {
    withdrawPasskeyRequest();
    const ends = (text: string) => completesTeamEmail(text, domain) || completesKnownEmail(text);
    const wasWhole = ends(email);
    setEmail(value);
    if (wasWhole || changing || password || !ends(value)) return;
    movedOn.current = true;
    absorbLeavingKey.current = true;
    setFieldErrors((errors) => ({ ...errors, email: undefined }));
    passwordRef.current?.focus();
  }

  /** Caps Lock as the last key event in the password field reports it (no other way to read it). */
  function onPasswordKey(event: KeyboardEvent<HTMLInputElement>) {
    setCapsLock(event.getModifierState?.("CapsLock") === true);
  }

  /** Backspace in the still-empty password after the screen moved on goes back to the e-mail's end.
   * Tab or Enter as the very next key after the screen moved on (a person leaving the e-mail field
   * by habit, as in other products) is absorbed: the screen already went on, so the key neither
   * skips past the password nor sends the form without it. Any other key ends that. */
  function onPasswordKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    onPasswordKey(event);
    const absorb = absorbLeavingKey.current;
    absorbLeavingKey.current = false;
    if (absorb && !password && ((event.key === "Tab" && !event.shiftKey) || event.key === "Enter") && !event.nativeEvent.isComposing) {
      event.preventDefault();
      return;
    }
    if (event.key !== "Backspace" || password || !movedOn.current) return;
    event.preventDefault();
    movedOn.current = false;
    const field = emailRef.current;
    field?.focus();
    field?.setSelectionRange(field.value.length, field.value.length);
  }

  async function submit(workspace?: string) {
    withdrawPasskeyRequest();
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
        rememberSignInDomain(signInEmail);
        notePasswordSignIn();
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
        setError(result.kind === "failed" ? failureMessage(result, language) : fromServer(result.message, language));
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
                aria-describedby={[suffix ? "email-domain" : "", (domain || knowsDomains) && !changing ? "email-moves-on" : "", fieldErrors.email ? "email-error" : ""].filter(Boolean).join(" ") || undefined}
              />
              {suffix && (
                <span id="email-domain" className="signin-domain" title={t("shell.signIn.domainHint")}>
                  {suffix}
                  <span className="visually-hidden"> {t("shell.signIn.domainHint")}</span>
                </span>
              )}
            </span>
            {(domain || knowsDomains) && !changing && (
              <span id="email-moves-on" className="muted signin-note">
                {t(domain ? "shell.signIn.domainMovesOn" : "shell.signIn.knownDomainMovesOn")}
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
                onChange={(e) => {
                  withdrawPasskeyRequest();
                  setPassword(e.target.value);
                }}
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
              {"key" in error
                ? error.pausedUntil !== undefined
                  ? t(error.key, { time: format.time(new Date(error.pausedUntil)), minutes: Math.max(1, Math.ceil((error.pausedUntil - Date.now()) / 60_000)) })
                  : t(error.key)
                : error.text}
              {"key" in error && error.deviceCode && (
                <p className="signin-device-code">
                  {t("shell.signIn.deviceCode")}{" "}
                  <strong dir="ltr" translate="no" data-device-code="">
                    {error.deviceCode}
                  </strong>
                </p>
              )}
              {failures >= failuresBeforeHint && !changing && !("key" in error && error.pausedUntil !== undefined) && (
                <p className="signin-pause-hint">{t("shell.signIn.repeatedFailures")}</p>
              )}
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
          <button type="submit" className="button primary signin-submit" disabled={busy}>
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
