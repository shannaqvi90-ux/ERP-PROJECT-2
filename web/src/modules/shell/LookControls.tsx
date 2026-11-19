import { Icon, type IconName } from "../../kernel/icons";
import { useI18n } from "../../kernel/i18n";
import { densities, themeChoices, useLook, type ThemeChoice } from "../../kernel/theme";

const themeIcon: Record<ThemeChoice, IconName> = { system: "screen", light: "sun", dark: "moon", hc: "contrast" };

/**
 * The density and theme switches of the top bar: compact or comfortable rows, light, dark or high
 * contrast. Pressing the chosen theme again returns to "follow the system". Both are remembered on
 * this device (kernel/theme.ts); the same choices are in the preferences dialog and the palette.
 */
export function LookControls({ only }: { only?: "theme" | "density" }) {
  const { t } = useI18n();
  const { choice, theme, density, setTheme, setDensity } = useLook();
  return (
    <div className="look-controls" role="group" aria-label={t("shell.look.group")}>
      {only !== "theme" && (
      <div className="segmented" role="group" aria-label={t("shell.look.density")}>
        {densities.map((option) => (
          <button key={option} type="button" className="segment" aria-pressed={density === option} onClick={() => setDensity(option)}>
            {t(`shell.look.density.${option}`)}
          </button>
        ))}
      </div>
      )}
      {only !== "density" && (
      <div className="segmented" role="group" aria-label={t("shell.look.theme")}>
        {themeChoices
          .filter((option) => option !== "system")
          .map((option) => (
            <button
              key={option}
              type="button"
              className="segment icon-segment"
              aria-pressed={theme === option}
              aria-label={t(`shell.look.theme.${option}`)}
              title={t(`shell.look.theme.${option}`)}
              onClick={() => setTheme(choice === option ? "system" : option)}
            >
              <Icon name={themeIcon[option]} />
            </button>
          ))}
      </div>
      )}
    </div>
  );
}

export function LookChoices() {
  const { t } = useI18n();
  const { choice, density, setTheme, setDensity } = useLook();
  return (
    <>
      <fieldset className="choice">
        <legend>{t("shell.look.theme")}</legend>
        {themeChoices.map((option) => (
          <label key={option} className="choice-option">
            <input type="radio" name="theme" value={option} checked={choice === option} onChange={() => setTheme(option)} />
            {t(`shell.look.theme.${option}`)}
          </label>
        ))}
      </fieldset>
      <fieldset className="choice">
        <legend>{t("shell.look.density")}</legend>
        {densities.map((option) => (
          <label key={option} className="choice-option">
            <input type="radio" name="density" value={option} checked={density === option} onChange={() => setDensity(option)} />
            {t(`shell.look.density.${option}`)}
          </label>
        ))}
      </fieldset>
    </>
  );
}
