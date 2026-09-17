// StickerMemo i18n loader (shared by deck.js, note.js, about.html, settings.js).
//
// Single source of truth: ui/lang/{locale}.json. This file only loads and
// applies that data - it never hardcodes translated strings itself.
//
// To add a new language later: add ui/lang/<code>.json (same key set as
// en.json) and add "<code>" to SUPPORTED_LOCALES below. No other file in
// ui/ needs to change.
(function () {
  const SUPPORTED_LOCALES = ["ko", "en", "ja"];
  const DEFAULT_LOCALE = "en";

  let currentLocale = DEFAULT_LOCALE;
  let dict = {};

  function normalizeLocale(loc) {
    return SUPPORTED_LOCALES.indexOf(loc) !== -1 ? loc : DEFAULT_LOCALE;
  }

  async function fetchDict(locale) {
    const res = await fetch(`lang/${locale}.json`);
    if (!res.ok) throw new Error(`lang/${locale}.json: HTTP ${res.status}`);
    return res.json();
  }

  // Loads the dictionary for `locale` (normalizing unsupported codes to
  // DEFAULT_LOCALE first) and falls back to DEFAULT_LOCALE if the fetch
  // itself fails for some reason (e.g. a partially-installed build).
  async function loadLocale(locale) {
    const normalized = normalizeLocale(locale);
    try {
      dict = await fetchDict(normalized);
      currentLocale = normalized;
    } catch (err) {
      console.error(`i18n: failed to load "${normalized}", falling back to "${DEFAULT_LOCALE}"`, err);
      if (normalized !== DEFAULT_LOCALE) {
        dict = await fetchDict(DEFAULT_LOCALE);
      }
      currentLocale = DEFAULT_LOCALE;
    }
    return currentLocale;
  }

  // t("some.key", {n: 3}) looks up "some.key" and replaces "{n}" with 3.
  // A missing key returns the key itself so a translation gap is visible
  // in the UI instead of silently blank.
  function t(key, params) {
    let s = Object.prototype.hasOwnProperty.call(dict, key) ? dict[key] : key;
    if (params) {
      for (const k of Object.keys(params)) {
        s = s.split(`{${k}}`).join(String(params[k]));
      }
    }
    return s;
  }

  // Applies the current dictionary to every element under `root` (default:
  // whole document) that carries a data-i18n* attribute. Safe to call again
  // after a locale change to retranslate already-rendered static markup.
  function applyI18n(root) {
    const scope = root || document;
    scope.querySelectorAll("[data-i18n]").forEach((el) => {
      el.textContent = t(el.getAttribute("data-i18n"));
    });
    scope.querySelectorAll("[data-i18n-title]").forEach((el) => {
      el.title = t(el.getAttribute("data-i18n-title"));
    });
    scope.querySelectorAll("[data-i18n-placeholder]").forEach((el) => {
      el.placeholder = t(el.getAttribute("data-i18n-placeholder"));
    });
  }

  // Resolves the effective locale via the Rust-side get_locale_info command
  // (Rust is the source of truth for "what locale is active right now",
  // since it also has to translate the tray menu and window titles), loads
  // that locale's dictionary, and applies it to static markup already in
  // the DOM. Returns the resolved locale code.
  async function initI18n(invoke) {
    let effective = DEFAULT_LOCALE;
    try {
      const info = await invoke("get_locale_info");
      effective = info && info.effective;
    } catch (err) {
      console.error("i18n: get_locale_info failed, defaulting to en", err);
    }
    await loadLocale(effective);
    applyI18n();
    return currentLocale;
  }

  window.StickerMemoI18n = {
    SUPPORTED_LOCALES,
    DEFAULT_LOCALE,
    loadLocale,
    t,
    applyI18n,
    initI18n,
    getCurrentLocale: () => currentLocale,
  };
})();
