import { useId, useState, type Ref } from "react";
import { api, ApiError } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";
import { Field } from "../../kernel/forms/fields";

/** Eight symbols of the device-code alphabet (no 0/O, 1/I/L), either case, hyphens and spaces anywhere. */
export const deviceCodePattern = /^[ -]*(?:[A-HJKMNP-Z2-9][ -]*){8}$/i;

/**
 * Lets one device sign in while sign-in from its shared network address (a gateway or proxy) is
 * paused after someone's failures (critic p03 round 12): the device's sign-in screen shows a device
 * code, and the person (My account, on a device where they are signed in) or an administrator (the
 * user's Sign-in history) enters it here. That browser alone may then sign in with its password.
 * Keyboard first: type the code, Enter (or Ctrl+Enter) lets it in. A group, not a form of its own:
 * it sits inside the user record's form, and forms may not nest.
 */
export function DeviceApprovalForm({
  endpoint,
  hintKey,
  inputRef,
  onApproved,
}: {
  endpoint: string;
  hintKey: string;
  inputRef?: Ref<HTMLInputElement>;
  onApproved?: (until: string) => void;
}) {
  const { t, formatDateTime } = useI18n();
  const [code, setCode] = useState("");
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const headingId = useId();

  async function submit() {
    if (busy) return;
    setDone(null);
    setError(null);
    if (!deviceCodePattern.test(code)) {
      setFieldError(t("identity.device.codeInvalid"));
      return;
    }
    setFieldError(null);
    setBusy(true);
    try {
      const answer = await api<{ until: string }>("POST", endpoint, { code });
      setCode("");
      setDone(t("identity.device.approved", { time: formatDateTime(answer.until) }));
      onApproved?.(answer.until);
    } catch (e) {
      const refused = e instanceof ApiError ? e.fieldErrors.code?.[0] : undefined;
      if (refused) setFieldError(refused.message);
      else setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div
      className="id-form id-device-approval"
      role="group"
      aria-labelledby={headingId}
      onKeyDown={(e) => {
        // Enter in the code field lets the device in here, never the record form around it.
        if (e.key === "Enter" && !e.ctrlKey && !e.metaKey && !e.altKey && !e.shiftKey && (e.target as HTMLElement).tagName === "INPUT") {
          e.preventDefault();
          e.stopPropagation();
          void submit();
          return;
        }
        // The save keys (Ctrl+Enter, Ctrl+S by key position) let it in too, not save the record.
        if ((e.ctrlKey || e.metaKey) && !e.altKey && (e.key === "Enter" || e.code === "KeyS" || e.key.toLowerCase() === "s")) {
          e.preventDefault();
          e.stopPropagation();
          void submit();
        }
      }}
    >
      <h2 id={headingId}>{t("identity.device.title")}</h2>
      <p className="muted">{t(hintKey)}</p>
      <Field name="deviceCode" label={t("identity.device.code")} errors={fieldError ? [fieldError] : []}>
        {(a) => (
          <input
            {...a}
            ref={inputRef}
            name="deviceCode"
            dir="ltr"
            autoComplete="off"
            autoCapitalize="characters"
            spellCheck={false}
            maxLength={20}
            placeholder={t("identity.device.placeholder")}
            value={code}
            onChange={(e) => setCode(e.target.value.toUpperCase())}
          />
        )}
      </Field>
      <button type="button" className="button primary" disabled={busy} onClick={() => void submit()}>
        {t("identity.device.letIn")}
      </button>
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
    </div>
  );
}
