import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { installFocusCycle } from "./kernel/focusCycle";
import { installHistoryGuard } from "./kernel/historyGuard";
import { App } from "./modules/shell/App";
import "./styles.css";

// A page restored from the back/forward cache is an old document with an old identity in memory
// (for example the screen of someone who has since signed out): load a fresh one instead.
window.addEventListener("pageshow", (event) => {
  if (event.persisted) window.location.reload();
});

// Before any screen reads the address: an entry left in this tab's history by an identity that has
// ended (Back after someone else signed out) opens the home screen, not that person's search or
// record (kernel/historyGuard).
installHistoryGuard();

// Tab past the last control comes back to the first, with no dead stop on the document
// (kernel/focusCycle); a modal dialog keeps its own cycle.
installFocusCycle();

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
