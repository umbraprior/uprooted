import { initStarfield } from "./starfield.js";
import { init as initInstallerPage } from "./pages/main.js";
import { init as initThemesPage } from "./pages/themes.js";

const { getCurrentWindow } = (window as any).__TAURI__.window;

// Titlebar controls
function setupTitlebar(): void {
  const appWindow = getCurrentWindow();

  document.getElementById("btn-minimize")?.addEventListener("click", () => {
    appWindow.minimize();
  });

  document.getElementById("btn-close")?.addEventListener("click", () => {
    appWindow.close();
  });
}

// Page routing
type PageName = "installer" | "themes";
const pageInits: Record<PageName, (el: HTMLElement) => Promise<void>> = {
  installer: initInstallerPage,
  themes: initThemesPage,
};
const initialized = new Set<PageName>();

function switchPage(name: PageName): void {
  // Update nav tabs
  for (const tab of document.querySelectorAll<HTMLElement>(".nav-tab")) {
    tab.classList.toggle("active", tab.dataset.page === name);
  }

  // Update page visibility
  for (const page of document.querySelectorAll<HTMLElement>(".page")) {
    page.classList.toggle("active", page.id === `page-${name}`);
  }

  // Initialize page if not done yet
  if (!initialized.has(name)) {
    initialized.add(name);
    const el = document.getElementById(`page-${name}`);
    if (el && pageInits[name]) {
      pageInits[name](el);
    }
  }
}

function setupNav(): void {
  for (const tab of document.querySelectorAll<HTMLElement>(".nav-tab")) {
    tab.addEventListener("click", () => {
      const page = tab.dataset.page as PageName | undefined;
      if (page) switchPage(page);
    });
  }
}

// Boot
document.addEventListener("DOMContentLoaded", () => {
  initStarfield();
  setupTitlebar();
  setupNav();
  switchPage("installer");
});
