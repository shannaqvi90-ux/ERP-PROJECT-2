/**
 * The countries a company or branch address may name, by ISO 3166-1 alpha-2 code: the regions the
 * server accepts (TenancyValidation.Countries, the regions of the runtime's ICU cultures, as of
 * .NET 10). Names come from the browser's own CLDR data (Intl.DisplayNames) in the screen's
 * language, so no country name is kept here.
 */
export const COUNTRY_CODES: readonly string[] = (
  "AD AE AF AG AI AL AM AO AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BW BY BZ " +
  "CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DG DJ DK DM DO DZ EA EC EE EG EH ER ES ET FI FJ " +
  "FK FM FO FR GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HN HR HT HU IC ID IE IL IM IN IO IQ " +
  "IR IS IT JE JM JO JP KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH " +
  "MK ML MM MN MO MP MQ MR MS MT MU MV MW MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL " +
  "PM PN PR PS PT PW PY QA RE RO RS RU RW SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD " +
  "TG TH TJ TK TL TM TN TO TR TT TV TW TZ UA UG UM US UY UZ VA VC VE VG VI VN VU WF WS XK YE YT ZA ZM ZW"
).split(" ");

/** The UAE and its Gulf neighbours first (where most of this market's addresses are), then the rest by name. */
const FIRST = ["AE", "SA", "OM", "QA", "BH", "KW"];

/** The countries as choices in `language`: [code, name], the UAE and the Gulf first, then by name. */
export function countryChoices(language: string, current?: string): { value: string; label: string }[] {
  let names: Intl.DisplayNames | null = null;
  try {
    names = new Intl.DisplayNames([language], { type: "region" });
  } catch {
    names = null;
  }
  const name = (code: string) => {
    try {
      return names?.of(code) ?? code;
    } catch {
      return code;
    }
  };
  const codes = current && !COUNTRY_CODES.includes(current) ? [...COUNTRY_CODES, current] : COUNTRY_CODES;
  const collator = new Intl.Collator(language);
  const rest = codes.filter((c) => !FIRST.includes(c)).map((c) => ({ value: c, label: name(c) })).sort((a, b) => collator.compare(a.label, b.label));
  return [...FIRST.map((c) => ({ value: c, label: name(c) })), ...rest];
}
