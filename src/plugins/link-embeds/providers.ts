/**
 * Link Embeds -- URL parsing, OpenGraph metadata fetching, and caching.
 */

export interface EmbedData {
  url: string;
  type: "generic" | "youtube";
  provider?: string;
  title?: string;
  description?: string;
  image?: string;
  color?: string;
  videoId?: string;
}

const metadataCache = new Map<string, EmbedData | null>();

const FETCH_TIMEOUT = 8000;

const LOG = "[Uprooted:link-embeds]";
const DEBUG_URL = "http://localhost:9876/log";

function dbgProvider(msg: string): void {
  const line = `[${new Date().toLocaleTimeString()}] ${msg}`;
  console.log(`${LOG} ${msg}`);
  try {
    // Use XMLHttpRequest to avoid going through any fetch wrappers
    const xhr = new XMLHttpRequest();
    xhr.open("POST", DEBUG_URL, true);
    xhr.setRequestHeader("Content-Type", "text/plain");
    xhr.send(`${LOG} ${line}`);
  } catch {}
}

/**
 * Extract a YouTube video ID from common URL patterns.
 * Returns null if the URL is not a recognized YouTube link.
 */
export function parseYouTubeId(url: string): string | null {
  try {
    const u = new URL(url);
    const host = u.hostname.replace("www.", "");

    if (host === "youtube.com" || host === "m.youtube.com") {
      // /watch?v=ID
      if (u.pathname === "/watch") {
        return u.searchParams.get("v");
      }
      // /embed/ID or /shorts/ID
      const match = u.pathname.match(/^\/(embed|shorts)\/([^/?&]+)/);
      if (match) return match[2];
    }

    // youtu.be/ID
    if (host === "youtu.be") {
      const id = u.pathname.slice(1).split(/[/?&]/)[0];
      return id || null;
    }
  } catch {
    // invalid URL
  }
  return null;
}

/**
 * Parse OpenGraph meta tags from raw HTML.
 */
export function parseOpenGraph(html: string): {
  title?: string;
  description?: string;
  image?: string;
  siteName?: string;
  themeColor?: string;
} {
  const result: Record<string, string> = {};

  // Match <meta property="og:..." content="..."> and <meta name="og:..." content="...">
  // Also pick up theme-color meta tag
  const metaRegex =
    /<meta\s+(?:[^>]*?\s)?(?:property|name)\s*=\s*["']([^"']+)["'][^>]*?\scontent\s*=\s*["']([^"']*)["'][^>]*?\/?>/gi;

  let match: RegExpExecArray | null;
  while ((match = metaRegex.exec(html)) !== null) {
    const [, key, value] = match;
    result[key.toLowerCase()] = value;
  }

  // Also try reversed attribute order: content before property
  const metaRegexReverse =
    /<meta\s+(?:[^>]*?\s)?content\s*=\s*["']([^"']*)["'][^>]*?\s(?:property|name)\s*=\s*["']([^"']+)["'][^>]*?\/?>/gi;

  while ((match = metaRegexReverse.exec(html)) !== null) {
    const [, value, key] = match;
    const k = key.toLowerCase();
    if (!result[k]) result[k] = value;
  }

  return {
    title: result["og:title"],
    description: result["og:description"],
    image: result["og:image"],
    siteName: result["og:site_name"],
    themeColor: result["theme-color"],
  };
}

/**
 * Fetch metadata for a URL and return an EmbedData object.
 * YouTube URLs use thumbnail patterns and oEmbed. Other URLs fetch HTML and parse OG tags.
 * Returns null on failure.
 */
export async function fetchMetadata(url: string): Promise<EmbedData | null> {
  if (metadataCache.has(url)) {
    dbgProvider(` Cache hit for ${url}`);
    return metadataCache.get(url)!;
  }

  try {
    const videoId = parseYouTubeId(url);

    if (videoId) {
      dbgProvider(` Fetching YouTube metadata for ${url} (id: ${videoId})`);
      const data = await fetchYouTubeMetadata(url, videoId);
      metadataCache.set(url, data);
      dbgProvider(` YouTube metadata:`, data?.title ?? "(no title)");
      return data;
    }

    dbgProvider(` Fetching generic metadata for ${url}`);
    const data = await fetchGenericMetadata(url);
    metadataCache.set(url, data);
    dbgProvider(` Generic metadata:`, data ? `"${data.title}"` : "null (no data)");
    return data;
  } catch (err) {
    dbgProvider(` fetchMetadata failed for ${url}:`, err);
    metadataCache.set(url, null);
    return null;
  }
}

async function fetchYouTubeMetadata(
  url: string,
  videoId: string,
): Promise<EmbedData> {
  const data: EmbedData = {
    url,
    type: "youtube",
    provider: "YouTube",
    videoId,
    image: `https://img.youtube.com/vi/${videoId}/hqdefault.jpg`,
    color: "#FF0000",
  };

  // Try oEmbed for the title
  try {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), FETCH_TIMEOUT);

    const resp = await fetch(
      `https://www.youtube.com/oembed?url=${encodeURIComponent(url)}&format=json`,
      { signal: controller.signal },
    );
    clearTimeout(timer);

    if (resp.ok) {
      const json = (await resp.json()) as { title?: string; author_name?: string };
      data.title = json.title;
      if (json.author_name) data.description = json.author_name;
    }
  } catch {
    // oEmbed failed -- thumbnail still works
  }

  return data;
}

async function fetchGenericMetadata(url: string): Promise<EmbedData | null> {
  const controller = new AbortController();
  // Timeout covers the ENTIRE operation (fetch + body reading)
  const timer = setTimeout(() => controller.abort(), FETCH_TIMEOUT);

  let resp: Response;
  try {
    resp = await fetch(url, {
      signal: controller.signal,
      headers: { Accept: "text/html" },
    });
  } catch (err) {
    clearTimeout(timer);
    dbgProvider(` Fetch failed for ${url}:`, err);
    return null;
  }

  if (!resp.ok) {
    clearTimeout(timer);
    dbgProvider(` Non-OK response for ${url}: ${resp.status}`);
    return null;
  }

  const contentType = resp.headers.get("content-type") ?? "";
  if (!contentType.includes("text/html")) {
    clearTimeout(timer);
    dbgProvider(` Non-HTML content-type for ${url}: ${contentType}`);
    return null;
  }

  // Read only first 50KB to avoid loading huge pages
  let html = "";
  try {
    const reader = resp.body?.getReader();
    if (!reader) {
      // Fallback: use text() for environments where body.getReader() isn't available
      html = (await resp.text()).slice(0, 50_000);
    } else {
      const decoder = new TextDecoder();
      const MAX_BYTES = 50_000;

      while (html.length < MAX_BYTES) {
        const { done, value } = await reader.read();
        if (done) break;
        html += decoder.decode(value, { stream: true });
      }
      reader.cancel().catch(() => {});
    }
  } catch (err) {
    clearTimeout(timer);
    dbgProvider(` Body read failed for ${url}:`, err);
    if (!html) return null;
    // If we got partial HTML, try to parse what we have
  }
  clearTimeout(timer);

  const og = parseOpenGraph(html);

  // Need at least a title to show an embed
  if (!og.title) {
    // Fall back to <title> tag
    const titleMatch = html.match(/<title[^>]*>([^<]+)<\/title>/i);
    if (titleMatch) og.title = titleMatch[1].trim();
  }

  if (!og.title) return null;

  // Resolve relative image URLs
  let image = og.image;
  if (image && !image.startsWith("http")) {
    try {
      image = new URL(image, url).href;
    } catch {
      image = undefined;
    }
  }

  // Extract hostname for provider name
  let provider = og.siteName;
  if (!provider) {
    try {
      provider = new URL(url).hostname.replace("www.", "");
    } catch {
      // leave undefined
    }
  }

  return {
    url,
    type: "generic",
    provider,
    title: og.title,
    description: og.description,
    image,
    color: og.themeColor,
  };
}

/** Clear the metadata cache (used on plugin stop). */
export function clearCache(): void {
  metadataCache.clear();
}
