// Minimal software WebAuthn authenticator (ES256, "none" attestation) for the critic's attacks.
import crypto from "node:crypto";
const b64u = (b) => Buffer.from(b).toString("base64url");
function head(major, n) {
  if (n < 24) return Buffer.from([(major << 5) | n]);
  if (n < 256) return Buffer.from([(major << 5) | 24, n]);
  if (n < 65536) { const b = Buffer.alloc(3); b[0] = (major << 5) | 25; b.writeUInt16BE(n, 1); return b; }
  const b = Buffer.alloc(5); b[0] = (major << 5) | 26; b.writeUInt32BE(n, 1); return b;
}
export function cbor(v) {
  if (typeof v === "number") return v >= 0 ? head(0, v) : head(1, -1 - v);
  if (typeof v === "string") { const s = Buffer.from(v, "utf8"); return Buffer.concat([head(3, s.length), s]); }
  if (Buffer.isBuffer(v)) return Buffer.concat([head(2, v.length), v]);
  if (v instanceof Map) { const parts = [head(5, v.size)]; for (const [k, x] of v) parts.push(cbor(k), cbor(x)); return Buffer.concat(parts); }
  throw new Error("cbor: unsupported");
}
export class SoftPasskey {
  constructor(origin, rpId) {
    this.origin = origin; this.rpId = rpId;
    const { privateKey, publicKey } = crypto.generateKeyPairSync("ec", { namedCurve: "P-256" });
    this.priv = privateKey; this.jwk = publicKey.export({ format: "jwk" });
    this.credId = crypto.randomBytes(32); this.counter = 0; this.userHandle = null;
  }
  rpHash() { return crypto.createHash("sha256").update(this.rpId).digest(); }
  registration(options) {
    this.userHandle = options.user.id;
    const cose = cbor(new Map([[1, 2], [3, -7], [-1, 1], [-2, Buffer.from(this.jwk.x, "base64url")], [-3, Buffer.from(this.jwk.y, "base64url")]]));
    const len = Buffer.alloc(2); len.writeUInt16BE(this.credId.length);
    const cnt = Buffer.alloc(4); cnt.writeUInt32BE(this.counter);
    const authData = Buffer.concat([this.rpHash(), Buffer.from([0x45]), cnt, Buffer.alloc(16), len, this.credId, cose]);
    const att = cbor(new Map([["fmt", "none"], ["attStmt", new Map()], ["authData", authData]]));
    const cd = Buffer.from(JSON.stringify({ type: "webauthn.create", challenge: options.challenge, origin: this.origin, crossOrigin: false }));
    return { clientDataJson: b64u(cd), attestationObject: b64u(att), transports: ["internal"] };
  }
  assertion(challenge, { userHandle, credentialId } = {}) {
    this.counter++;
    const cnt = Buffer.alloc(4); cnt.writeUInt32BE(this.counter);
    const authData = Buffer.concat([this.rpHash(), Buffer.from([0x05]), cnt]);
    const cd = Buffer.from(JSON.stringify({ type: "webauthn.get", challenge, origin: this.origin, crossOrigin: false }));
    const sig = crypto.sign("sha256", Buffer.concat([authData, crypto.createHash("sha256").update(cd).digest()]), this.priv);
    return { credentialId: credentialId ?? b64u(this.credId), clientDataJson: b64u(cd), authenticatorData: b64u(authData), signature: b64u(sig), userHandle: userHandle ?? this.userHandle };
  }
}
