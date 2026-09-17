// Shared in-app confirmation for permanent note deletion.
// Loaded on both deck.html and note.html, always after ui/i18n.js (see the
// <script> order in each), so window.StickerMemoI18n is available by the
// time this function actually runs (it's only invoked lazily, on a delete
// click, by which point i18n has long since finished initializing).
window.showDeleteConfirm = function (deleteAction) {
  if (document.querySelector(".delete-confirm-backdrop")) return;

  const I18N = window.StickerMemoI18n;
  const t = (key) => (I18N ? I18N.t(key) : key);

  const previousFocus = document.activeElement;
  const overlay = document.createElement("div");
  overlay.className = "delete-confirm-backdrop";
  overlay.innerHTML =
    '<section class="delete-confirm-card" role="dialog" aria-modal="true" aria-labelledby="delete-confirm-title" aria-describedby="delete-confirm-description">' +
      '<div class="delete-confirm-icon" aria-hidden="true">!</div>' +
      '<h2 id="delete-confirm-title"></h2>' +
      '<p id="delete-confirm-description"></p>' +
      '<p class="delete-confirm-error" role="alert" hidden></p>' +
      '<div class="delete-confirm-actions">' +
        '<button type="button" class="delete-confirm-cancel"></button>' +
        '<button type="button" class="delete-confirm-delete"></button>' +
      '</div>' +
    '</section>';

  overlay.querySelector("#delete-confirm-title").textContent = t("delete_confirm.title");
  overlay.querySelector("#delete-confirm-description").textContent = t("delete_confirm.description");
  overlay.querySelector(".delete-confirm-error").textContent = t("delete_confirm.error");
  overlay.querySelector(".delete-confirm-cancel").textContent = t("common.cancel");
  overlay.querySelector(".delete-confirm-delete").textContent = t("common.delete");

  const cancelButton = overlay.querySelector(".delete-confirm-cancel");
  const deleteButton = overlay.querySelector(".delete-confirm-delete");
  const errorText = overlay.querySelector(".delete-confirm-error");
  let busy = false;

  function close() {
    if (busy) return;
    document.removeEventListener("keydown", onKeyDown, true);
    overlay.remove();
    if (previousFocus && previousFocus.isConnected) previousFocus.focus();
  }

  function onKeyDown(event) {
    if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      close();
    }
  }

  cancelButton.addEventListener("click", close);
  overlay.addEventListener("click", (event) => {
    if (event.target === overlay) close();
  });
  deleteButton.addEventListener("click", async () => {
    if (busy) return;
    busy = true;
    cancelButton.disabled = true;
    deleteButton.disabled = true;
    errorText.hidden = true;
    try {
      await deleteAction();
      busy = false;
      close();
    } catch (error) {
      console.error("Failed to delete note:", error);
      errorText.hidden = false;
      busy = false;
      cancelButton.disabled = false;
      deleteButton.disabled = false;
      deleteButton.focus();
    }
  });

  document.body.appendChild(overlay);
  document.addEventListener("keydown", onKeyDown, true);
  cancelButton.focus();
};