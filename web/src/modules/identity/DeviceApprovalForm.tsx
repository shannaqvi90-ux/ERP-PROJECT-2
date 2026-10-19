import { useState, type FormEvent, type Ref } from "react";
import { api, ApiError } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";
import { formKeys } from "../../kernel/forms/RecordForm";
import { Field } from "../../kernel/forms/fields";

/** Eight symbols of the device-code alphabet (no 0/O, 1/I/L), either case, hyphens and spaces anywhere. */
export const deviceCodePattern = /^[ -]*(?:[A-HJKMNP-Z2-9][ -]*){8}$/i;

/**
 * Lets one device sign in while sign-in from its shared network address (a gateway or proxy) is
 * paused after someone's failures (critic p03 round 12): the device's sign-in screen shows a device
 * code, and the person (My account, on a device where they are signed in) or an administrator (the
 * user's Sign-in history) enters it here. That browser alone may then sign in with its password.
 * Keyboard first: type the code, Enter (or Ctrl+Enter) lets it in.
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

  async function submit(event: FormEvent) {
    event.preventDefault();
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
    <form
      className="id-form id-device-approval"
      noValidate
      onSubmit={(e) => void submit(e)}
      onKeyDown={(e) => formKeys(e, () => e.currentTarget.requestSubmit(), () => undefined)}
    >
      <h2>{t("identity.device.title")}</h2>
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
            placeholder="K7QM-3XRA"
            value={code}
            onChange={(e) => setCode(e.target.value.toUpperCase())}
          />
        )}
      </Field>
      <button type="submit" className="button primary" disabled={busy}>
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
    </form>
  );
}
