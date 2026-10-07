// Blind screenshots: a reviewer must not be able to tell which product a screenshot shows from
// its branding or its file name.
//
// - Logos, product names, vendor links and the vendor's bot avatar are painted over with a
//   flat neutral box (Playwright's screenshot mask) in both products.
// - Signature colours are neutralised: the screenshot is rendered in greyscale.
// - Title and favicon are replaced with neutral ones before the shot.
// - File names are random; a key file kept apart from the screenshots maps them back.
import crypto from 'node:crypto';

export const MASK_COLOR = '#8a8a8a';

/** Applied only while the screenshot is taken (Playwright's `style` option). */
export const NEUTRAL_STYLE = `
  html { filter: grayscale(100%) !important; }
  *, *::before, *::after { caret-color: transparent !important; transition: none !important; animation: none !important; }
`;

/**
 * What counts as branding in each product. Selectors are painted over; words are matched
 * case-insensitively in visible text and the elements holding them are painted over.
 */
export const BRANDING = Object.freeze({
  odoo: {
    selectors: [
      // Placeholders that name the vendor are cleared before the shot (neutraliseDocument), not
      // painted over: a grey box over a filled-in field would hide its value and single it out.
      'img[src*="logo" i]', 'img[alt*="odoo" i]', 'a[href*="odoo.com"]', '[title*="odoo" i]',
      '.o_brand_promotion', '.o_web_client .o_brand', 'img[src*="odoobot" i]',
      // OdooBot's avatar (partner 2 in every Odoo database).
      'img[src*="/res.partner/2/"]',
    ],
    words: ['Odoo', 'OdooBot'],
    // The demo data's own names tell the products apart too (round 3): the reference's company
    // and its database badge. `identity` is matched anywhere in a text; `identityExact` only as a
    // whole text (a short code would otherwise hide ordinary words).
    identity: ['Demo Trading LLC'],
    identityExact: ['reference'],
    // Round 5: codes matched as a word anywhere in a text (an e-mail domain, "workspace alnoor"),
    // not only as a whole text.
    identityWords: ['demo-trading'],
  },
  ours: {
    selectors: ['[data-brand]', 'img[src*="logo" i]'],
    words: [],
    // Round 7: every company of the demo tenants (the shell's company switcher and the companies
    // list show them all), in English and Arabic.
    identity: ['Al Noor Trading LLC', 'Al Noor General Trading', 'Al Noor Industries', 'Al Noor Technical Services', 'Gulf Steel Fabrication',
      'شركة النور للتجارة', 'النور للتجارة العامة', 'مصانع النور', 'النور للخدمات الفنية', 'الخليج لتصنيع الصلب'],
    identityExact: ['alnoor'],
    // Round 7: the demo companies' and branches' codes, shown in the top bar ("ALN-DXB · DEIRA-HQ").
    identityWords: ['alnoor', 'gulfsteel', 'ALN-DXB', 'ALN-FZE', 'ALN-SHJ', 'ALN-AUH', 'GSF-SHJ', 'GSF-RAK', 'DEIRA-HQ', 'AQZ-WH', 'DIP-SR', 'JAFZA-WH',
      'DAFZ-OF', 'SHJ-FAC', 'SAIF-WH', 'AJM-WS', 'MUS-WS', 'AIN-OF', 'RAK-ST', 'FUJ-ST', 'SHJ-PLANT', 'HAMR-YD', 'GHAIL-PL'],
  },
});

const unionOf = key => [...new Set(Object.values(BRANDING).flatMap(b => b[key] || []))];

/**
 * What a product's screenshots paint over. Round 7: the demo data's names of every product, not
 * only the product's own: a name masked in one product's shots and left showing in the other's
 * (the task's own sign-in "...@demo-trading.example" showed in ours and was masked in the
 * reference's) told the reviewer which product a shot came from. So each product's shots mask
 * every product's company names and codes, and every vendor word; the vendor's own selectors stay
 * with that vendor's screens.
 */
export function brandingFor(product, extraWords = []) {
  const b = BRANDING[product];
  if (!b) throw new Error(`unknown product: ${product}`);
  return { selectors: [...b.selectors], words: [...new Set([...unionOf('words'), ...extraWords])], identity: unionOf('identity'),
    identityExact: unionOf('identityExact'), identityWords: unionOf('identityWords') };
}

const escapeRe = s => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/** A text holding one of the identity codes as a word ("workspace alnoor", "admin@alnoor.example"). */
export function identityWordPattern(words) {
  return new RegExp(`(^|[^\\p{L}\\p{N}_-])(${words.map(escapeRe).join('|')})(?=$|[^\\p{L}\\p{N}_-])`, 'iu');
}

/** Playwright locators for everything that must be painted over on `page`. */
export function maskLocators(page, branding) {
  const locs = branding.selectors.map(s => page.locator(s));
  if (branding.words.length) {
    const re = new RegExp(branding.words.map(escapeRe).join('|'), 'i');
    locs.push(page.getByText(re));
  }
  if (branding.identity?.length) locs.push(page.getByText(new RegExp(branding.identity.map(escapeRe).join('|'), 'i')));
  for (const w of branding.identityExact || []) locs.push(page.getByText(w, { exact: true }));
  if (branding.identityWords?.length) locs.push(page.getByText(identityWordPattern(branding.identityWords)));
  return locs;
}

/**
 * For each element (in the page), how many levels up stands the box that clips it: the nearest
 * ancestor that hides its overflow, when that ancestor is no taller than two of the element's
 * lines (a list cell, not a scrolling list or a bar). 0 when there is none.
 */
function clipDepths(els) {
  return els.map(el => {
    const r = el.getBoundingClientRect();
    if (!r.width || !r.height) return 0;
    let depth = 0;
    for (let a = el.parentElement; a && a !== document.body && a !== document.documentElement; a = a.parentElement) {
      depth++;
      const cs = getComputedStyle(a);
      if (cs.overflowX === 'visible' && cs.overflowY === 'visible') continue;
      const ar = a.getBoundingClientRect();
      return ar.height <= 2 * r.height + 1 ? depth : 0;
    }
    return 0;
  });
}

/**
 * What a screenshot paints over, aligned to the columns. Round 7 (p02 critic, round 4: list rows
 * looked misaligned around a masked company name): a name cut short by its list cell (an
 * ellipsis) has a box wider than the cell, and painting that box covered part of the next column,
 * while a short name left part of its cell showing. A name in a cell that clips it is painted over
 * by the cell's box instead: every masked cell is painted edge to edge, whatever the name's length
 * (which says nothing either).
 */
export async function maskTargets(page, branding, { perLocator = 300 } = {}) {
  const out = [];
  for (const loc of maskLocators(page, branding)) {
    const depths = await loc.evaluateAll(clipDepths).catch(() => null);
    if (!depths || !depths.some(d => d > 0) || depths.length > perLocator) { out.push(loc); continue; }
    depths.forEach((d, i) => out.push(d > 0 ? loc.nth(i).locator(`xpath=ancestor::*[${d}]`) : loc.nth(i)));
  }
  return out;
}

/**
 * Neutral title, no favicon, and no brand word in a visible hint (placeholder, tooltip, image or
 * accessible label): such a hint is emptied rather than painted over, so the field and its value
 * look like any other field.
 */
export async function neutraliseDocument(page, words = []) {
  await page.evaluate(brandWords => {
    document.title = 'Product';
    for (const l of document.querySelectorAll('link[rel~="icon"], link[rel="shortcut icon"], link[rel="apple-touch-icon"]')) l.remove();
    if (!brandWords.length) return;
    const re = new RegExp(brandWords.map(w => w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|'), 'i');
    for (const attr of ['placeholder', 'aria-label', 'alt']) {
      for (const el of document.querySelectorAll(`[${attr}]`)) if (re.test(el.getAttribute(attr))) el.setAttribute(attr, '');
    }
  }, words).catch(() => {});
}

/**
 * Captions for a blind page: the moments both products share keep their names (start, done);
 * the moments a driver chose inside its path become "moment 1", "moment 2" ..., because their
 * names describe one product's screens.
 */
export function neutralMoments(shots) {
  let n = 0;
  return shots.map(s => (s.moment === 'start' || s.moment === 'done' || s.moment === 'error' ? s.moment : `moment ${++n}`));
}

/** Random, product-neutral screenshot file name. */
export function blindName(ext = 'jpg') {
  return `${crypto.randomBytes(8).toString('hex')}.${ext}`;
}

/** True when a name gives away a product. Used by the harness self-tests. */
export function revealsProduct(name, extraWords = []) {
  return new RegExp(['odoo', 'ours', ...extraWords].map(escapeRe).join('|'), 'i').test(name);
}

/** Randomly assign the letters A and B to the two products of one side-by-side task. */
export function assignLetters(products, random = Math.random) {
  const order = random() < 0.5 ? products : [...products].reverse();
  return Object.fromEntries(order.map((p, i) => [p, String.fromCharCode(65 + i)]));
}

/**
 * The order two products run in for one task, at random, so the order of the runs (and anything
 * that follows it) says nothing about which product is which.
 */
export function productOrder(products, random = Math.random) {
  return products.length === 2 && random() < 0.5 ? [...products].reverse() : [...products];
}
