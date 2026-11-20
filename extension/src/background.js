"use strict";

const API_BASE = "http://127.0.0.1:47821";
const CACHE_KEY = "stillBlockedCache";
const REDIRECT_GUARD_MS = 1500;
const api = typeof browser !== "undefined" ? browser : chrome;
const redirecting = new Map();
let memoryCache = null;

function normalizeHost(host) {
  return String(host || "")
    .trim()
    .toLowerCase()
    .replace(/\.$/, "")
    .replace(/^www\./, "");
}

function matchesHost(host, target) {
  const normalizedHost = normalizeHost(host);
  const normalizedTarget = normalizeHost(target);
  return Boolean(normalizedHost && normalizedTarget)
    && (normalizedHost === normalizedTarget || normalizedHost.endsWith("." + normalizedTarget));
}

function parseHttpUrl(value) {
  try {
    const url = new URL(value);
    return url.protocol === "http:" || url.protocol === "https:" ? url : null;
  } catch {
    return null;
  }
}

async function fetchJson(path, options = {}) {
  const response = await fetch(API_BASE + path, {
    cache: "no-store",
    ...options
  });
  let data = null;
  try {
    data = await response.json();
  } catch {
    // The status code below still gives a useful error.
  }
  if (!response.ok) {
    throw new Error(data?.message || `Still API: HTTP ${response.status}`);
  }
  return data;
}

async function getCache() {
  try {
    const stored = await api.storage.local.get(CACHE_KEY);
    const cache = stored[CACHE_KEY];
    if (cache && Array.isArray(cache.domains)) {
      memoryCache = cache;
      return cache;
    }
  } catch (error) {
    console.debug("Still: lokaler Cache konnte nicht gelesen werden.", error);
  }

  return memoryCache;
}

async function saveCache(cache) {
  memoryCache = cache;
  try {
    await api.storage.local.set({ [CACHE_KEY]: cache });
  } catch (error) {
    console.debug("Still: lokaler Cache konnte nicht gespeichert werden.", error);
  }
}

async function refreshBlockedDomains() {
  try {
    const data = await fetchJson("/v1/blocked");
    if (!data || !Array.isArray(data.domains)) {
      throw new Error("Ungültige Antwort der Still-Schnittstelle.");
    }

    const domains = [...new Set(data.domains.map(normalizeHost).filter(Boolean))];
    const cache = {
      domains,
      fetchedAt: Date.now(),
      online: true
    };
    await saveCache(cache);
    return cache;
  } catch (error) {
    const previous = await getCache();
    if (!previous) {
      return null;
    }

    const offline = { ...previous, online: false };
    await saveCache(offline);
    console.debug("Still: Blockliste konnte nicht aktualisiert werden.", error);
    return offline;
  }
}

async function ensureCache() {
  const cached = await getCache();
  return cached || refreshBlockedDomains();
}

async function openStill() {
  try {
    await fetchJson("/v1/open", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: "{}"
    });
  } catch (error) {
    console.debug("Still konnte nicht geöffnet werden.", error);
  }
}

async function handleNavigation(details) {
  if (details.frameId !== 0 || details.tabId < 0) {
    return;
  }

  const parsed = parseHttpUrl(details.url);
  if (!parsed) {
    return;
  }

  if (redirecting.has(details.tabId)) {
    return;
  }

  const cache = await ensureCache();
  if (!cache || cache.domains.length === 0) {
    return;
  }

  const host = normalizeHost(parsed.hostname);
  if (!cache.domains.some(target => matchesHost(host, target))) {
    return;
  }

  redirecting.set(details.tabId, Date.now());
  const blockUrl = api.runtime.getURL("blocked.html")
    + "?url=" + encodeURIComponent(details.url)
    + "&host=" + encodeURIComponent(host);

  try {
    await api.tabs.update(details.tabId, { url: blockUrl });
  } catch (error) {
    console.debug("Still: Sperrseite konnte nicht geöffnet werden.", error);
  } finally {
    setTimeout(() => redirecting.delete(details.tabId), REDIRECT_GUARD_MS);
  }
}

api.webNavigation.onBeforeNavigate.addListener(details => {
  void handleNavigation(details);
});

api.runtime.onInstalled.addListener(() => {
  void refreshBlockedDomains();
});

api.runtime.onStartup.addListener(() => {
  void refreshBlockedDomains();
});

api.alarms.create("still-refresh", { periodInMinutes: 1 });
api.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === "still-refresh") {
    void refreshBlockedDomains();
  }
});

if (api.action?.onClicked) {
  api.action.onClicked.addListener(() => {
    void openStill();
  });
}

api.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === "still:refresh") {
    refreshBlockedDomains()
      .then(cache => sendResponse({ ok: Boolean(cache), cache }))
      .catch(error => sendResponse({ ok: false, error: String(error) }));
    return true;
  }

  if (message?.type === "still:open") {
    openStill()
      .then(() => sendResponse({ ok: true }))
      .catch(error => sendResponse({ ok: false, error: String(error) }));
    return true;
  }

  if (message?.type === "still:close") {
    const tabId = sender?.tab?.id;
    if (tabId === undefined || tabId === null) {
      sendResponse({ ok: false, error: "Kein Tab-Kontext." });
      return false;
    }

    api.tabs.remove(tabId)
      .then(() => sendResponse({ ok: true }))
      .catch(error => sendResponse({ ok: false, error: String(error) }));
    return true;
  }

  return false;
});
