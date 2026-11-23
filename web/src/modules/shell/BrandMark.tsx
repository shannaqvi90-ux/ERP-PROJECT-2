import type { Language } from "../../kernel/i18n";

/** The Bizora wordmark (docs/design/brand, used as it is): ink on light, white on dark and high
 * contrast (the theme tokens show one of the two; on a phone the square app icon takes their place), the Arabic wordmark on Arabic screens. */
export function BrandMark({ language }: { language: Language }) {
  const file = language === "ar" ? "bizora-arabic" : "bizora-wordmark";
  return (
    <>
      <img className="brand-mark ink" src={`/brand/${file}.svg`} alt="" />
      <img className="brand-mark white" src={`/brand/${file}-white.svg`} alt="" />
      <img className="brand-icon" src="/brand/bizora-app-icon.svg" alt="" />
    </>
  );
}
