import { Dialog } from "../../kernel/dialog";
import { createFormatter, numeralSystems } from "../../kernel/format";
import { languages, useI18n } from "../../kernel/i18n";
import { sessionUserName, type Session } from "../../kernel/session";
import { usePreferenceActions } from "./usePreferenceActions";

const sampleDate = new Date(Date.UTC(2026, 9, 3, 9, 30));

/**
 * The user's own preferences: interface language and the digits Arabic screens use. A choice
 * applies at once (no Save button) and is saved to the profile when the user's roles allow it.
 */
export function PreferencesDialog({ session, onClose }: { session: Session; onClose: () => void }) {
  const { t } = useI18n();
  const { language, numerals, changeLanguage, changeNumerals, saveState } = usePreferenceActions();

  return (
    <Dialog title={t("shell.prefs.title")} onClose={onClose} className="preferences">
      <p className="prefs-account">
        <span className="muted">{t("shell.prefs.account")} </span>
        <strong>{sessionUserName(session.user, language)}</strong>{" "}
        <span dir="ltr" className="muted">
          {session.user.email}
        </span>
      </p>
      <fieldset className="choice">
        <legend>{t("shell.prefs.language")}</legend>
        {languages.map((option) => (
          <label key={option} className="choice-option" lang={option}>
            <input type="radio" name="language" value={option} checked={language === option} onChange={() => changeLanguage(option)} />
            {t(`shell.language.native.${option}`)}
          </label>
        ))}
      </fieldset>
      <fieldset className="choice">
        <legend>{t("shell.prefs.digits")}</legend>
        {numeralSystems.map((option) => (
          <label key={option} className="choice-option">
            <input type="radio" name="numerals" value={option} checked={numerals === option} onChange={() => changeNumerals(option)} />
            {t(`shell.prefs.digits.${option}`)}
          </label>
        ))}
        <p className="muted prefs-sample">
          {(() => {
            const arabic = createFormatter("ar", numerals);
            return t("shell.prefs.sample", { number: arabic.decimal("1234567.89"), date: arabic.date(sampleDate) });
          })()}
        </p>
      </fieldset>
      {saveState && (
        <p className={saveState === "saved" ? "prefs-status" : "prefs-status warn"} role="status">
          {t(
            saveState === "saved"
              ? "shell.prefs.saved"
              : saveState === "device"
                ? "shell.prefs.savedDevice"
                : saveState === "offline"
                  ? "shell.prefs.offline"
                  : "shell.prefs.refused",
          )}
        </p>
      )}
      <div className="dialog-actions">
        <button type="button" className="button" onClick={onClose}>
          {t("shell.prefs.close")}
        </button>
      </div>
    </Dialog>
  );
}
