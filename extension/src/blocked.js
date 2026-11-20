"use strict";

const API_BASE = "http://127.0.0.1:47821";
const api = typeof browser !== "undefined" ? browser : chrome;
const params = new URLSearchParams(window.location.search);
const originalUrl = safeHttpUrl(params.get("url"));
const host = normalizeHost(params.get("host")
  || (originalUrl ? new URL(originalUrl).hostname : ""));

const elements = {
  kind: document.getElementById("kind"),
  subject: document.getElementById("subject"),
  rules: document.getElementById("rules"),
  explanation: document.getElementById("explanation"),
  notice: document.getElementById("notice"),
  close: document.getElementById("close"),
  closeFooter: document.getElementById("close-footer"),
  contact: document.getElementById("contact"),
  grace: document.getElementById("grace")
};

let status = null;
let busy = false;

function safeHttpUrl(value) {
  try {
    const url = new URL(value);
    return url.protocol === "http:" || url.protocol === "https:" ? url.toString() : null;
  } catch {
    return null;
  }
}

function normalizeHost(value) {
  return String(value || "")
    .trim()
    .toLowerCase()
    .replace(/\.$/, "")
    .replace(/^www\./, "");
}

async function request(path, options = {}) {
  const response = await fetch(API_BASE + path, {
    cache: "no-store",
    ...options
  });
  let data = null;
  try {
    data = await response.json();
  } catch {
    // Keep the HTTP status as the error below.
  }
  if (!response.ok) {
    throw new Error(data?.message || `Still API: HTTP ${response.status}`);
  }
  return data;
}

function showNotice(message, error = false) {
  if (!message) {
    elements.notice.hidden = true;
    elements.notice.textContent = "";
    return;
  }

  elements.notice.textContent = message;
  elements.notice.className = "notice" + (error ? " error" : " success");
  elements.notice.hidden = false;
}

function setBusy(value) {
  busy = value;
  elements.contact.disabled = value;
  elements.grace.disabled = value;
  if (value) {
    elements.grace.textContent = "Einen Moment…";
  }
}

function render() {
  if (!status) {
    return;
  }

  elements.kind.textContent = status.kindLabel || "Website gesperrt";
  elements.subject.textContent = status.subject || host || "Website";
  elements.rules.textContent = status.ruleNames?.length
    ? "Regel: " + status.ruleNames.join(", ")
    : "Eine Regel greift gerade.";
  elements.explanation.textContent = status.explanation || "";
  document.title = "Website gesperrt · " + (status.subject || host || "Still");

  const canGrace = Boolean(status.graceAvailable && status.graceRuleId && status.graceMinutes > 0);
  elements.grace.hidden = !canGrace;
  elements.grace.textContent = canGrace ? `+${status.graceMinutes} Minuten` : "";
  setBusy(busy);
}

function showOffline(detail) {
  status = null;
  elements.kind.textContent = "Website gesperrt";
  elements.subject.textContent = host || "Website";
  elements.rules.textContent = "Still ist nicht erreichbar.";
  elements.explanation.textContent = detail || "Starte Still, damit die Nachfrist und der Weg zurück funktionieren.";
  elements.grace.hidden = true;
  elements.contact.disabled = true;
  document.title = "Website gesperrt · Still";
}

function goBack() {
  if (originalUrl) {
    window.location.replace(originalUrl);
    return;
  }

  showNotice("Diese Website ist nicht mehr gesperrt.", false);
}

async function loadStatus() {
  if (!host) {
    showOffline("Die blockierte Adresse konnte nicht gelesen werden.");
    return;
  }

  try {
    const data = await request("/v1/status?host=" + encodeURIComponent(host));
    if (!data?.status) {
      throw new Error(data?.message || "Ungültige Antwort der Still-Schnittstelle.");
    }

    status = data.status;
    if (!status.blocked) {
      goBack();
      return;
    }

    render();
  } catch (error) {
    showOffline("Still ist nicht erreichbar. Starte die App und lade diese Seite neu.");
  }
}

async function grantGrace() {
  if (busy || !status?.graceRuleId || !status.graceAvailable) {
    return;
  }

  setBusy(true);
  showNotice("");
  try {
    const data = await request("/v1/grace", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ ruleId: status.graceRuleId, host })
    });

    if (!data?.ok) {
      if (data?.status) {
        status = data.status;
        render();
      }
      showNotice(data?.message || "Die Nachfrist konnte nicht vergeben werden.", true);
      return;
    }

    showNotice(data.message || "Nachfrist aktiviert.", false);
    status = data.status || status;

    // Refresh the background cache before returning to the site, otherwise the
    // next navigation would immediately hit the old list and show this page again.
    try {
      await api.runtime.sendMessage({ type: "still:refresh" });
    } catch {
      // The next alarm refresh also recovers the cache.
    }

    if (status?.blocked) {
      render();
      showNotice("Die Website bleibt durch eine weitere Regel gesperrt.", true);
      return;
    }

    goBack();
  } catch (error) {
    showNotice("Still ist nicht erreichbar. Die Nachfrist wurde nicht vergeben.", true);
  } finally {
    setBusy(false);
  }
}

async function contactApprovers() {
  if (busy) {
    return;
  }

  showNotice("");
  setBusy(true);
  try {
    const data = await request("/v1/contact", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: "{}"
    });
    showNotice(data?.message || "Still öffnet deine Kontaktpersonen.", false);
  } catch {
    showNotice("Still ist nicht erreichbar. Bitte starte die App.", true);
  } finally {
    setBusy(false);
  }
}

async function closeTab() {
  try {
    const response = await api.runtime.sendMessage({ type: "still:close" });
    if (response?.ok) {
      return;
    }
  } catch {
    // Fall through to the direct tab API.
  }

  try {
    const tab = await api.tabs.getCurrent();
    if (tab?.id !== undefined && tab.id !== null) {
      await api.tabs.remove(tab.id);
      return;
    }
  } catch {
    // Falls back to closing the window below.
  }

  window.close();
}

elements.close.addEventListener("click", () => {
  void closeTab();
});

elements.closeFooter.addEventListener("click", () => {
  void closeTab();
});

elements.contact.addEventListener("click", () => {
  void contactApprovers();
});

elements.grace.addEventListener("click", () => {
  void grantGrace();
});

document.addEventListener("keydown", event => {
  if (event.key === "Escape") {
    void closeTab();
  }
});

void loadStatus();
