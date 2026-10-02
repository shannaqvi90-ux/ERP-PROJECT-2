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
      'img[src*="logo" i]', 'img[alt*="odoo" i]', 'a[href*="odoo.com"]', '[title*="odoo" i]', '[placeholder*="odoo" i]',
      '.o_brand_promotion', '.o_web_client .o_brand', 'img[src*="odoobot" i]',
      // OdooBot's avatar (partner 2 in every Odoo database).
      'img[src*="/res.partner/2/"]',
    ],
    words: ['Odoo', 'OdooBot'],
  },
  ours: {
    selectors: ['[data-brand]', 'img[src*="logo" i]'],
    words: [],
  },
});

export function brandingFor(product, extraWords = []) {
  const b = BRANDING[product];
  if (!b) throw new Error(`unknown product: ${product}`);
  return { selectors: [...b.selectors], words: [...b.words, ...extraWords] };
}

const escapeRe = s => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/** Playwright locators for everything that must be painted over on `page`. */
export function maskLocators(page, branding) {
  const locs = branding.selectors.map(s => page.locator(s));
  if (branding.words.length) {
    const re = new RegExp(branding.words.map(escapeRe).join('|'), 'i');
    locs.push(page.getByText(re));
  }
  return locs;
}

/** Neutral title and no favicon, so a shot of the whole window would not give the product away. */
export async function neutraliseDocument(page) {
  await page.evaluate(() => {
    document.title = 'Product';
    for (const l of document.querySelectorAll('link[rel~="icon"], link[rel="shortcut icon"], link[rel="apple-touch-icon"]')) l.remove();
  }).catch(() => {});
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
