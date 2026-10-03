// The start of the measured part belongs to the runner, not to the driver.
//
// A driver's set-up and sign-in run outside the clock, so whatever they leave behind must not
// help the measured part (round 3: a script installed during sign-in finished the task, and a
// sign-in that opened the users list and typed the name left one step to measure). After sign-in
// the runner therefore
//   1. keeps only the session: the cookies of the browser context (and, for a signed-out start, its
//      local storage, where a returning browser remembers the sign-in), and closes that context
//      with every page, script, route, exposed function and listener in it;
//   2. opens a fresh context with that session and loads the task's start screen itself: the
//      product's own address for 'home' and 'sign-in', or, for a task that starts on a screen the
//      driver opened ('record', 'list'), that screen's address, reloaded, with no query or fragment;
//   3. waits until the product is ready and quiet, and checks the start state: on 'home' and 'list'
//      no field holds typed text, on 'sign-in' no password is filled in and the only remembered text
//      is the task's own sign-in. The start state is recorded in the result.
import { PRODUCTS } from './config.mjs';

/** Where each kind of start lives. 'record' and 'list' are screens the driver's sign-in opened. */
export const START_KINDS = Object.freeze(['home', 'sign-in', 'record', 'list', 'api']);

/** The address the runner loads for a start kind the product defines. */
export function startUrl(product, kind) {
  const p = PRODUCTS[product.id] || product;
  if (kind === 'home') return product.baseUrl + (p.homePath ?? '/');
  if (kind === 'sign-in') return product.baseUrl + (typeof p.signInPath === 'function' ? p.signInPath(product) : p.signInPath ?? '/');
  return null;
}

/**
 * The address of a driver-opened start screen ('record', 'list'): the product's own origin, no
 * query and no fragment (state carried in the address would be typed before the clock).
 */
export function screenUrlProblem(product, url) {
  let u;
  try { u = new URL(url); } catch { return `the start screen's address ${url} is not an address`; }
  if (u.origin !== new URL(product.baseUrl).origin) return `the start screen ${u.origin}${u.pathname} is not on the product (${product.baseUrl})`;
  if (u.search) return `the start screen's address carries a query (${u.search}); a start screen is reloaded from its path alone`;
  if (u.hash && u.hash !== '#') return `the start screen's address carries a fragment (${u.hash}); a start screen is reloaded from its path alone`;
  return null;
}

/**
 * Page conditions: the product has rendered the start screen and shows no loading indicator.
 * `signedIn` for every start but 'sign-in' (a lost session would show the sign-in screen), `signIn`
 * for the sign-in screen.
 */
export const READY = Object.freeze({
  odoo: {
    signedIn: () => !document.querySelector('.o_loading_indicator, .o_blockUI') && !!document.querySelector('.o_main_navbar button.o_user_menu') &&
      !!document.querySelector('.o_action_manager .o_view_controller, .o_action_manager .o_action'),
    signIn: () => !!document.querySelector('form.oe_login_form input[name="login"], form[action*="/web/login"] input[name="login"]'),
  },
  ours: {
    signedIn: () => document.readyState === 'complete' && !!document.querySelector('nav[aria-label="Main navigation"], nav.navpane'),
    signIn: () => document.readyState === 'complete' && !!document.querySelector('input[name="email"]'),
  },
  default: { signedIn: () => document.readyState === 'complete', signIn: () => document.readyState === 'complete' },
});

/** The ready condition for a product and start kind. */
export function readyCondition(readyKind, kind) {
  const r = READY[readyKind] || READY.default;
  return kind === 'sign-in' ? r.signIn : r.signedIn;
}

/** Page function (run by the harness, not a driver): what the start screen shows. */
export function snapshotStartState() {
  const TEXTLESS = ['hidden', 'checkbox', 'radio', 'button', 'submit', 'reset', 'image', 'file', 'range', 'color'];
  const visible = el => !!(el.offsetWidth || el.offsetHeight || el.getClientRects().length) && getComputedStyle(el).visibility !== 'hidden';
  const textual = el => (el.tagName === 'INPUT' && !TEXTLESS.includes((el.type || 'text').toLowerCase())) || el.tagName === 'TEXTAREA' || el.isContentEditable;
  const deep = root => {
    const out = [];
    for (const el of root.querySelectorAll('*')) { out.push(el); if (el.shadowRoot) out.push(...deep(el.shadowRoot)); }
    return out;
  };
  const all = deep(document);
  const value = el => (el.isContentEditable ? el.innerText : el.value) || '';
  const describe = el => ({
    tag: el.tagName.toLowerCase(),
    type: el.type || null,
    name: el.getAttribute('name') || el.id || el.getAttribute('aria-label') || el.getAttribute('placeholder') || null,
    password: (el.type || '').toLowerCase() === 'password',
    value_length: value(el).length,
    value: (el.type || '').toLowerCase() === 'password' ? null : value(el).slice(0, 120),
  });
  // Editable regions inside a contenteditable root count once, as their root.
  const fields = all.filter(el => textual(el) && visible(el) && !(el.isContentEditable && el.parentElement?.isContentEditable));
  let focused = document.activeElement;
  while (focused?.shadowRoot?.activeElement) focused = focused.shadowRoot.activeElement;
  return {
    path: location.pathname,
    query: location.search,
    fragment: location.hash,
    fields: fields.length,
    filled: fields.filter(el => value(el).trim().length > 0).map(describe),
    focused: focused && focused !== document.body ? describe(focused) : null,
  };
}

/** Problems with a start state for the task's start kind (empty when it is a fair start). */
export function startStateProblems(task, kind, state) {
  const problems = [];
  if (kind === 'home' || kind === 'list') {
    for (const f of state.filled) problems.push(`the start screen already holds typed text in ${f.tag}${f.name ? ` "${f.name}"` : ''} (${f.value_length} characters)`);
  } else if (kind === 'sign-in') {
    const login = String(task.input?.user ?? '').toLowerCase();
    for (const f of state.filled) {
      if (f.password) problems.push(`the sign-in screen starts with a password filled in (${f.name || f.tag})`);
      else if (!login || String(f.value).trim().toLowerCase() !== login) problems.push(`the sign-in screen starts with text that is not the task's remembered sign-in in ${f.name || f.tag}`);
    }
  }
  return problems;
}
