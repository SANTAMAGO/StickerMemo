// StickerMemo Floating Note Controller
const tauri = window.__TAURI__ || {};
const core = tauri.core || {};
const invoke = core.invoke || tauri.invoke || (window.__TAURI_INTERNALS__ && window.__TAURI_INTERNALS__.invoke);

const urlParams = new URLSearchParams(window.location.search);
const noteId = urlParams.get("id");


let note = null;
let saveTimer = null;
let winMoveSaveTimer = null;

const paperCard = document.getElementById("paper-card");
const tapeThemeLabel = document.getElementById("tape-theme-label");
const titleInput = document.getElementById("title-input");
const contentEditor = document.getElementById("content-editor");
const timestampText = document.getElementById("timestamp-text");

const fontBtn = document.getElementById("font-btn");
const colorBtn = document.getElementById("color-btn");
const archiveBtn = document.getElementById("archive-btn");
const deleteBtn = document.getElementById("delete-btn");
const closeBtn = document.getElementById("close-btn");

const colorPopup = document.getElementById("color-popup");
const fontPopup = document.getElementById("font-popup");

const fontFamilySelect = document.getElementById("font-family-select");
const btnDecSize = document.getElementById("btn-dec-size");
const btnIncSize = document.getElementById("btn-inc-size");
const fontSizeText = document.getElementById("font-size-text");
const boldChip = document.getElementById("bold-chip");
const resizeGrip = document.getElementById("resize-grip");

async function init() {
  try {
    note = await invoke("get_current_note");
    if (!note) {
      const urlParams = new URLSearchParams(window.location.search);
      const noteId = urlParams.get("id");
      if (noteId) {
        note = await invoke("get_note_by_id", { id: noteId });
      }
    }
    if (note) {
      applyNoteToUi();
      setupEventListeners();
      setupWindowTracking();
    }
  } catch (err) {
    console.error("Failed to load note:", err);
  }
}


function applyNoteToUi() {
  if (!note) return;

  // Theme
  setThemeClass(note.themeId);

  // Text
  titleInput.value = note.title || "";
  contentEditor.value = note.text || "";

  // Timestamp
  updateTimestampText();

  // Font
  fontFamilySelect.value = note.fontFamily || "Malgun Gothic";
  applyFontToEditor();
}

function setThemeClass(themeId) {
  const validThemes = ["Yellow", "Pink", "Mint", "Blue", "Purple", "Kraft"];
  const theme = validThemes.includes(themeId) ? themeId : "Yellow";

  validThemes.forEach((t) => paperCard.classList.remove(`theme-${t}`));
  paperCard.classList.add(`theme-${theme}`);
  tapeThemeLabel.textContent = theme.toUpperCase();
}

function applyFontToEditor() {
  contentEditor.style.fontFamily = note.fontFamily || "Malgun Gothic";
  titleInput.style.fontFamily = note.fontFamily || "Malgun Gothic";

  const size = note.fontSize || 13.5;
  contentEditor.style.fontSize = `${size}pt`;
  fontSizeText.textContent = `${size} pt`;

  if (note.isBold) {
    contentEditor.style.fontWeight = "bold";
    boldChip.classList.add("active");
  } else {
    contentEditor.style.fontWeight = "normal";
    boldChip.classList.remove("active");
  }
}

function updateTimestampText() {
  if (note && note.updatedAt) {
    try {
      const d = new Date(note.updatedAt);
      const m = String(d.getMonth() + 1).padStart(2, "0");
      const day = String(d.getDate()).padStart(2, "0");
      const h = String(d.getHours()).padStart(2, "0");
      const min = String(d.getMinutes()).padStart(2, "0");
      timestampText.textContent = `수정: ${m}/${day} ${h}:${min}`;
      return;
    } catch {}
  }
  timestampText.textContent = "방금 수정됨";
}

function triggerSave() {
  clearTimeout(saveTimer);
  saveTimer = setTimeout(async () => {
    if (!note) return;
    note.title = titleInput.value;
    note.text = contentEditor.value;
    try {
      const saved = await invoke("save_note", { note });
      note = saved;
      updateTimestampText();
    } catch (err) {
      console.error("Autosave failed:", err);
    }
  }, 400);
}

function setupEventListeners() {
  // Title & Content input
  titleInput.addEventListener("input", triggerSave);
  contentEditor.addEventListener("input", triggerSave);

  // Tab key inside textarea inserts tab or spaces
  contentEditor.addEventListener("keydown", (e) => {
    if (e.key === "Tab") {
      e.preventDefault();
      const start = contentEditor.selectionStart;
      const end = contentEditor.selectionEnd;
      contentEditor.value = contentEditor.value.substring(0, start) + "\t" + contentEditor.value.substring(end);
      contentEditor.selectionStart = contentEditor.selectionEnd = start + 1;
      triggerSave();
    }
  });

  // Font button
  fontBtn.addEventListener("click", (e) => {
    e.stopPropagation();
    colorPopup.classList.add("hidden");
    fontPopup.classList.toggle("hidden");
  });

  // Color button
  colorBtn.addEventListener("click", (e) => {
    e.stopPropagation();
    fontPopup.classList.add("hidden");
    colorPopup.classList.toggle("hidden");
  });

  // Color dots
  colorPopup.querySelectorAll(".color-dot").forEach((dot) => {
    dot.addEventListener("click", (e) => {
      e.stopPropagation();
      const newTheme = dot.dataset.theme;
      note.themeId = newTheme;
      setThemeClass(newTheme);
      colorPopup.classList.add("hidden");
      triggerSave();
    });
  });

  // Font family change
  fontFamilySelect.addEventListener("change", () => {
    note.fontFamily = fontFamilySelect.value;
    applyFontToEditor();
    triggerSave();
  });

  // Font size dec
  btnDecSize.addEventListener("click", (e) => {
    e.stopPropagation();
    if (note.fontSize > 8) {
      note.fontSize = Math.max(8, Math.round((note.fontSize - 1.0) * 10) / 10);
      applyFontToEditor();
      triggerSave();
    }
  });

  // Font size inc
  btnIncSize.addEventListener("click", (e) => {
    e.stopPropagation();
    if (note.fontSize < 36) {
      note.fontSize = Math.min(36, Math.round((note.fontSize + 1.0) * 10) / 10);
      applyFontToEditor();
      triggerSave();
    }
  });

  // Bold chip
  boldChip.addEventListener("click", (e) => {
    e.stopPropagation();
    note.isBold = !note.isBold;
    applyFontToEditor();
    triggerSave();
  });

  // Archive
  archiveBtn.addEventListener("click", async (e) => {
    e.stopPropagation();
    await invoke("archive_note", { id: note.id });
  });

  // Delete
  deleteBtn.addEventListener("click", async (e) => {
    e.stopPropagation();
    showDeleteConfirm(async () => {
      await invoke("delete_note", { id: note.id });
      await invoke("close_floating_note", { id: note.id });
    });
  });

  // Close / Dock back
  closeBtn.addEventListener("click", async (e) => {
    e.stopPropagation();
    await invoke("close_floating_note", { id: note.id });
  });

  // Click outside closes popups
  document.addEventListener("click", (e) => {
    if (!fontPopup.contains(e.target) && e.target !== fontBtn) {
      fontPopup.classList.add("hidden");
    }
    if (!colorPopup.contains(e.target) && e.target !== colorBtn) {
      colorPopup.classList.add("hidden");
    }
  });

  // Custom resize grip handler
  setupResizeGrip();
}

function setupResizeGrip() {
  let isResizing = false;
  let startX = 0;
  let startY = 0;
  let startWidth = 350;
  let startHeight = 350;


  resizeGrip.addEventListener("mousedown", async (e) => {
    e.preventDefault();
    isResizing = true;
    startX = e.screenX;
    startY = e.screenY;

    try {
      const [w, h] = await invoke("get_note_window_size");
      startWidth = w;
      startHeight = h;
    } catch {}

    const onMouseMove = (moveEvent) => {
      if (!isResizing) return;
      const dx = moveEvent.screenX - startX;
      const dy = moveEvent.screenY - startY;
      const newWidth = Math.max(280, startWidth + dx);
      const newHeight = Math.max(180, startHeight + dy);

      invoke("set_note_window_size", { width: newWidth, height: newHeight });
      note.width = newWidth;
      note.height = newHeight;
      scheduleWindowSave();
    };

    const onMouseUp = () => {
      isResizing = false;
      document.removeEventListener("mousemove", onMouseMove);
      document.removeEventListener("mouseup", onMouseUp);
      scheduleWindowSave();
    };

    document.addEventListener("mousemove", onMouseMove);
    document.addEventListener("mouseup", onMouseUp);
  });
}

function setupWindowTracking() {
  const topBar = document.getElementById("top-bar");
  topBar.addEventListener("mousedown", (e) => {
    if (e.target.closest(".toolbar") || e.target.closest("button")) return;
    invoke("start_dragging").catch(() => {});
  });

  topBar.addEventListener("mouseup", async () => {
    try {
      const [x, y] = await invoke("get_note_window_position");
      note.x = x;
      note.y = y;
      scheduleWindowSave();
    } catch {}
  });
}

function scheduleWindowSave() {
  clearTimeout(winMoveSaveTimer);
  winMoveSaveTimer = setTimeout(() => {
    if (note) {
      invoke("save_note", { note });
    }
  }, 500);
}

document.addEventListener("DOMContentLoaded", init);

