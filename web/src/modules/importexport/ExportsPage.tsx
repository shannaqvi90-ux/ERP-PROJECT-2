import { useCallback, useEffect, useState } from "react";
import { api, ApiError, sessionEndedEvent } from "../../kernel/api";
import { requestLanguage, useI18n } from "../../kernel/i18n";
import { useSession } from "../../kernel/session";
import { useShortcut } from "../../kernel/shortcuts";
import "./importexport.css";
import type { TenantExport } from "./types";

const pollMilliseconds = 2000;

/** Saves the answer of a download as a file (the download is a POST: it counts on the export's record and so stays in the audit trail). */
async function download(id: string): Promise<void> {
  const response = await fetch(`/api/importexport/tenant-exports/${id}/download`, {
    method: "POST",
    headers: { "X-Erp-Request": "1", Accept: "application/zip", "Accept-Language": requestLanguage() },
    credentials: "same-origin",
  });
  if (!response.ok) {
    if (response.status === 401) window.dispatchEvent(new Event(sessionEndedEvent));
    let body: Record<string, unknown> = {};
    try {
      body = (await response.json()) as Record<string, unknown>;
    } catch {
      // The status says it.
    }
    throw new ApiError(response.status, body);
  }
  const name = /filename="?([^";]+)"?/.exec(response.headers.get("Content-Disposition") ?? "")?.[1] ?? "workspace-export.zip";
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement("a");
  link.href = url;
  link.download = name;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/**
 * The full export of the workspace's data (p14): start one (CSV or JSON per kind of record, with the attachments and a manifest),
 * see its progress while it is written in the background, and download it within its few days. Alt+N starts one.
 */
export function ExportsPage() {
  const { t, format, language } = useI18n();
  const { can } = useSession();
  const [items, setItems] = useState<TenantExport[] | null>(null);
  const [kind, setKind] = useState<"csv" | "json">("csv");
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const canStart = can("importexport.exports.create");

  const load = useCallback(async () => {
    try {
      setItems((await api<{ items: TenantExport[] }>("GET", "/api/importexport/tenant-exports")).items);
    } catch (e) {
      setProblem(e instanceof ApiError ? e.message : t("importexport.problem"));
    }
  }, [t]);

  useEffect(() => {
    void load();
  }, [load]);

  const active = items?.some((e) => e.status === "queued" || e.status === "running") ?? false;
  useEffect(() => {
    if (!active) return;
    const timer = window.setInterval(() => void load(), pollMilliseconds);
    return () => window.clearInterval(timer);
  }, [active, load]);

  async function start() {
    if (busy || active) return;
    setBusy(true);
    setProblem(null);
    try {
      await api("POST", "/api/importexport/tenant-exports", { format: kind });
      await load();
    } catch (e) {
      setProblem(e instanceof ApiError ? e.message : t("importexport.problem"));
    } finally {
      setBusy(false);
    }
  }

  async function save(id: string) {
    setProblem(null);
    try {
      await download(id);
      await load();
    } catch (e) {
      setProblem(e instanceof ApiError ? e.message : t("importexport.problem"));
    }
  }

  useShortcut({ id: "importexport.exports.start", chord: "Alt+KeyN", labelKey: "importexport.exports.start", groupKey: "importexport.shortcut.group", enabled: canStart && !active, run: () => void start() });

  const when = (iso: string | null) => (iso ? format.dateTime(iso) : "");
  const size = (bytes: number) => t("importexport.exports.size", { megabytes: format.number(Math.max(0.1, Math.round(bytes / 104857.6) / 10)) });

  return (
    <section className="ie-page" aria-labelledby="ie-exports-title">
      <h1 id="ie-exports-title">{t("importexport.exports.title")}</h1>
      <p>{t("importexport.exports.intro")}</p>
      {canStart && (
        <div className="ie-start">
          <label>
            {t("importexport.exports.format")}{" "}
            <select value={kind} onChange={(e) => setKind(e.target.value as "csv" | "json")} aria-label={t("importexport.exports.format")}>
              <option value="csv">{t("importexport.exports.csv")}</option>
              <option value="json">{t("importexport.exports.json")}</option>
            </select>
          </label>
          <button type="button" className="button primary" disabled={busy || active} onClick={() => void start()} aria-keyshortcuts="Alt+N" data-testid="export-start">
            {t("importexport.exports.start")}
          </button>
        </div>
      )}
      {problem && (
        <p className="notice error" role="alert">
          {problem}
        </p>
      )}
      {items && items.length === 0 && <p className="ie-empty">{t("importexport.exports.none")}</p>}
      {items && items.length > 0 && (
        <table className="ie-exports" data-testid="exports-table" lang={language}>
          <thead>
            <tr>
              <th scope="col">{t("importexport.exports.started")}</th>
              <th scope="col">{t("importexport.exports.status")}</th>
              <th scope="col">{t("importexport.exports.contents")}</th>
              <th scope="col">{t("importexport.exports.expires")}</th>
              <th scope="col">
                <span className="ie-sr">{t("importexport.exports.actions")}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {items.map((e) => (
              <tr key={e.id} data-status={e.status}>
                <td>{when(e.createdAt)}</td>
                <td>
                  <span className={`ie-status ie-status-${e.status}`}>{t(`importexport.exports.status.${e.status}`)}</span>
                  {e.status === "failed" && e.failure && <span className="ie-failure"> {e.failure}</span>}
                </td>
                <td>
                  {e.status === "succeeded" || e.status === "expired"
                    ? t("importexport.exports.holds", { files: format.number(e.fileCount), size: size(e.byteSize), kind: e.format.toUpperCase() })
                    : ""}
                </td>
                <td>{e.status === "succeeded" ? when(e.expiresAt) : ""}</td>
                <td>
                  {e.downloadable && (
                    <button type="button" className="button" onClick={() => void save(e.id)} data-testid="export-download">
                      {t("importexport.exports.download")}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
