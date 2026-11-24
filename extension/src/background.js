"use strict";

const API_BASE = "http://127.0.0.1:47821";
const CACHE_KEY = "stillBlockedCache";
const REFRESH_ALARM = "still-refresh";
const CACHE_FRESH_MS = 30_000;
const REDIRECT_GUARD_MS = 1_500;
const USAGE_KEY = "stillWebsiteUsage";
const USAGE_BATCH_SIZE = 50;
const USAGE_MAX_PENDING = 2_000;

const api = typeof browser !== "undefined" ? browser : chrome;
const redirecting = new Map();
let memoryCache = null;
let usageState = null;
let usageQueue = Promise.resolve();

function normalizeHost(host) {
  return String(host || "")
    .trim()
    .toLowerCase()
    .replace(/\.$/, "")
    .replace(/^www\./, "");
}

function localDateKey(value) {
  const date = new Date(value);
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function validDateKey(value) {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    return null;
  }

  const parsed = new Date(`${value}T00:00:00`);
  return Number.isNaN(parsed.getTime()) || localDateKey(parsed.getTime()) !== value
    ? null
    : value;
}

function parseTarget(value) {
  const raw = String(value || "").trim();
  const exact = raw.startsWith("=");
  return {
    exact,
    host: normalizeHost(exact ? raw.slice(1) : raw)
  };
}

function matchesHost(host, target) {
  const normalizedHost = normalizeHost(host);
  const parsed = parseTarget(target);
  return Boolean(normalizedHost && parsed.host)
    && (normalizedHost === parsed.host
      || (!parsed.exact && normalizedHost.endsWith("." + parsed.host)));
}

function parseHttpUrl(value) {
  try {
    const url = new URL(value);
    return url.protocol === "http:" || url.protocol === "https:" ? url : null;
  } catch {
    return null;
  }
}

function isExtensionPage(value) {
  try {
    const protocol = new URL(value).protocol;
    return protocol === "moz-extension:" || protocol === "chrome-extension:";
  } catch {
    return false;
  }
}

function browserName() {
  const ua = navigator.userAgent || "";
  if (/Edg\//.test(ua)) return "Microsoft Edge";
  if (/OPR\//.test(ua)) return "Opera";
  if (/Vivaldi\//.test(ua)) return "Vivaldi";
  if (/Firefox\//.test(ua)) return "Mozilla Firefox";
  if (/Chrome\//.test(ua)) return "Google Chrome";
  return "Browser";
}

function runUsageWork(work) {
  usageQueue = usageQueue
    .then(work)
    .catch(error => {
      console.debug("Still: Website-Zeit konnte nicht verarbeitet werden.", error);
    });
  return usageQueue;
}

function validActiveSession(value) {
  if (!value || typeof value.host !== "string" || !value.host) {
    return null;
  }

  const startedAt = Number(value.startedAt);
  if (!Number.isFinite(startedAt) || startedAt <= 0) {
    return null;
  }

  return {
    host: normalizeHost(value.host),
    startedAt: Math.min(startedAt, Date.now())
  };
}

async function loadUsageState() {
  if (usageState) {
    return usageState;
  }

  let stored = null;
  try {
    const result = await api.storage.local.get(USAGE_KEY);
    stored = result?.[USAGE_KEY];
  } catch (error) {
    console.debug("Still: Website-Zeit konnte nicht gelesen werden.", error);
  }

  const pending = Array.isArray(stored?.pending)
    ? stored.pending
        .filter(entry => entry && typeof entry.host === "string" && Number(entry.seconds) > 0)
        .map(entry => ({
          date: validDateKey(entry.date) || localDateKey(Date.now()),
          host: normalizeHost(entry.host),
          seconds: Math.min(86_400, Math.max(1, Math.floor(Number(entry.seconds))))
        }))
        .filter(entry => entry.host)
        .slice(0, USAGE_MAX_PENDING)
    : [];

  usageState = {
    active: validActiveSession(stored?.active),
    pending
  };
  return usageState;
}

async function saveUsageState() {
  if (!usageState) {
    return;
  }

  try {
    await api.storage.local.set({ [USAGE_KEY]: usageState });
  } catch (error) {
    console.debug("Still: Website-Zeit konnte nicht gespeichert werden.", error);
  }
}

function queuePending(state, host, date, seconds) {
  const value = Math.min(86_400, Math.max(1, Math.floor(seconds)));
  const day = validDateKey(date) || localDateKey(Date.now());
  const existing = state.pending.find(entry => entry.host === host && entry.date === day);
  if (existing) {
    existing.seconds = Math.min(86_400, existing.seconds + value);
    return;
  }

  if (state.pending.length >= USAGE_MAX_PENDING) {
    state.pending.shift();
  }

  state.pending.push({ date: day, host, seconds: value });
}

function queueUsageInterval(state, host, startedAt, endedAt) {
  const end = Number(endedAt);
  let cursor = Number(startedAt);
  if (!Number.isFinite(cursor) || !Number.isFinite(end) || cursor >= end) {
    return;
  }

  while (cursor < end) {
    const dayStart = new Date(cursor);
    dayStart.setHours(0, 0, 0, 0);
    const nextDay = new Date(dayStart.getTime());
    nextDay.setDate(nextDay.getDate() + 1);

    const segmentEnd = Math.min(end, nextDay.getTime());
    const seconds = Math.floor((segmentEnd - cursor) / 1000);
    if (seconds >= 1) {
      queuePending(state, host, localDateKey(cursor), seconds);
    }

    cursor = segmentEnd;
  }
}

async function focusedWebsite() {
  if (!api.windows?.getLastFocused) {
    return null;
  }

  let window = null;
  try {
    window = await api.windows.getLastFocused({ populate: true });
  } catch {
    return null;
  }

  if (!window || window.focused === false || (window.type && window.type !== "normal")) {
    return null;
  }

  let tab = Array.isArray(window.tabs) ? window.tabs.find(item => item.active) : null;
  if (!tab && api.tabs?.query && typeof window.id === "number") {
    try {
      const tabs = await api.tabs.query({ active: true, windowId: window.id });
      tab = tabs?.[0] || null;
    } catch {
      return null;
    }
  }

  if (!tab?.url) {
    return null;
  }

  const parsed = parseHttpUrl(tab.url);
  if (!parsed || isExtensionPage(tab.url)) {
    return null;
  }

  return normalizeHost(parsed.hostname) || null;
}

function stopActiveSession(state, now) {
  if (!state.active) {
    return;
  }

  queueUsageInterval(state, state.active.host, state.active.startedAt, now);
  state.active = null;
}

async function sendPendingUsage(state) {
  while (state.pending.length > 0) {
    const batch = state.pending.slice(0, USAGE_BATCH_SIZE);
    try {
      await request("/v1/website-usage", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ entries: batch })
      });
      state.pending = state.pending.slice(batch.length);
      await saveUsageState();
    } catch (error) {
      // Keep the batch and retry on the next alarm. The app is local, so this
      // normally only happens while Still is closed.
      console.debug("Still: Website-Zeit konnte nicht gesendet werden.", error);
      return;
    }
  }
}

async function syncActiveWebsiteLocked() {
  const state = await loadUsageState();
  const now = Date.now();
  const host = await focusedWebsite();
  let stopped = false;

  if (state.active && state.active.host !== host) {
    stopActiveSession(state, now);
    stopped = true;
  }

  if (host && !state.active) {
    state.active = { host, startedAt: now };
  }

  await saveUsageState();

  if (stopped || !state.active) {
    await sendPendingUsage(state);
  }
}

async function flushUsageLocked() {
  const state = await loadUsageState();
  const now = Date.now();
  const host = await focusedWebsite();

  if (state.active && state.active.host !== host) {
    stopActiveSession(state, now);
  }

  if (host && !state.active) {
    state.active = { host, startedAt: now };
  }
  else if (host && state.active) {
    const startedAt = state.active.startedAt;
    if (now - startedAt >= 1000) {
      queueUsageInterval(state, state.active.host, startedAt, now);
      state.active.startedAt = now;
    }
  }

  await saveUsageState();
  await sendPendingUsage(state);
}

function syncActiveWebsite() {
  return runUsageWork(syncActiveWebsiteLocked);
}

function flushUsage() {
  return runUsageWork(flushUsageLocked);
}

function resetUsageSession() {
  return runUsageWork(async () => {
    const state = await loadUsageState();
    state.active = null;
    await saveUsageState();
  });
}

function clientInfo() {
  let version = "0";
  try {
    version = api.runtime.getManifest().version || version;
  } catch {
    // The manifest is always available in a real extension context.
  }

  return {
    version,
    browser: browserName(),
    userAgent: navigator.userAgent || ""
  };
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
    // The status code below still gives a useful error.
  }
  if (!response.ok) {
    throw new Error(data?.message || `Still API: HTTP ${response.status}`);
  }
  return data;
}

async function sayHello() {
  try {
    await request("/v1/hello", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(clientInfo())
    });
  } catch (error) {
    console.debug("Still: App nicht erreichbar.", error);
  }
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

async function updateBadge(cache) {
  if (!api.action?.setBadgeText) {
    return;
  }

  const count = Array.isArray(cache?.domains) ? cache.domains.length : 0;
  try {
    await api.action.setBadgeText({ text: count > 0 ? String(count) : "" });
    await api.action.setBadgeBackgroundColor({ color: count > 0 ? "#C4F5A9" : "#3A4432" });
    if (api.action.setBadgeTextColor) {
      await api.action.setBadgeTextColor({ color: "#0B110A" });
    }
    await api.action.setTitle({
      title: count > 0 ? `Still · ${count} Website-Regeln aktiv` : "Still Website Blocker"
    });
  } catch (error) {
    console.debug("Still: Badge konnte nicht aktualisiert werden.", error);
  }
}

async function refreshBlockedDomains(enforceOpenTabs = false) {
  await sayHello();

  try {
    const data = await request("/v1/blocked");
    if (!data || !Array.isArray(data.domains)) {
      throw new Error("Ungültige Antwort der Still-Schnittstelle.");
    }

    const domains = [...new Set(data.domains.map(value => {
      const parsed = parseTarget(value);
      return parsed.host ? (parsed.exact ? "=" + parsed.host : parsed.host) : "";
    }).filter(Boolean))];
    const cache = {
      domains,
      fetchedAt: Date.now(),
      online: true
    };
    await saveCache(cache);
    await updateBadge(cache);
    if (enforceOpenTabs) {
      try {
        await enforceTabs(cache);
      } catch (error) {
        console.debug("Still: offene Tabs konnten nicht geprüft werden.", error);
      }
    }
    return cache;
  } catch (error) {
    const previous = await getCache();
    if (!previous) {
      await updateBadge(null);
      return null;
    }

    const offline = { ...previous, online: false };
    await saveCache(offline);
    await updateBadge(offline);
    console.debug("Still: Blockliste konnte nicht aktualisiert werden.", error);
    return offline;
  }
}

async function ensureCache() {
  const cached = await getCache();
  const age = cached ? Date.now() - (cached.fetchedAt || 0) : Number.POSITIVE_INFINITY;
  if (cached && age < CACHE_FRESH_MS) {
    return cached;
  }

  return (await refreshBlockedDomains()) || cached;
}

async function openStill() {
  try {
    await request("/v1/open", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: "{}"
    });
  } catch (error) {
    console.debug("Still konnte nicht geöffnet werden.", error);
  }
}

async function blockTab(tabId, url, host) {
  if (redirecting.has(tabId)) {
    return;
  }

  redirecting.set(tabId, Date.now());
  const blockUrl = api.runtime.getURL("blocked.html")
    + "?url=" + encodeURIComponent(url)
    + "&host=" + encodeURIComponent(host);

  try {
    await api.tabs.update(tabId, { url: blockUrl });
  } catch (error) {
    console.debug("Still: Sperrseite konnte nicht geöffnet werden.", error);
  } finally {
    setTimeout(() => redirecting.delete(tabId), REDIRECT_GUARD_MS);
  }
}

async function handleNavigation(details) {
  if (details.frameId !== 0 || details.tabId < 0) {
    return;
  }

  const parsed = parseHttpUrl(details.url);
  if (!parsed || isExtensionPage(details.url)) {
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

  await blockTab(details.tabId, details.url, host);
}

// A rule can activate while a matching site is already open. Redirect those
// tabs on the periodic refresh so the block does not wait for a reload.
async function enforceTabs(cache) {
  if (!cache || cache.domains.length === 0 || !api.tabs?.query) {
    return;
  }

  let tabs = [];
  try {
    tabs = await api.tabs.query({});
  } catch (error) {
    console.debug("Still: offene Tabs konnten nicht gelesen werden.", error);
    return;
  }

  for (const tab of tabs) {
    if (typeof tab.id !== "number" || tab.id < 0 || !tab.url) {
      continue;
    }

    const parsed = parseHttpUrl(tab.url);
    if (!parsed || isExtensionPage(tab.url)) {
      continue;
    }

    const host = normalizeHost(parsed.hostname);
    if (cache.domains.some(target => matchesHost(host, target))) {
      await blockTab(tab.id, tab.url, host);
    }
  }
}

api.webNavigation.onBeforeNavigate.addListener(details => {
  void handleNavigation(details);
});

if (api.webNavigation.onHistoryStateUpdated) {
  api.webNavigation.onHistoryStateUpdated.addListener(details => {
    void handleNavigation(details);
    if (details.frameId === 0) {
      void syncActiveWebsite();
    }
  });
}

api.tabs.onActivated.addListener(() => {
  void syncActiveWebsite();
});

api.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (changeInfo.url || changeInfo.status === "complete") {
    void syncActiveWebsite();
  }
});

api.tabs.onRemoved.addListener(() => {
  void syncActiveWebsite();
});

if (api.windows?.onFocusChanged) {
  api.windows.onFocusChanged.addListener(() => {
    void syncActiveWebsite();
  });
}

api.runtime.onInstalled.addListener(() => {
  void refreshBlockedDomains(true);
  void resetUsageSession().then(() => syncActiveWebsite());
});

api.runtime.onStartup.addListener(() => {
  void refreshBlockedDomains(true);
  void resetUsageSession().then(() => syncActiveWebsite());
});

api.alarms.create(REFRESH_ALARM, { periodInMinutes: 1 });
api.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === REFRESH_ALARM) {
    void refreshBlockedDomains(true);
    void flushUsage();
  }
});

api.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === "still:refresh") {
    refreshBlockedDomains(true)
      .then(cache => sendResponse({ ok: Boolean(cache), cache }))
      .catch(error => sendResponse({ ok: false, error: String(error) }));
    return true;
  }

  if (message?.type === "still:hello") {
    sayHello()
      .then(() => sendResponse({ ok: true }))
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

void refreshBlockedDomains(true);
void syncActiveWebsite();
