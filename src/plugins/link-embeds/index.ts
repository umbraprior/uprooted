/**
 * Link Embeds Plugin -- Discord-style link previews for URLs in chat.
 *
 * Watches for new <a> elements via MutationObserver and renders rich embed
 * cards with OpenGraph metadata. YouTube URLs get special inline player embeds.
 */

import type { UprootedPlugin } from "../../types/plugin.js";
import { fetchMetadata, clearCache } from "./providers.js";
import { createEmbedCard } from "./embeds.js";

const LINK_PATTERN = /^https?:\/\//;
const LOG = "[Uprooted:link-embeds]";

let observer: MutationObserver | null = null;
const processedLinks = new WeakSet<HTMLAnchorElement>();

// --- Debug: remote log to localhost debug server ---
const DEBUG_URL = "http://localhost:9876/log";
// Stash the real fetch before sentry-blocker wraps it
const _rawFetch = window.fetch.bind(window);

function dbg(msg: string): void {
  const line = `[${new Date().toLocaleTimeString()}] ${msg}`;
  console.log(`${LOG} ${msg}`);
  // POST to debug server (fire-and-forget)
  try {
    _rawFetch(DEBUG_URL, {
      method: "POST",
      headers: { "Content-Type": "text/plain" },
      body: `${LOG} ${line}`,
    }).catch(() => {});
  } catch {}
}
// --- End debug ---

function getPluginConfig(): {
  youtube: boolean;
  websites: boolean;
  maxEmbedsPerMessage: number;
} {
  const config =
    window.__UPROOTED_SETTINGS__?.plugins?.["link-embeds"]?.config;
  return {
    youtube: (config?.youtube as boolean) ?? true,
    websites: (config?.websites as boolean) ?? true,
    maxEmbedsPerMessage: (config?.maxEmbedsPerMessage as number) ?? 3,
  };
}

/** Count existing embeds within the closest message-like ancestor. */
function countEmbedsInContext(anchor: HTMLAnchorElement): number {
  let container: HTMLElement | null = anchor.parentElement;
  for (let i = 0; i < 5 && container; i++) {
    container = container.parentElement;
  }
  if (!container) container = anchor.parentElement;
  return container?.querySelectorAll(".uprooted-embed").length ?? 0;
}

/** Find the best insertion point for the embed card. */
function findInsertionPoint(anchor: HTMLAnchorElement): {
  parent: Node;
  ref: Node | null;
} {
  let block: HTMLElement | null = anchor;
  while (block && block !== document.body) {
    const display = getComputedStyle(block).display;
    if (display === "block" || display === "flex" || display === "grid") {
      return { parent: block.parentNode!, ref: block.nextSibling };
    }
    block = block.parentElement;
  }
  return { parent: anchor.parentNode!, ref: anchor.nextSibling };
}

async function processLink(anchor: HTMLAnchorElement): Promise<void> {
  if (processedLinks.has(anchor)) return;
  processedLinks.add(anchor);

  const href = anchor.href;
  if (!LINK_PATTERN.test(href)) {
    dbg(`Skip non-http: ${href.slice(0, 60)}`);
    return;
  }

  // Skip links inside Uprooted's own UI
  if (anchor.closest('[id^="uprooted-"], [data-uprooted]')) {
    dbg(`Skip uprooted-ui link: ${href.slice(0, 60)}`);
    return;
  }

  const config = getPluginConfig();

  const isYouTube = /(?:youtube\.com|youtu\.be)/.test(href);
  if (isYouTube && !config.youtube) {
    dbg(`YouTube disabled, skip: ${href.slice(0, 60)}`);
    return;
  }
  if (!isYouTube && !config.websites) {
    dbg(`Websites disabled, skip: ${href.slice(0, 60)}`);
    return;
  }

  if (countEmbedsInContext(anchor) >= config.maxEmbedsPerMessage) {
    dbg(`Max embeds reached, skip: ${href.slice(0, 60)}`);
    return;
  }

  dbg(`Processing: ${href}`);

  const data = await fetchMetadata(href);
  if (!data) {
    dbg(`No metadata for: ${href.slice(0, 60)}`);
    return;
  }

  if (!anchor.isConnected) {
    dbg(`Anchor gone from DOM: ${href.slice(0, 60)}`);
    return;
  }

  if (countEmbedsInContext(anchor) >= config.maxEmbedsPerMessage) {
    dbg(`Max embeds (post-fetch), skip: ${href.slice(0, 60)}`);
    return;
  }

  dbg(`Got metadata: "${data.title}" [${data.type}]`);
  const card = createEmbedCard(data);
  const { parent, ref } = findInsertionPoint(anchor);

  try {
    parent.insertBefore(card, ref);
    dbg(`INSERTED embed for: ${href.slice(0, 60)}`);
  } catch (err) {
    dbg(`Insert failed, trying fallback...`);
    try {
      anchor.parentNode?.insertBefore(card, anchor.nextSibling);
      dbg(`INSERTED (fallback) for: ${href.slice(0, 60)}`);
    } catch (err2) {
      dbg(`ALL insertions failed for: ${href.slice(0, 60)}`);
    }
  }
}

let mutationCount = 0;

function scanForLinks(root: Node): void {
  const anchors =
    root instanceof HTMLElement
      ? root.querySelectorAll<HTMLAnchorElement>("a[href]")
      : [];

  if (anchors.length > 0) {
    dbg(`Scan: ${anchors.length} link(s) in node <${(root as HTMLElement).tagName?.toLowerCase?.() ?? "?"}>`);
  }

  for (const anchor of anchors) {
    if (!processedLinks.has(anchor) && LINK_PATTERN.test(anchor.href)) {
      processLink(anchor);
    }
  }

  if (
    root instanceof HTMLAnchorElement &&
    root.href &&
    LINK_PATTERN.test(root.href)
  ) {
    processLink(root);
  }
}

function onMutations(mutations: MutationRecord[]): void {
  let addedElements = 0;
  for (const mutation of mutations) {
    for (const node of mutation.addedNodes) {
      if (node.nodeType === Node.ELEMENT_NODE) {
        addedElements++;
        scanForLinks(node);
      }
    }
  }
  // Log mutation batches periodically (every 50th batch to avoid spam)
  mutationCount++;
  if (addedElements > 0 && mutationCount <= 20) {
    dbg(`Mutation #${mutationCount}: ${addedElements} element(s) added`);
  }
}

export default {
  name: "link-embeds",
  description: "Discord-style link previews for URLs in chat",
  version: "0.2.5",
  authors: [{ name: "Uprooted" }],

  settings: {
    youtube: {
      type: "boolean",
      default: true,
      description: "Show YouTube video embeds",
    },
    websites: {
      type: "boolean",
      default: true,
      description: "Show website link previews",
    },
    maxEmbedsPerMessage: {
      type: "number",
      default: 3,
      min: 1,
      max: 10,
      description: "Maximum embeds per message",
    },
  },

  start() {
    dbg(`Context: ${location.href}`);
    dbg(`Title: "${document.title}"`);
    dbg(`Body children: ${document.body?.children.length ?? 0}`);

    observer = new MutationObserver(onMutations);
    observer.observe(document.body, { childList: true, subtree: true });

    const existingLinks = document.querySelectorAll<HTMLAnchorElement>("a[href]");
    dbg(`Started -- ${existingLinks.length} existing link(s)`);

    for (const a of Array.from(existingLinks).slice(0, 15)) {
      dbg(`  existing: ${a.href.slice(0, 80)} ("${a.textContent?.slice(0, 30) ?? ""}")`);
    }
    if (existingLinks.length > 15) dbg(`  ...and ${existingLinks.length - 15} more`);

    scanForLinks(document.body);
  },

  stop() {
    if (observer) {
      observer.disconnect();
      observer = null;
    }

    const removed = document.querySelectorAll(".uprooted-embed");
    removed.forEach((el) => el.remove());

    clearCache();
    dbg(`Stopped (removed ${removed.length} embeds)`);
  },
} satisfies UprootedPlugin;
