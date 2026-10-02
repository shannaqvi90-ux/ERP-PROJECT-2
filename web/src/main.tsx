import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./modules/shell/App";
import "./styles.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
