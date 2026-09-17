// StickerMemo Settings window controller.
//
// Two independent settings live here:
//   - Language: "system" | "ko" | "en" | "ja", persisted via the Settings
//     table (see set_locale / get_locale_info in commands/mod.rs). Changing
//     it applies immediately, app-wide, via the "locale-changed" broadcast.
//   - Autostart: a plain bool, persisted in the Windows registry (see
//     platform::autostart). Unrelated to locale, just surfaced in the same
//     window because there was nowhere else for it.
const tauri = window.__TAURI__ || {};
const core = tauri.core || {};
const invoke = core.invoke || tauri.invoke || (window.__TAURI_INTERNALS__ && window.__TAURI_INTERNALS__.invoke);
const event = tauri.event || {};
const listen = event.listen || (window.__TAURI_INTERNALS__ && window.__TAURI_INTERNALS__.listen) || (() => {});
const windowApi = tauri.window || {};

const I18N = window.StickerMemoI18n;

const closeBtn = document.getElementById("close");
const autostartCheckbox = document.getElementById("autostart-checkbox");
const langRadios = () => Array.from(document.querySelectorAll('input[name="language"]'));

function closeSettings() {
  if (windowApi.getCurrentWindow) {
    windowApi.getCurrentWindow().destroy();
  }
}

function selectLanguageRadio(preference) {
  langRadios().forEach((r) => {
    r.checked = r.value === preference;
  });
}

async function loadCurrentSettings() {
  try {
    const info = await invoke("get_locale_info");
    selectLanguageRadio(info && info.preference ? info.preference : "system");
  } catch (err) {
    console.error("Failed to load locale info:", err);
    selectLanguageRadio("system");
  }

  try {
    const enabled = await invoke("get_autostart");
    autostartCheckbox.checked = !!enabled;
  } catch (err) {
    console.error("Failed to load autostart state:", err);
  }
}

function setupEventListeners() {
  closeBtn.addEventListener("click", closeSettings);
  window.addEventListener("keydown", (e) => {
    if (e.key === "Escape") {
      e.preventDefault();
      closeSettings();
    }
  });

  langRadios().forEach((radio) => {
    radio.addEventListener("change", async () => {
      if (!radio.checked) return;
      try {
        await invoke("set_locale", { preference: radio.value });
      } catch (err) {
        console.error("Failed to set locale:", err);
        // Revert to whatever is actually persisted so the UI never shows
        // a selection that didn't take effect.
        await loadCurrentSettings();
      }
    });
  });

  autostartCheckbox.addEventListener("change", async () => {
    const enable = autostartCheckbox.checked;
    try {
      await invoke("set_autostart_setting", { enable });
    } catch (err) {
      console.error("Failed to set autostart:", err);
      autostartCheckbox.checked = !enable;
    }
  });

  // The Settings window is usually the one *causing* a locale change, but
  // it still listens for the broadcast like every other window so its own
  // labels retranslate immediately too (and so a redundant open stays in
  // sync if the language was somehow changed elsewhere first).
  listen("locale-changed", async (evt) => {
    if (!I18N) return;
    try {
      await I18N.loadLocale(evt.payload);
      document.documentElement.lang = I18N.getCurrentLocale();
      I18N.applyI18n();
    } catch (err) {
      console.error("Failed to apply locale change:", err);
    }
  });
}

async function init() {
  if (I18N) {
    try {
      await I18N.initI18n(invoke);
      document.documentElement.lang = I18N.getCurrentLocale();
    } catch (err) {
      console.error("i18n init failed:", err);
    }
  }
  await loadCurrentSettings();
  setupEventListeners();
}

document.addEventListener("DOMContentLoaded", init);
