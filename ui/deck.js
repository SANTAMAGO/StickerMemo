// StickerMemo Edge Deck Controller
const tauri = window.__TAURI__ || {};
const core = tauri.core || {};
const invoke = core.invoke || tauri.invoke || (window.__TAURI_INTERNALS__ && window.__TAURI_INTERNALS__.invoke);
const event = tauri.event || {};
const listen = event.listen || (window.__TAURI_INTERNALS__ && window.__TAURI_INTERNALS__.listen) || (() => {});


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
let isDeckFan = false;
let isDrawerOpen = false;
let isArchivedTab = false;
let hoveredTab = null;

let hoverCloseTimer = null;
let tabLeaveTimer = null;
let searchDebounceTimer = null;

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

// =========================================================
// INITIALIZATION
// =========================================================
async function init() {
  await loadNotes();
  setupEventListeners();
  setupTauriListeners();
}

async function loadNotes() {
  try {
    const active = await invoke("get_active_notes");
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
    const tab = document.createElement("div");
    tab.className = `note-tab ${note.isFloating ? "floating" : ""}`;
    tab.dataset.id = note.id;
    tab.style.backgroundColor = theme.bg;
    tab.style.borderColor = note.isFloating ? theme.accent : theme.border;

    tab.innerHTML = `
      <!-- Compact View -->
      <div class="tab-compact-view">
        <div class="tab-tape-indicator" style="background-color: ${theme.accent};"></div>
        <span class="tab-title-text">${escapeHtml(note.displayMainTitle)}</span>
        ${note.isFloating ? '<span class="tab-pin-icon">📌</span>' : ""}
      </div>

      <!-- Hover Preview Card -->
      <div class="tab-hover-view">
        <div class="hover-header">
          <div class="hover-tape" style="background-color: ${theme.accent};"></div>
          <span class="hover-title">${escapeHtml(note.displayMainTitle)}</span>
        </div>
        <div class="hover-body">${escapeHtml(note.displayHoverBodyPreview)}</div>
      </div>
    `;

    // Hover preview handlers
    tab.addEventListener("mouseenter", () => onTabMouseEnter(tab));
    tab.addEventListener("mouseleave", () => onTabMouseLeave(tab));

    // Click opens floating note
    tab.addEventListener("click", async (e) => {
      e.stopPropagation();
      console.log("Deck tab clicked for note ID:", note.id);
      collapseHoveredPreviewImmediate();
      try {
        await invoke("open_floating_note", { id: note.id });
      } catch (err) {
        console.error("Failed to open floating note:", err);
      }
    });

    tabsStack.appendChild(tab);
  });

  // More notes button
  if (allNotes.length > 8) {
    const hiddenCount = allNotes.length - 8;
    moreCountText.textContent = `+${hiddenCount} more`;
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
}

function transitionToDormant() {
  collapseHoveredPreviewImmediate();
  isDeckFan = false;
  deckContainer.classList.remove("state-fan");
  deckContainer.classList.add("state-dormant");
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
}

function closeDrawer() {
  isDrawerOpen = false;
  drawerContainer.classList.add("hidden");
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
    ? `총 ${totalCount}개의 보관된 메모 중 ${list.length}개 표시`
    : `총 ${totalCount}개의 활성 메모 중 ${list.length}개 표시`;

  if (list.length === 0) {
    const emptyMsg = document.createElement("div");
    emptyMsg.style.textAlign = "center";
    emptyMsg.style.padding = "20px 0";
    emptyMsg.style.fontSize = "11px";
    emptyMsg.style.color = "#9CA3AF";
    emptyMsg.textContent = isArchivedTab ? "보관된 메모가 없습니다." : "표시할 활성 메모가 없습니다.";
    drawerList.appendChild(emptyMsg);
    return;
  }

  list.forEach((note) => {
    const theme = getTheme(note.themeId);
    const item = document.createElement("div");
    item.className = `drawer-item ${note.isFloating ? "floating" : ""}`;
    item.style.borderColor = note.isFloating ? theme.accent : theme.border;

    const dateStr = note.updatedAt ? formatShortDate(note.updatedAt) : "";

    item.innerHTML = `
      <div class="item-dot" style="background-color: ${theme.accent};"></div>
      <span class="item-title">${escapeHtml(note.displayMainTitle)}</span>
      <div class="item-right">
        ${isArchivedTab ? `
          <button class="restore-btn" title="보관 해제 및 활성 메모로 복원">↩</button>
          <button class="delete-btn" title="메모 영구 삭제">🗑️</button>
        ` : (note.isFloating ? '<span>📌</span>' : '')}
        <span class="item-date">${dateStr}</span>
      </div>
    `;

    // Click on item opens floating note
    item.addEventListener("click", async (e) => {
      if (e.target.closest(".restore-btn") || e.target.closest(".delete-btn")) return;
      console.log("Drawer item clicked for note ID:", note.id);
      try {
        await invoke("open_floating_note", { id: note.id });
      } catch (err) {
        console.error("Failed to open floating note from drawer:", err);
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
        if (confirm("이 메모를 영구 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다.")) {
          await invoke("delete_note", { id: note.id });
          await loadNotes();
          await renderDrawerList();
        }
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

  listen("note-deleted", (event) => {
    const id = event.payload;
    allNotes = allNotes.filter((n) => n.id !== id);
    renderTabs();
    if (isDrawerOpen) renderDrawerList();
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
