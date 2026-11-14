import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import "./spike.css";
import "./spike-hmr";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
