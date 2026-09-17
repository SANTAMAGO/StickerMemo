// StickerMemo Edge Deck Controller
const tauri = window.__TAURI__ || {};
const core = tauri.core || {};
const invoke = core.invoke || tauri.invoke || (window.__TAURI_INTERNALS__ && window.__TAURI_INTERNALS__.invoke);
const event = tauri.event || {};
const listen = event.listen || (window.__TAURI_INTERNALS__ && window.__TAURI_INTERNALS__.listen) || (() => {});

// i18n: window.StickerMemoI18n is loaded by ui/i18n.js (must be included
// before this script - see deck.html). `t()` here is a thin wrapper so the
// rest of this file can call t("some.key", {n: 3}) without null-checking
// the global every time.
const I18N = window.StickerMemoI18n;
function t(key, params) {
  return I18N ? I18N.t(key, params) : key;
}

const THEMES = {
  Yellow: { bg: "#FEF08A", accent: "#A16207", border: "#FACC15" },
  Pink: { bg: "#FBCFE8", accent: "#BE185D", border: "#F472B6" },
  Mint: { bg: "#A7F3D0", accent: "#047857", border: "#34D399" },
  Blue: { bg: "#BAE6FD", accent: "#0369A1", border: "#38BDF8" },
  Purple: { bg: "#DDD6FE", accent: "#6D28D9", border: "#A78BFA" },
  Kraft: { bg: "#E6D5B8", accent: "#78350F", border: "#C7A77D" }
};

function getTheme(id) {
  return THEMES[id] || THEMES.Yellow;
}

let allNotes = [];
let notesLoadVersion = 0;
let isDeckFan = false;
let isDrawerOpen = false;
let isArchivedTab = false;
let hoveredTab = null;

let hoverCloseTimer = null;
let tabLeaveTimer = null;
let searchDebounceTimer = null;
let deckBoundsVersion = 0;

const deckContainer = document.getElementById("deck-edge-container");
const tabsStack = document.getElementById("tabs-stack");
const addNoteBtn = document.getElementById("add-note-btn");
const moreNotesBtn = document.getElementById("more-notes-btn");
const moreCountText = document.getElementById("more-count-text");

const drawerContainer = document.getElementById("all-notes-drawer");
const drawerCloseBtn = document.getElementById("drawer-close-btn");
const tabActiveBtn = document.getElementById("tab-active-btn");
const tabArchivedBtn = document.getElementById("tab-archived-btn");
const searchInput = document.getElementById("search-input");
const drawerList = document.getElementById("drawer-list");
const drawerFooter = document.getElementById("drawer-footer");
function syncDeckNativeBounds(state) {
  const version = ++deckBoundsVersion;
  const delay = state === "preview" ? 180 : 220;
  setTimeout(() => {
    if (version !== deckBoundsVersion) return;
    const rects = [deckContainer.getBoundingClientRect()];
    if (isDrawerOpen) rects.push(drawerContainer.getBoundingClientRect());
    const left = Math.min(...rects.map((r) => r.left));
    const right = Math.max(...rects.map((r) => r.right));
    const top = Math.min(...rects.map((r) => r.top));
    const bottom = Math.max(...rects.map((r) => r.bottom));
    const width = Math.max(1, right - left);
    const height = Math.max(1, bottom - top);
    invoke("set_deck_interaction_state", { state, width, height }).catch((err) => {
      console.error("Failed to sync Deck native bounds:", err);
    });
  }, delay);
}
// =========================================================
// INITIALIZATION
// =========================================================
async function init() {
  if (I18N) {
    try {
      await I18N.initI18n(invoke);
      document.documentElement.lang = I18N.getCurrentLocale();
    } catch (err) {
      console.error("i18n init failed:", err);
    }
  }
  await loadNotes();
  setupEventListeners();
  setupTauriListeners();
  syncDeckNativeBounds("dormant");
}

async function loadNotes() {
  const version = ++notesLoadVersion;
  try {
    const active = await invoke("get_active_notes");
    if (version !== notesLoadVersion) return;
    allNotes = active;
    renderTabs();
    if (isDrawerOpen) {
      renderDrawerList();
    }
  } catch (err) {
    console.error("Failed to load notes:", err);
  }
}

// =========================================================
// DECK TABS RENDERING
// =========================================================
function renderTabs() {
  tabsStack.innerHTML = "";
  const visibleNotes = allNotes.slice(0, 8);

  visibleNotes.forEach((note) => {
    const theme = getTheme(note.themeId);
    const title = note.displayMainTitle || t("note.default_title");
    const preview = note.displayHoverBodyPreview || t("note.empty_preview");
    const tab = document.createElement("div");
    tab.className = `note-tab ${note.isFloating ? "floating" : ""}`;
    tab.dataset.id = note.id;
    tab.style.backgroundColor = theme.bg;
    tab.style.borderColor = note.isFloating ? theme.accent : theme.border;

    tab.innerHTML = `
      <!-- Compact View -->
      <div class="tab-compact-view">
        <div class="tab-tape-indicator" style="background-color: ${theme.accent};"></div>
        <span class="tab-title-text">${escapeHtml(title)}</span>
        ${note.isFloating ? '<span class="tab-pin-icon">📌</span>' : ""}
      </div>

      <!-- Hover Preview Card -->
      <div class="tab-hover-view">
        <div class="hover-header">
          <div class="hover-tape" style="background-color: ${theme.accent};"></div>
          <span class="hover-title">${escapeHtml(title)}</span>
        </div>
        <div class="hover-body">${escapeHtml(preview)}</div>
      </div>
    `;

    // Hover preview handlers
    tab.addEventListener("mouseenter", () => onTabMouseEnter(tab));
    tab.addEventListener("mouseleave", () => onTabMouseLeave(tab));

    // Click opens floating note
    tab.addEventListener("click", async (e) => {
      e.stopPropagation();
      try {
        await invoke("log_front", { msg: `Deck tab clicked for note ID: ${note.id} (${note.displayMainTitle})` });
      } catch {}
      collapseHoveredPreviewImmediate();
      try {
        await invoke("open_floating_note", { id: note.id });
        try {
          await invoke("log_front", { msg: `open_floating_note succeeded for ID: ${note.id}` });
        } catch {}
      } catch (err) {
        console.error("Failed to open floating note:", err);
        try {
          await invoke("log_front", { msg: `open_floating_note failed for ID: ${note.id}: ${err}` });
        } catch {}
      }
    });

    tabsStack.appendChild(tab);
  });

  // More notes button
  if (allNotes.length > 8) {
    const hiddenCount = allNotes.length - 8;
    moreCountText.textContent = t("deck.more_count", { n: hiddenCount });
    moreNotesBtn.classList.remove("hidden");
  } else {
    moreNotesBtn.classList.add("hidden");
  }
}

function onTabMouseEnter(tab) {
  if (!isDeckFan) return;
  clearTimeout(tabLeaveTimer);

  if (hoveredTab && hoveredTab !== tab) {
    hoveredTab.classList.remove("hover-preview");
  }

  hoveredTab = tab;
  tab.classList.add("hover-preview");
  syncDeckNativeBounds("preview");
}

function onTabMouseLeave(tab) {
  if (!isDeckFan) return;
  clearTimeout(tabLeaveTimer);
  tabLeaveTimer = setTimeout(() => {
    if (hoveredTab === tab) {
      tab.classList.remove("hover-preview");
      hoveredTab = null;
    }
  }, 200);
}

function collapseHoveredPreviewImmediate() {
  clearTimeout(tabLeaveTimer);
  if (hoveredTab) {
    hoveredTab.classList.remove("hover-preview");
    hoveredTab = null;
  }
}

// =========================================================
// DECK FAN / DORMANT STATE TRANSITIONS
// =========================================================
function transitionToFan() {
  clearTimeout(hoverCloseTimer);
  isDeckFan = true;
  deckContainer.classList.remove("state-dormant");
  deckContainer.classList.add("state-fan");
  syncDeckNativeBounds("fan");
}

function transitionToDormant() {
  collapseHoveredPreviewImmediate();
  isDeckFan = false;
  deckContainer.classList.remove("state-fan");
  deckContainer.classList.add("state-dormant");
  syncDeckNativeBounds("dormant");
}

// =========================================================
// ALL NOTES DRAWER
// =========================================================
async function openDrawer() {
  isDrawerOpen = true;
  isArchivedTab = false;
  tabActiveBtn.classList.add("active");
  tabArchivedBtn.classList.remove("active");
  searchInput.value = "";
  drawerContainer.classList.remove("hidden");
  await renderDrawerList();
  syncDeckNativeBounds("drawer");
}

function closeDrawer() {
  isDrawerOpen = false;
  drawerContainer.classList.add("hidden");
  syncDeckNativeBounds(isDeckFan ? "fan" : "dormant");
}

async function renderDrawerList() {
  drawerList.innerHTML = "";

  let list = [];
  try {
    if (isArchivedTab) {
      list = await invoke("get_archived_notes");
    } else {
      list = allNotes;
    }
  } catch (err) {
    console.error("Failed to query notes for drawer:", err);
  }

  const query = searchInput.value.trim().toLowerCase();
  if (query) {
    list = list.filter((n) =>
      (n.title && n.title.toLowerCase().includes(query)) ||
      (n.text && n.text.toLowerCase().includes(query))
    );
  }

  const totalCount = isArchivedTab ? list.length : allNotes.length;
  drawerFooter.textContent = isArchivedTab
    ? t("deck.drawer.footer_archived", { total: totalCount, shown: list.length })
    : t("deck.drawer.footer_active", { total: totalCount, shown: list.length });

  if (list.length === 0) {
    const emptyMsg = document.createElement("div");
    emptyMsg.style.textAlign = "center";
    emptyMsg.style.padding = "20px 0";
    emptyMsg.style.fontSize = "11px";
    emptyMsg.style.color = "#9CA3AF";
    emptyMsg.textContent = isArchivedTab ? t("deck.drawer.empty_archived") : t("deck.drawer.empty_active");
    drawerList.appendChild(emptyMsg);
    return;
  }

  list.forEach((note) => {
    const theme = getTheme(note.themeId);
    const item = document.createElement("div");
    item.className = `drawer-item ${note.isFloating ? "floating" : ""}`;
    item.style.borderColor = note.isFloating ? theme.accent : theme.border;

    const dateStr = note.updatedAt ? formatShortDate(note.updatedAt) : "";
    const title = note.displayMainTitle || t("note.default_title");

    item.innerHTML = `
      <div class="item-dot" style="background-color: ${theme.accent};"></div>
      <span class="item-title">${escapeHtml(title)}</span>
      <div class="item-right">
        ${isArchivedTab ? `
          <button class="restore-btn" title="${t("deck.drawer.restore_tooltip")}">↩</button>
          <button class="delete-btn" title="${t("common.delete_permanently_tooltip")}">🗑️</button>
        ` : (note.isFloating ? '<span>📌</span>' : '')}
        <span class="item-date">${dateStr}</span>
      </div>
    `;

    // Click on item opens floating note
    item.addEventListener("click", async (e) => {
      if (e.target.closest(".restore-btn") || e.target.closest(".delete-btn")) return;
      try {
        await invoke("log_front", { msg: `Drawer item clicked for note ID: ${note.id} (${note.displayMainTitle})` });
      } catch {}
      try {
        await invoke("open_floating_note", { id: note.id });
      } catch (err) {
        console.error("Failed to open floating note from drawer:", err);
        try {
          await invoke("log_front", { msg: `Drawer open_floating_note failed for ID: ${note.id}: ${err}` });
        } catch {}
      }
    });

    // Restore button
    const restoreBtn = item.querySelector(".restore-btn");
    if (restoreBtn) {
      restoreBtn.addEventListener("click", async (e) => {
        e.stopPropagation();
        await invoke("restore_note", { id: note.id });
        await loadNotes();
        await renderDrawerList();
      });
    }

    // Delete button
    const deleteBtn = item.querySelector(".delete-btn");
    if (deleteBtn) {
      deleteBtn.addEventListener("click", async (e) => {
        e.stopPropagation();
        showDeleteConfirm(async () => {
          await invoke("delete_note", { id: note.id });
          await invoke("close_floating_note", { id: note.id });
          await loadNotes();
          await renderDrawerList();
        });
      });
    }

    drawerList.appendChild(item);
  });
}

// =========================================================
// EVENT LISTENERS
// =========================================================
function setupEventListeners() {
  // Deck hover state
  deckContainer.addEventListener("mouseenter", () => {
    transitionToFan();
  });

  deckContainer.addEventListener("mouseleave", () => {
    clearTimeout(hoverCloseTimer);
    hoverCloseTimer = setTimeout(() => {
      transitionToDormant();
    }, 350);
  });

  // Add Note Button
  addNoteBtn.addEventListener("click", async () => {
    try {
      const newNote = await invoke("create_note");
      allNotes.unshift(newNote);
      renderTabs();
      await invoke("open_floating_note", { id: newNote.id });
    } catch (err) {
      console.error("Failed to create note:", err);
    }
  });

  // More Notes Button
  moreNotesBtn.addEventListener("click", (e) => {
    e.stopPropagation();
    openDrawer();
  });

  // Drawer Close Button
  drawerCloseBtn.addEventListener("click", closeDrawer);

  // Segmented Tabs
  tabActiveBtn.addEventListener("click", async () => {
    if (!isArchivedTab) return;
    isArchivedTab = false;
    tabActiveBtn.classList.add("active");
    tabArchivedBtn.classList.remove("active");
    await renderDrawerList();
  });

  tabArchivedBtn.addEventListener("click", async () => {
    if (isArchivedTab) return;
    isArchivedTab = true;
    tabArchivedBtn.classList.add("active");
    tabActiveBtn.classList.remove("active");
    await renderDrawerList();
  });

  // Search input debounce
  searchInput.addEventListener("input", () => {
    clearTimeout(searchDebounceTimer);
    searchDebounceTimer = setTimeout(renderDrawerList, 120);
  });

  // Escape to close drawer
  window.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && isDrawerOpen) {
      closeDrawer();
    }
  });
}

function removeDeletedNoteFromDeck(id) {
  ++notesLoadVersion;
  allNotes = allNotes.filter((n) => n.id !== id);
  renderTabs();
  if (isDrawerOpen) renderDrawerList();
  invoke("log_front", { msg: `Deck removed deleted note ID: ${id}` }).catch(() => {});
  loadNotes();
}

function setupTauriListeners() {
  listen("note-updated", (event) => {
    const updated = event.payload;
    const idx = allNotes.findIndex((n) => n.id === updated.id);
    if (idx !== -1) {
      allNotes[idx] = updated;
      renderTabs();
      if (isDrawerOpen) renderDrawerList();
    }
  });

  listen("notes-changed", () => {
    loadNotes();
  });

  Promise.resolve(listen("note-deleted", (event) => {
    removeDeletedNoteFromDeck(event.payload);
  })).then(() => {
    invoke("log_front", { msg: "Deck note-deleted listener registered" }).catch(() => {});
  }).catch((error) => {
    invoke("log_front", { msg: `Deck note-deleted listener failed: ${error}` }).catch(() => {});
  });

  listen("floating-state-changed", (event) => {
    const [id, isFloating] = event.payload;
    const note = allNotes.find((n) => n.id === id);
    if (note) {
      note.isFloating = isFloating;
      renderTabs();
      if (isDrawerOpen) renderDrawerList();
    }
  });

  // Locale change broadcast from Rust (see commands::retranslate_everything).
  // Reload the dictionary, retranslate static markup, then re-render
  // everything that was built from JS templates (tab titles/previews,
  // drawer footer counts, empty-state messages) so nothing is left in the
  // old language.
  listen("locale-changed", async (event) => {
    if (!I18N) return;
    try {
      await I18N.loadLocale(event.payload);
      document.documentElement.lang = I18N.getCurrentLocale();
      I18N.applyI18n();
      renderTabs();
      if (isDrawerOpen) await renderDrawerList();
    } catch (err) {
      console.error("Failed to apply locale change:", err);
    }
  });
}

function formatShortDate(iso) {
  try {
    const d = new Date(iso);
    const m = String(d.getMonth() + 1).padStart(2, "0");
    const day = String(d.getDate()).padStart(2, "0");
    return `${m}/${day}`;
  } catch {
    return "";
  }
}

function escapeHtml(str) {
  if (!str) return "";
  return str
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#039;");
}

document.addEventListener("DOMContentLoaded", init);
