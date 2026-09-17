// Shared in-app confirmation for permanent note deletion.
window.showDeleteConfirm = function (deleteAction) {
  if (document.querySelector(".delete-confirm-backdrop")) return;

  const previousFocus = document.activeElement;
  const overlay = document.createElement("div");
  overlay.className = "delete-confirm-backdrop";
  overlay.innerHTML =
    '<section class="delete-confirm-card" role="dialog" aria-modal="true" aria-labelledby="delete-confirm-title" aria-describedby="delete-confirm-description">' +
      '<div class="delete-confirm-icon" aria-hidden="true">!</div>' +
      '<h2 id="delete-confirm-title">이 메모를 영구 삭제하시겠습니까?</h2>' +
      '<p id="delete-confirm-description">이 작업은 되돌릴 수 없습니다.</p>' +
      '<p class="delete-confirm-error" role="alert" hidden>삭제하지 못했습니다. 다시 시도해 주세요.</p>' +
      '<div class="delete-confirm-actions">' +
        '<button type="button" class="delete-confirm-cancel">취소</button>' +
        '<button type="button" class="delete-confirm-delete">삭제</button>' +
      '</div>' +
    '</section>';

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