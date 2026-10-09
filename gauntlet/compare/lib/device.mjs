// The person's passkey device, for tasks that declare one (`device: 'passkey'`).
//
// A passkey sign-in has an action no key or click measures: the person confirms on the device
// (a fingerprint, a face, the device PIN) when the browser asks. Both products may use it, so the
// harness gives every browser of such a task the same device and counts that confirmation as a
// step of its own (op.confirmOnDevice, lib/operator.mjs), never as free:
//
//   - Each page gets a virtual WebAuthn authenticator (Chromium's, through CDP): user-verifying,
//     holding discoverable credentials, answering at once once asked.
//   - A shim installed in every document before the product's own script holds each request the
//     product makes to navigator.credentials (get or create, with a publicKey) until the person
//     confirms. In set-up (the free phase) the person confirms at once: set-up may add a passkey
//     the way a person does, on the product's own screens. From the start screen on, a request
//     waits for op.confirmOnDevice: a product that asks as its sign-in screen opens is asked on the
//     start screen and answered only by the counted step.
//   - What the device holds (the passkeys set-up made, private keys included) is carried from the
//     set-up browser to the fresh browser of the start (lib/runner.mjs), as a person's device is
//     the same device after they closed the browser.
//
// The product cannot answer for the person: the shim's original functions are not reachable from
// the page, and the binding it waits on only reports a request; the release comes from the runner.
import { currentPhase } from './guard.mjs';

const BINDING = '__compareDeviceAsk';

/** Page script (installed by the harness, not a driver): hold every passkey request for the person. */
function shim(binding) {
  const proto = globalThis.CredentialsContainer?.prototype;
  const ask = globalThis[binding];
  if (!proto || typeof proto.get !== 'function' || typeof ask !== 'function') return;
  // On the prototype, so no reference to an unheld function is left in the page (the container's
  // own methods are the held ones too).
  const original = { get: proto.get, create: proto.create };
  for (const kind of ['get', 'create']) {
    const held = async function (options) {
      if (options && options.publicKey) await ask(kind);
      return original[kind].call(this, options);
    };
    Object.defineProperty(proto, kind, { value: held, writable: false, configurable: false });
  }
}

export class Device {
  #credentials = new Map(); // credential id -> CDP credential (private key included)
  #pages = new Map(); // page -> { cdp, authenticatorId }
  #waiting = []; // { page, kind, release }
  #listeners = [];
  #contexts = new WeakSet();

  /** Give a context the shim and the binding; call before its first page opens. */
  async attachContext(context) {
    if (this.#contexts.has(context)) return;
    this.#contexts.add(context);
    await context.exposeBinding(BINDING, (source, kind) => this.#ask(source.page, kind));
    await context.addInitScript(shim, BINDING);
  }

  /** Give a page its authenticator, holding what the device holds; call before it loads the product. */
  async attachPage(page) {
    if (this.#pages.has(page)) return;
    const cdp = await page.context().newCDPSession(page);
    await cdp.send('WebAuthn.enable', { enableUI: false });
    const { authenticatorId } = await cdp.send('WebAuthn.addVirtualAuthenticator', {
      options: { protocol: 'ctap2', ctap2Version: 'ctap2_1', transport: 'internal', hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true },
    });
    for (const credential of this.#credentials.values()) await cdp.send('WebAuthn.addCredential', { authenticatorId, credential });
    this.#pages.set(page, { cdp, authenticatorId });
    page.once('close', () => this.#pages.delete(page));
  }

  /** Keep what every open page's authenticator holds (before the runner closes the set-up browser). */
  async collect() {
    for (const [page, { cdp, authenticatorId }] of this.#pages) {
      try {
        const { credentials } = await cdp.send('WebAuthn.getCredentials', { authenticatorId });
        for (const c of credentials) {
          const known = this.#credentials.get(c.credentialId);
          if (!known || (c.signCount ?? 0) >= (known.signCount ?? 0)) this.#credentials.set(c.credentialId, c);
        }
      } catch { /* the page closed */ }
      if (page.isClosed()) this.#pages.delete(page);
    }
    return this.#credentials.size;
  }

  /** Passkeys the device holds. */
  get held() { return this.#credentials.size; }

  /** Requests of the product waiting for the person on `page` (any page when omitted). */
  waiting(page = null) { return this.#waiting.filter(w => !page || w.page === page).length; }

  #ask(page, kind) {
    // Set-up: the person confirms at once (they are adding a passkey, or signing in to prepare).
    if (currentPhase() === 'free') return Promise.resolve(true);
    return new Promise(release => {
      this.#waiting.push({ page, kind, release });
      for (const l of this.#listeners.splice(0)) l();
    });
  }

  /** Wait until the product asks on `page` (at most `timeout` ms); false when it never did. */
  async whenAsked(page, timeout) {
    const deadline = Date.now() + timeout;
    while (!this.waiting(page)) {
      if (page.isClosed() || Date.now() > deadline) return false;
      await new Promise(resolve => { const t = setTimeout(resolve, 50); this.#listeners.push(() => { clearTimeout(t); resolve(); }); });
    }
    return true;
  }

  /** The person confirms the oldest request on `page`: the authenticator answers it. */
  confirm(page) {
    const at = this.#waiting.findIndex(w => w.page === page);
    if (at < 0) return null;
    const [w] = this.#waiting.splice(at, 1);
    w.release(true);
    return w.kind;
  }

  /** Forget the pages (the browser closes); requests still waiting stay unanswered. */
  close() {
    this.#pages.clear();
    this.#waiting = [];
  }
}
