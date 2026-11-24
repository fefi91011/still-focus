"use strict";

const API_BASE = "http://127.0.0.1:47821";
const api = typeof browser !== "undefined" ? browser : chrome;

const elements = {
  status: document.getElementById("status"),
  connection: document.getElementById("connection"),
  count: document.getElementById("count"),
  updated: document.getElementById("updated"),
  notice: document.getElementById("notice"),
  refresh: document.getElementById("refresh"),
  open: document.getElementById("open"),
  version: document.getElementById("version")
};

let busy = false;

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

function setOnline(online) {
  elements.status.className = "status " + (online ? "online" : "offline");
}

function setBusy(value) {
  busy = value;
  elements.refresh.disabled = value;
  elements.open.disabled = value;
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
    // The HTTP status remains the useful error.
  }
  if (!response.ok) {
    throw new Error(data?.message || `Still API: HTTP ${response.status}`);
  }
  return data;
}

function formatTime(value) {
  if (!value) {
    return "–";
  }

  try {
    return new Date(value).toLocaleTimeString("de-DE", {
      hour: "2-digit",
      minute: "2-digit"
    });
  } catch {
    return "–";
  }
}

async function load() {
  setBusy(true);
  try {
    const ping = await request("/v1/ping");
    const data = await request("/v1/blocked");
    const domains = Array.isArray(data?.domains) ? data.domains : [];

    setOnline(true);
    elements.connection.textContent = `Still ${ping.version || ""} ist verbunden.`;
    elements.count.textContent = String(domains.length);
    elements.updated.textContent = formatTime(data?.updatedAt || Date.now());
    showNotice("");
  } catch {
    setOnline(false);
    elements.connection.textContent = "Still ist nicht erreichbar. Starte die App, damit Regeln und Nachfristen funktionieren.";
    elements.count.textContent = "–";
    elements.updated.textContent = "–";
    showNotice("Die zuletzt geladene Blockliste bleibt aktiv.", false);
  } finally {
    setBusy(false);
  }
}

async function refresh() {
  if (busy) {
    return;
  }

  setBusy(true);
  showNotice("");
  try {
    const response = await api.runtime.sendMessage({ type: "still:refresh" });
    if (!response?.ok) {
      throw new Error(response?.error || "Die Blockliste konnte nicht geladen werden.");
    }
    showNotice("Blockliste aktualisiert.", false);
    await load();
  } catch (error) {
    showNotice("Still ist nicht erreichbar. Die vorhandene Blockliste bleibt aktiv.", true);
  } finally {
    setBusy(false);
  }
}

async function openStill() {
  try {
    await api.runtime.sendMessage({ type: "still:open" });
    window.close();
  } catch {
    showNotice("Still konnte nicht geöffnet werden.", true);
  }
}

elements.refresh.addEventListener("click", () => {
  void refresh();
});

elements.open.addEventListener("click", () => {
  void openStill();
});

try {
  elements.version.textContent = "v" + (api.runtime.getManifest().version || "");
} catch {
  elements.version.textContent = "";
}

void load();
