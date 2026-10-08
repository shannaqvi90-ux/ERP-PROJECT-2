import { useCallback, useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { api, ApiError } from "../../kernel/api";
import { Dialog } from "../../kernel/dialog";
import { useI18n } from "../../kernel/i18n";
import { cancelledByPerson, createPasskey, deviceName, passkeyOffered, passkeysSupported, setPasskeyOffered, type PasskeyCreationOptions } from "../../kernel/passkeys";
import { useSession } from "../../kernel/session";

export type Passkey = { id: string; name: string; createdAt: string; lastUsedAt: string | null; backedUp: boolean; version: number };
type MyPasskeys = { items: Passkey[]; canAddUntil: string | null; limit: number };

/**
 * The signed-in user's passkeys on My account: add one (this device or a password manager makes
 * it; the workspace keeps only its public key), rename, remove, and this device's choice to be
 * asked for the passkey as soon as the sign-in screen opens. Adding needs a sign-in within the last
 * few minutes; otherwise the section asks for the password first (a fresh sign-in, as changing the
 * password does).
 */
export function PasskeysSection() {
  const { t, formatDateTime } = useI18n();
  const { state, refresh } = useSession();
  const [data, setData] = useState<MyPasskeys | null>(null);
  const [name, setName] = useState(() => deviceName());
  const [confirming, setConfirming] = useState(false);
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [nameError, setNameError] = useState<string | null>(null);
  const [renaming, setRenaming] = useState<{ id: string; name: string; version: number } | null>(null);
  const [removing, setRemoving] = useState<Passkey | null>(null);
  const [offered, setOffered] = useState(passkeyOffered);
  const passwordRef = useRef<HTMLInputElement>(null);
  const renameRef = useRef<HTMLInputElement>(null);
  const supported = passkeysSupported();

  const load = useCallback(async () => {
    try {
      setData(await api<MyPasskeys>("GET", "/api/identity/me/passkeys"));
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (confirming) passwordRef.current?.focus();
  }, [confirming]);

  useEffect(() => {
    if (renaming) renameRef.current?.select();
  }, [renaming?.id]);

  if (state.status !== "signedIn") return null;
  const { user, tenant } = state.session;

  async function add(event?: FormEvent) {
    event?.preventDefault();
    setNotice(null);
    setError(null);
    const label = name.trim() || t("identity.passkeys.defaultName");
    if (label.length > 100) {
      setNameError(t("identity.passkeys.nameTooLong"));
      return;
    }
    setNameError(null);
    setBusy(true);
    try {
      const options = await api<PasskeyCreationOptions>("POST", "/api/identity/me/passkeys/options");
      const answer = await createPasskey(options);
      await api("POST", "/api/identity/me/passkeys", { name: label, ...answer });
      setPasskeyOffered(true);
      setOffered(true);
      setNotice(t("identity.passkeys.added", { name: label }));
      await load();
    } catch (e) {
      if (e instanceof ApiError && e.code === "auth.recentSignInRequired") {
        setConfirming(true);
        setNotice(t("identity.passkeys.confirmFirst"));
      } else if (cancelledByPerson(e)) {
        setNotice(t("identity.passkeys.notMade"));
      } else if (e instanceof DOMException) {
        setError(e.name === "InvalidStateError" ? t("identity.passkeys.alreadyOnDevice") : t("identity.passkeys.deviceFailed"));
      } else {
        setError(e instanceof Error ? e.message : String(e));
      }
    } finally {
      setBusy(false);
    }
  }

  /** Signs in again with the password (a fresh session may add passkeys), then adds the passkey. */
  async function confirm(event: FormEvent) {
    event.preventDefault();
    if (!password) {
      setError(t("identity.me.currentRequired"));
      passwordRef.current?.focus();
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await api("POST", "/api/auth/sign-in", { email: user.email, password, workspace: tenant.code });
      setPassword("");
      setConfirming(false);
      await refresh();
    } catch (e) {
      setError(e instanceof ApiError && e.status === 401 ? t("identity.me.currentWrong") : e instanceof Error ? e.message : String(e));
      setBusy(false);
      passwordRef.current?.focus();
      return;
    }
    setBusy(false);
    await add();
  }

  async function saveName(event: FormEvent) {
    event.preventDefault();
    if (!renaming) return;
    const label = renaming.name.trim();
    if (!label || label.length > 100) {
      setNameError(t(label ? "identity.passkeys.nameTooLong" : "identity.passkeys.nameRequired"));
      renameRef.current?.focus();
      return;
    }
    setNameError(null);
    try {
      await api("PUT", `/api/identity/me/passkeys/${renaming.id}`, { name: label, version: renaming.version });
      setRenaming(null);
      setNotice(t("identity.passkeys.renamed"));
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  async function remove(passkey: Passkey) {
    setRemoving(null);
    try {
      await api("DELETE", `/api/identity/me/passkeys/${passkey.id}`);
      setNotice(t("identity.passkeys.removed", { name: passkey.name }));
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  function onRenameKey(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Escape") {
      event.preventDefault();
      setRenaming(null);
      setNameError(null);
    }
  }

  const items = data?.items ?? [];
  const full = data !== null && items.length >= data.limit;

  return (
    <section className="id-passkeys" aria-labelledby="passkeys-title">
      <h2 id="passkeys-title">{t("identity.passkeys.title")}</h2>
      <p className="muted">{t("identity.passkeys.lead")}</p>
      {notice && (
        <div className="id-notice" role="status">
          {notice}
        </div>
      )}
      {error && (
        <div className="alert" role="alert">
          {error}
        </div>
      )}
      {items.length === 0 ? (
        <p>{t("identity.passkeys.none")}</p>
      ) : (
        <table className="grid id-passkey-table">
          <thead>
            <tr>
              <th scope="col">{t("identity.passkeys.name")}</th>
              <th scope="col">{t("identity.passkeys.addedAt")}</th>
              <th scope="col">{t("identity.passkeys.lastUsed")}</th>
              <th scope="col">{t("identity.passkeys.synced")}</th>
              <th scope="col">
                <span className="visually-hidden">{t("identity.passkeys.actions")}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {items.map((p) => (
              <tr key={p.id}>
                <td dir="auto">
                  {renaming?.id === p.id ? (
                    <form className="id-inline" onSubmit={(e) => void saveName(e)}>
                      <input
                        ref={renameRef}
                        aria-label={t("identity.passkeys.newName")}
                        aria-invalid={nameError ? true : undefined}
                        aria-describedby={nameError ? "passkey-rename-error" : undefined}
                        value={renaming.name}
                        maxLength={100}
                        onChange={(e) => setRenaming({ ...renaming, name: e.target.value })}
                        onKeyDown={onRenameKey}
                      />
                      <button type="submit" className="button">
                        {t("identity.passkeys.save")}
                      </button>
                      <button type="button" className="button" onClick={() => setRenaming(null)}>
                        {t("identity.passkeys.cancel")}
                      </button>
                      {nameError && (
                        <span id="passkey-rename-error" className="field-error">
                          {nameError}
                        </span>
                      )}
                    </form>
                  ) : (
                    p.name
                  )}
                </td>
                <td>{formatDateTime(p.createdAt)}</td>
                <td>{p.lastUsedAt ? formatDateTime(p.lastUsedAt) : t("identity.users.never")}</td>
                <td>{p.backedUp ? t("identity.passkeys.yes") : t("identity.passkeys.no")}</td>
                <td className="id-actions">
                  {renaming?.id !== p.id && (
                    <>
                      <button type="button" className="button" aria-label={t("identity.passkeys.renameNamed", { name: p.name })} onClick={() => setRenaming({ id: p.id, name: p.name, version: p.version })}>
                        {t("identity.passkeys.rename")}
                      </button>
                      <button type="button" className="button danger" aria-label={t("identity.passkeys.removeNamed", { name: p.name })} onClick={() => setRemoving(p)}>
                        {t("identity.passkeys.remove")}
                      </button>
                    </>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {!supported ? (
        <p className="muted">{t("identity.passkeys.unsupported")}</p>
      ) : confirming ? (
        <form className="id-form" noValidate onSubmit={(e) => void confirm(e)}>
          <label className="field">
            <span className="field-label">{t("identity.passkeys.confirmPassword")}</span>
            <input ref={passwordRef} name="confirm-password" type="password" dir="ltr" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} />
          </label>
          <div className="id-inline">
            <button type="submit" className="button primary" disabled={busy}>
              {t("identity.passkeys.confirmAndAdd")}
            </button>
            <button type="button" className="button" onClick={() => setConfirming(false)}>
              {t("identity.passkeys.cancel")}
            </button>
          </div>
        </form>
      ) : (
        <form className="id-form" noValidate onSubmit={(e) => void add(e)}>
          <label className="field">
            <span className="field-label">{t("identity.passkeys.nameForNew")}</span>
            <input
              name="passkey-name"
              dir="auto"
              maxLength={100}
              value={name}
              placeholder={t("identity.passkeys.defaultName")}
              aria-invalid={nameError ? true : undefined}
              aria-describedby={nameError ? "passkey-name-error" : undefined}
              onChange={(e) => setName(e.target.value)}
            />
            {nameError && !renaming && (
              <span id="passkey-name-error" className="field-error">
                {nameError}
              </span>
            )}
          </label>
          <button type="submit" className="button primary" disabled={busy || full}>
            {t("identity.passkeys.add")}
          </button>
          {full && <p className="muted">{t("identity.passkeys.full", { limit: data?.limit ?? 0 })}</p>}
        </form>
      )}
      {supported && (
        <label className="id-check">
          <input
            type="checkbox"
            checked={offered}
            onChange={(e) => {
              setPasskeyOffered(e.target.checked);
              setOffered(e.target.checked);
            }}
          />
          {t("identity.passkeys.offerOnThisDevice")}
        </label>
      )}
      {removing && (
        <Dialog title={t("identity.passkeys.removeTitle", { name: removing.name })} onClose={() => setRemoving(null)}>
          <p>{t("identity.passkeys.removeBody")}</p>
          <div className="dialog-actions">
            <button type="button" className="button danger" onClick={() => void remove(removing)}>
              {t("identity.passkeys.remove")}
            </button>
            <button type="button" className="button" onClick={() => setRemoving(null)}>
              {t("identity.passkeys.cancel")}
            </button>
          </div>
        </Dialog>
      )}
    </section>
  );
}
