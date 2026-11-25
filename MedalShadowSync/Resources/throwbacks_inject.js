
/* REMEDAL_THROWBACK_PATCH_V20 */
;(function () {
  if (globalThis.__REMEDAL_THROWBACK_LOADED__) return;
  if (!Array.isArray(process?.argv) || !process.argv.includes("--renderer_name=main")) return;
  globalThis.__REMEDAL_THROWBACK_LOADED__ = true;

  const electron = require("electron");
  const REMEDAL_CFG = __REMEDAL_CONFIG_JSON__;
  const CFG_WINDOW_DAYS = (typeof REMEDAL_CFG?.windowDays === "number" && REMEDAL_CFG.windowDays >= 0)
    ? Math.min(90, Math.round(REMEDAL_CFG.windowDays))
    : 0;
  const CFG_BLOCK_ADS = REMEDAL_CFG?.blockAds !== false;
  const CFG_MAX_YEARS = (typeof REMEDAL_CFG?.maxYears === "number" && REMEDAL_CFG.maxYears >= 1)
    ? Math.min(10, Math.round(REMEDAL_CFG.maxYears))
    : (REMEDAL_CFG?.onlyLastYear ? 1 : 10);
  const CFG_ICON_URI = typeof REMEDAL_CFG?.iconDataUri === "string" ? REMEDAL_CFG.iconDataUri : "";

  console.info("[reMedal] V20 loaded", { windowDays: CFG_WINDOW_DAYS, blockAds: CFG_BLOCK_ADS, maxYears: CFG_MAX_YEARS });

  const STORY_ITEM_ID = "remedal-throwback-story";
  const MODAL_ID = "remedal-throwbacks-story-modal";
  const ADBLOCK_STYLE_ID = "remedal-adblock-css";
  const WATCHED_STORAGE_KEY = "remedal_watched_throwbacks_v2";
  const VOLUME_STORAGE_KEY = "remedal_player_volume_v1";

  const AD_URL_PATTERNS = [
    "medal.tv/ads",
    "doubleclick",
    "googlesyndication",
    "googleadservices",
    "playwire",
    "nitropay",
    "adnxs",
    "didomi",
    "cmp_popup"
  ];

  let cachedClips = null;
  let cachedDayKey = "";
  let fetching = false;
  let ensureTimer = null;

  function getWatchedSet() {
    try {
      const raw = localStorage.getItem(WATCHED_STORAGE_KEY);
      if (!raw) return new Set();
      const arr = JSON.parse(raw);
      return Array.isArray(arr) ? new Set(arr) : new Set();
    } catch {
      return new Set();
    }
  }

  function markWatched(clipId) {
    if (!clipId) return;
    try {
      const set = getWatchedSet();
      if (set.has(clipId)) return;
      set.add(clipId);
      const arr = Array.from(set).slice(-500);
      localStorage.setItem(WATCHED_STORAGE_KEY, JSON.stringify(arr));
    } catch {
      // Ignore storage quota / privacy mode errors
    }
  }

  function loadVolumePrefs() {
    try {
      const raw = localStorage.getItem(VOLUME_STORAGE_KEY);
      if (!raw) return { volume: 1, muted: false };
      const obj = JSON.parse(raw);
      const vol = typeof obj?.volume === "number" && obj.volume >= 0 && obj.volume <= 1 ? obj.volume : 1;
      const muted = Boolean(obj?.muted);
      return { volume: vol, muted };
    } catch {
      return { volume: 1, muted: false };
    }
  }

  function saveVolumePrefs(volume, muted) {
    try {
      localStorage.setItem(VOLUME_STORAGE_KEY, JSON.stringify({ volume, muted }));
    } catch {
      // Ignore storage errors
    }
  }

  function toMedalFsUrl(filePath) {
    if (!filePath) return "";
    return "medal-fs://file/" + encodeURIComponent(filePath);
  }

  function toFileUrl(filePath) {
    if (!filePath) return "";
    const normalized = String(filePath).replace(/\\/g, "/");
    const parts = normalized.split("/");
    const encoded = parts.map((seg, i) => (i === 0 && /^[a-zA-Z]:$/.test(seg)) ? seg : encodeURIComponent(seg));
    return "file:///" + encoded.join("/");
  }

  function formatDate(unixSec) {
    try {
      return new Date(unixSec * 1000).toLocaleDateString("de-DE", {
        day: "2-digit",
        month: "2-digit",
        year: "numeric"
      });
    } catch {
      return "";
    }
  }

  function getLocalDayKey() {
    const d = new Date();
    const m = String(d.getMonth() + 1).padStart(2, "0");
    const day = String(d.getDate()).padStart(2, "0");
    return `${d.getFullYear()}-${m}-${day}`;
  }

  async function loadThrowbacks(windowDays) {
    const todayKey = getLocalDayKey();
    if (cachedClips && cachedDayKey === todayKey) {
      return cachedClips;
    }
    if (fetching) return cachedClips || [];
    fetching = true;

    try {
      const now = new Date();
      const results = [];
      const seenRemote = new Set();
      const seenLocal = new Set();

      for (let yearsAgo = 1; yearsAgo <= CFG_MAX_YEARS; yearsAgo++) {
        const anchor = new Date(now);
        anchor.setFullYear(now.getFullYear() - yearsAgo);

        const from = new Date(anchor);
        from.setDate(anchor.getDate() - windowDays);
        from.setHours(0, 0, 0, 0);

        const to = new Date(anchor);
        to.setDate(anchor.getDate() + windowDays);
        to.setHours(23, 59, 59, 999);

        const res = await electron.ipcRenderer.invoke("contents:getContents", {
          createdFromSeconds: Math.floor(from.getTime() / 1000),
          createdToSeconds: Math.floor(to.getTime() / 1000),
          limit: 30,
          sortOrder: "newest"
        });

        const list = Array.isArray(res) ? res : (res?.contents || []);
        for (const c of list) {
          if (!c || !c.remote_content_id) continue;

          let meta = c.metadata;
          if (typeof meta === "string") {
            try { meta = JSON.parse(meta); } catch { meta = {}; }
          }
          const rc = meta?.remoteContent;
          const isUploaded = Boolean(
            rc?.contentUrl || rc?.contentUrlHls || rc?.publishedAt || rc?.contentUploadedAt || meta?.uploadedAt
          );
          if (!isUploaded) continue;

          if (seenRemote.has(c.remote_content_id) || seenLocal.has(c.local_content_id)) continue;
          seenRemote.add(c.remote_content_id);
          seenLocal.add(c.local_content_id);

          const slug = rc?.category?.slug || "";
          const shareUrl = slug
            ? `https://medal.tv/games/${slug}/clips/${c.remote_content_id}`
            : `https://medal.tv/clips/${c.remote_content_id}`;

          const qualities = [];
          if (rc?.contentUrl) qualities.push({ label: "Original", url: rc.contentUrl });
          if (rc?.contentPreview1080p) qualities.push({ label: "1080p", url: rc.contentPreview1080p });
          if (rc?.contentPreview720p) qualities.push({ label: "720p", url: rc.contentPreview720p });
          if (rc?.contentPreview480p) qualities.push({ label: "480p", url: rc.contentPreview480p });
          if (rc?.contentPreview360p) qualities.push({ label: "360p", url: rc.contentPreview360p });
          if (c.video_path) qualities.push({ label: "Lokal", url: toFileUrl(c.video_path) });
          if (!qualities.length) continue;

          results.push({
            id: c.remote_content_id,
            remoteId: c.remote_content_id,
            title: rc?.contentTitle || meta?.title || "Clip",
            game: rc?.category?.categoryName || "",
            createdAt: c.created_at,
            yearsAgo,
            qualities,
            cloudUrl: rc?.contentUrl || "",
            localPath: c.video_path || "",
            thumbPath: c.thumbnail_path || "",
            cloudThumbUrl: rc?.thumbnailUrl || "",
            shareUrl
          });
        }
      }

      results.sort((a, b) => a.yearsAgo - b.yearsAgo || b.createdAt - a.createdAt);
      cachedClips = results;
      cachedDayKey = todayKey;
      console.info("[reMedal] Loaded uploaded throwback clips:", results.length);
      return results;
    } catch (err) {
      console.warn("[reMedal] Failed to load throwbacks:", err);
      return [];
    } finally {
      fetching = false;
    }
  }

  // --- Client-side ad hiding (DOM & webview/iframe only, never modifies entitlement flags) ---
  function isAdUrl(src) {
    if (!src) return false;
    const low = String(src).toLowerCase();
    return AD_URL_PATTERNS.some((p) => low.includes(p));
  }

  function applyAdBlockCss() {
    if (!CFG_BLOCK_ADS || document.getElementById(ADBLOCK_STYLE_ID) || !document.head) return;

    const selectors = AD_URL_PATTERNS.flatMap((p) => [
      `webview[src*="${p}"]`,
      `iframe[src*="${p}"]`
    ]).join(",\n");

    const style = document.createElement("style");
    style.id = ADBLOCK_STYLE_ID;
    style.textContent = [
      `${selectors} { display: none !important; width: 0 !important; height: 0 !important; pointer-events: none !important; }`,
      ".remedal-ad-tile { position: relative !important; }",
      ".remedal-ad-hover { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 6px; pointer-events: none; opacity: 0; transition: opacity 0.18s ease; z-index: 2; }",
      ".remedal-ad-tile:hover .remedal-ad-hover { opacity: 0.88; }"
    ].join("\n");
    document.head.appendChild(style);
  }

  const AD_ATTR_RE = /(?:^|[-_\s])(ad|ads|advert|advertisement|adbox|adslot|adunit|adbanner|adcontainer|playwire|nitropay)(?:$|[-_\s])/i;
  const AD_TEXT_RE = /^(ad|ads|advertisement|werbung|sponsored|report ads?|remove ads?|werbung melden|werbung entfernen|go ad[- ]?free|upgrade.*ads?|hide ads?|×|x|\s)*$/i;
  const AD_LABEL_EXACT_RE = /^(report ads?|remove ads?|werbung melden|werbung entfernen)$/i;

  function collapseToSpacer(node) {
    node.style.setProperty("height", "24px", "important");
    node.style.setProperty("min-height", "24px", "important");
    node.style.setProperty("max-height", "24px", "important");
    node.style.setProperty("margin", "0", "important");
    node.style.setProperty("padding", "0", "important");
    node.style.setProperty("background", "transparent", "important");
    node.style.setProperty("border", "none", "important");
    node.style.setProperty("box-shadow", "none", "important");
    node.style.setProperty("overflow", "hidden", "important");
    node.style.setProperty("pointer-events", "none", "important");
    for (const ch of node.children) {
      ch.style.setProperty("display", "none", "important");
    }
  }

  function isSafeAdWrapper(node, childNode) {
    if (!node || node.id === "root") return false;
    const tag = node.tagName;
    if (tag === "BODY" || tag === "HTML" || tag === "MAIN" || tag === "NAV" || tag === "HEADER") return false;
    if (node.querySelector("video, canvas, input, textarea, [data-testid='story-avatar-card']")) return false;
    // Stop if node contains any real app image (ignore our own hover icon)
    const imgs = node.querySelectorAll("img:not(.remedal-ad-icon)");
    if (imgs.length > 0) return false;

    const attrText = `${node.id || ""} ${typeof node.className === "string" ? node.className : ""} ${node.getAttribute("data-testid") || ""}`;
    if (AD_ATTR_RE.test(attrText) && (node.textContent || "").trim().length <= 120) return true;

    const otherVisible = Array.from(node.children).filter(
      (ch) => ch !== childNode && !ch.classList?.contains("remedal-ad-hover") && ch.tagName !== "STYLE" && ch.tagName !== "SCRIPT" && ch.style.display !== "none"
    );
    if (otherVisible.length === 0) return true;

    const txt = (node.textContent || "").trim();
    return txt.length <= 80 && AD_TEXT_RE.test(txt);
  }

  function findAdTileRoot(el) {
    let best = null;
    let child = el;
    let cur = el.parentElement;
    for (let depth = 0; depth < 7 && cur; depth++) {
      if (!isSafeAdWrapper(cur, child)) break;
      best = cur;
      child = cur;
      cur = cur.parentElement;
    }
    return best;
  }

  function isClipGridAdTile(tile) {
    let container = tile?.parentElement;
    for (let i = 0; i < 2 && container; i++) {
      const style = getComputedStyle(container);
      const isRowOrGrid = style.display === "grid" || (style.display === "flex" && !style.flexDirection.startsWith("column"));
      if (isRowOrGrid) {
        const clipImgs = container.querySelectorAll("img:not(.remedal-ad-icon)");
        if (clipImgs.length >= 2) return true;
      }
      container = container.parentElement;
    }
    return false;
  }

  function attachHoverBadge(tile) {
    if (!tile || !CFG_ICON_URI) return;
    tile.classList.add("remedal-ad-tile");
    if (tile.querySelector(".remedal-ad-hover")) return;
    const badge = document.createElement("div");
    badge.className = "remedal-ad-hover";
    const icon = document.createElement("img");
    icon.className = "remedal-ad-icon";
    icon.src = CFG_ICON_URI;
    icon.alt = "reMedal";
    icon.style.cssText = "width:42px;height:42px;border-radius:10px;filter:drop-shadow(0 2px 8px rgba(0,0,0,0.65));";
    const lbl = document.createElement("span");
    lbl.textContent = "No Ads";
    lbl.style.cssText = "color:rgba(255,255,255,0.72);font-size:12px;font-weight:600;letter-spacing:0.03em;font-family:system-ui,-apple-system,Segoe UI,sans-serif;";
    badge.appendChild(icon);
    badge.appendChild(lbl);
    tile.appendChild(badge);
  }

  function handleAdContainer(el) {
    const tile = findAdTileRoot(el);
    if (!tile) return;
    if (isClipGridAdTile(tile)) {
      attachHoverBadge(tile);
    } else {
      collapseToSpacer(tile);
    }
  }

  function neutralizeAdElement(el) {
    if (!CFG_BLOCK_ADS || !el || (el.tagName !== "WEBVIEW" && el.tagName !== "IFRAME")) return;
    const src = el.getAttribute("src") || el.src || "";
    if (!isAdUrl(src)) return;

    try {
      el.setAttribute("src", "about:blank");
      el.src = "about:blank";
    } catch {
      // Ignore read-only property errors
    }
    el.style.setProperty("display", "none", "important");
    handleAdContainer(el);
  }

  function sweepAdElements(rootNode) {
    if (!CFG_BLOCK_ADS) return;
    applyAdBlockCss();

    if (!rootNode || typeof rootNode.querySelectorAll !== "function") return;
    if (rootNode.tagName === "WEBVIEW" || rootNode.tagName === "IFRAME") {
      neutralizeAdElement(rootNode);
      return;
    }
    const list = rootNode.querySelectorAll("webview, iframe");
    for (const el of list) neutralizeAdElement(el);

    const controls = rootNode.querySelectorAll("button, a, span");
    for (const ctrl of controls) {
      if (ctrl.children.length > 1) continue;
      const txt = (ctrl.textContent || "").trim();
      if (txt.length >= 8 && txt.length <= 24 && AD_LABEL_EXACT_RE.test(txt)) {
        ctrl.style.setProperty("display", "none", "important");
        handleAdContainer(ctrl);
      }
    }
  }

  // --- Fullscreen Story Viewer Modal with Medal-style Controls ---
  function openStoryModal(clips, startIndex) {
    const existing = document.getElementById(MODAL_ID);
    if (existing) existing.remove();

    const playable = clips.slice();
    let idx = 0;
    if (typeof startIndex === "number") {
      idx = Math.max(0, Math.min(startIndex, playable.length - 1));
    } else {
      const watched = getWatchedSet();
      const firstUnwatched = playable.findIndex((c) => !watched.has(c.id));
      idx = firstUnwatched >= 0 ? firstUnwatched : 0;
    }

    const volPrefs = loadVolumePrefs();
    let isMuted = volPrefs.muted;
    let currentVol = volPrefs.volume;
    let currentRate = 1;

    const overlay = document.createElement("div");
    overlay.id = MODAL_ID;
    overlay.style.cssText = [
      "position:fixed;inset:0;z-index:2147483647;background:rgba(5,7,11,0.94);",
      "display:flex;align-items:center;justify-content:center;backdrop-filter:blur(10px);",
      "font-family:system-ui,-apple-system,Segoe UI,sans-serif;user-select:none;"
    ].join("");

    const card = document.createElement("div");
    card.style.cssText = [
      "position:relative;width:min(1080px,94vw);height:min(86vh,760px);",
      "background:#0b0e14;border:1px solid rgba(255,255,255,0.12);border-radius:18px;",
      "overflow:hidden;box-shadow:0 28px 80px rgba(0,0,0,0.78);display:flex;flex-direction:column;"
    ].join("");

    // Top Story segments + year badge + close
    const topOverlay = document.createElement("div");
    topOverlay.style.cssText = [
      "position:absolute;top:0;left:0;right:0;z-index:5;padding:12px 16px 24px 16px;",
      "background:linear-gradient(180deg,rgba(0,0,0,0.78) 0%,rgba(0,0,0,0) 100%);pointer-events:none;",
      "opacity:1;transition:opacity 0.25s ease;"
    ].join("");

    const progressRow = document.createElement("div");
    progressRow.style.cssText = "display:flex;gap:5px;margin-bottom:10px;pointer-events:auto;";
    const barTracks = [];
    const barFills = [];
    for (let i = 0; i < playable.length; i++) {
      const track = document.createElement("div");
      track.style.cssText = "flex:1;height:3px;background:rgba(255,255,255,0.24);border-radius:99px;overflow:hidden;cursor:pointer;";
      track.onclick = (e) => {
        e.stopPropagation();
        const targetIdx = barTracks.indexOf(track);
        if (targetIdx >= 0) loadClip(targetIdx);
      };
      const fill = document.createElement("div");
      fill.style.cssText = "height:100%;width:0%;background:#facc15;will-change:width;";
      track.appendChild(fill);
      progressRow.appendChild(track);
      barTracks.push(track);
      barFills.push(fill);
    }

    const headerRow = document.createElement("div");
    headerRow.style.cssText = "display:flex;align-items:center;justify-content:space-between;pointer-events:auto;";

    const pill = document.createElement("span");
    pill.style.cssText = "background:#facc15;color:#0f172a;font-size:11px;font-weight:800;padding:3px 9px;border-radius:999px;white-space:nowrap;";

    const rightHeader = document.createElement("div");
    rightHeader.style.cssText = "display:flex;align-items:center;gap:8px;";

    const countBadge = document.createElement("span");
    countBadge.style.cssText = "color:#e2e8f0;font-size:12px;font-weight:600;";

    const svgIcon = (body, size = 16, filled = false) =>
      `<svg viewBox="0 0 24 24" width="${size}" height="${size}" ${filled ? 'fill="currentColor"' : 'fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"'} style="display:block;flex-shrink:0;">${body}</svg>`;
    const ICON_PLAY = svgIcon('<path d="M8 5.14v14l11-7-11-7z"/>', 17, true);
    const ICON_PAUSE = svgIcon('<path d="M6 5h4v14H6zm8 0h4v14h-4z"/>', 17, true);
    const ICON_VOL_ON = svgIcon('<polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"/><path d="M15.54 8.46a5 5 0 0 1 0 7.07"/><path d="M19.07 4.93a10 10 0 0 1 0 14.14"/>', 17);
    const ICON_VOL_OFF = svgIcon('<polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"/><line x1="23" y1="9" x2="17" y2="15"/><line x1="17" y1="9" x2="23" y2="15"/>', 17);
    const ICON_LINK = svgIcon('<path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/>', 15);
    const ICON_CHECK = svgIcon('<polyline points="20 6 9 17 4 12"/>', 15);
    const ICON_EXT = svgIcon('<path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/>', 16);
    const ICON_GEAR = svgIcon('<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/>', 16);
    const ICON_FS = svgIcon('<path d="M8 3H5a2 2 0 0 0-2 2v3m18 0V5a2 2 0 0 0-2-2h-3m0 18h3a2 2 0 0 0 2-2v-3M3 16v3a2 2 0 0 0 2 2h3"/>', 16);
    const ICON_CLOSE = svgIcon('<line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>', 16);
    const ICON_PREV = svgIcon('<polyline points="15 18 9 12 15 6"/>', 20);
    const ICON_NEXT = svgIcon('<polyline points="9 18 15 12 9 6"/>', 20);

    const closeBtn = document.createElement("button");
    closeBtn.style.cssText = "background:rgba(15,23,42,0.75);border:1px solid rgba(255,255,255,0.18);color:#f8fafc;width:32px;height:32px;border-radius:8px;cursor:pointer;display:inline-flex;align-items:center;justify-content:center;";
    closeBtn.innerHTML = ICON_CLOSE;

    rightHeader.appendChild(countBadge);
    rightHeader.appendChild(closeBtn);
    headerRow.appendChild(pill);
    headerRow.appendChild(rightHeader);
    topOverlay.appendChild(progressRow);
    topOverlay.appendChild(headerRow);

    // Stage
    const stage = document.createElement("div");
    stage.style.cssText = "position:relative;flex:1;min-height:0;background:#000;display:flex;align-items:center;justify-content:center;overflow:hidden;";

    const video = document.createElement("video");
    video.controls = false;
    video.autoplay = true;
    video.playsInline = true;
    video.style.cssText = "width:100%;height:100%;object-fit:contain;display:block;cursor:pointer;";

    const emptyMsg = document.createElement("div");
    emptyMsg.style.cssText = "display:none;color:#cbd5e1;font-size:14px;text-align:center;padding:24px;";
    emptyMsg.textContent = "Keine abspielbaren hochgeladenen Clips gefunden.";

    if (!document.getElementById("remedal-spinner-css")) {
      const spinStyle = document.createElement("style");
      spinStyle.id = "remedal-spinner-css";
      spinStyle.textContent = "@keyframes remedal-spin{to{transform:rotate(360deg)}}";
      (document.head || document.documentElement).appendChild(spinStyle);
    }
    const spinnerOverlay = document.createElement("div");
    spinnerOverlay.style.cssText = "position:absolute;inset:0;display:flex;align-items:center;justify-content:center;pointer-events:none;z-index:4;";
    const spinnerRing = document.createElement("div");
    spinnerRing.style.cssText = "width:38px;height:38px;border:3px solid rgba(255,255,255,0.22);border-top-color:#ffffff;border-radius:50%;animation:remedal-spin 0.75s linear infinite;";
    spinnerOverlay.appendChild(spinnerRing);
    const setLoading = (on) => { spinnerOverlay.style.display = on ? "flex" : "none"; };

    const prevZone = document.createElement("div");
    prevZone.title = "Vorheriger Clip (←)";
    prevZone.style.cssText = "position:absolute;left:0;top:64px;bottom:96px;width:14%;z-index:3;cursor:pointer;display:flex;align-items:center;padding-left:14px;";
    const prevHint = document.createElement("span");
    prevHint.innerHTML = ICON_PREV;
    prevHint.style.cssText = "width:38px;height:38px;border-radius:999px;background:rgba(0,0,0,0.52);color:#fff;display:flex;align-items:center;justify-content:center;opacity:0;transition:opacity 0.15s;";
    prevZone.appendChild(prevHint);
    prevZone.onmouseenter = () => { if (idx > 0) prevHint.style.opacity = "1"; };
    prevZone.onmouseleave = () => { prevHint.style.opacity = "0"; };

    const nextZone = document.createElement("div");
    nextZone.title = "Nächster Clip (→)";
    nextZone.style.cssText = "position:absolute;right:0;top:64px;bottom:96px;width:14%;z-index:3;cursor:pointer;display:flex;align-items:center;justify-content:flex-end;padding-right:14px;";
    const nextHint = document.createElement("span");
    nextHint.innerHTML = ICON_NEXT;
    nextHint.style.cssText = "width:38px;height:38px;border-radius:999px;background:rgba(0,0,0,0.52);color:#fff;display:flex;align-items:center;justify-content:center;opacity:0;transition:opacity 0.15s;";
    nextZone.appendChild(nextHint);
    nextZone.onmouseenter = () => { if (idx < playable.length - 1) nextHint.style.opacity = "1"; };
    nextZone.onmouseleave = () => { nextHint.style.opacity = "0"; };

    // Sleek Medal-style bottom control overlay
    const ctrlOverlay = document.createElement("div");
    ctrlOverlay.style.cssText = [
      "position:absolute;left:0;right:0;bottom:0;z-index:6;padding:32px 16px 12px 16px;",
      "background:linear-gradient(180deg,rgba(0,0,0,0) 0%,rgba(0,0,0,0.58) 45%,rgba(0,0,0,0.9) 100%);",
      "display:flex;flex-direction:column;gap:6px;opacity:1;transition:opacity 0.25s ease;"
    ].join("");
    ctrlOverlay.onclick = (e) => e.stopPropagation();

    const titleRow = document.createElement("div");
    titleRow.style.cssText = "display:flex;align-items:baseline;justify-content:space-between;gap:12px;";
    const ctrlTitle = document.createElement("div");
    ctrlTitle.style.cssText = "color:#ffffff;font-size:14px;font-weight:700;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;text-shadow:0 1px 3px rgba(0,0,0,0.7);";
    const ctrlSub = document.createElement("div");
    ctrlSub.style.cssText = "color:rgba(255,255,255,0.76);font-size:12px;white-space:nowrap;flex-shrink:0;text-shadow:0 1px 3px rgba(0,0,0,0.7);";
    titleRow.appendChild(ctrlTitle);
    titleRow.appendChild(ctrlSub);

    // Custom white smooth seek bar (Medal style)
    const seekHit = document.createElement("div");
    seekHit.style.cssText = "position:relative;height:16px;display:flex;align-items:center;cursor:pointer;user-select:none;";
    const seekTrack = document.createElement("div");
    seekTrack.style.cssText = "position:relative;width:100%;height:3px;background:rgba(255,255,255,0.28);border-radius:99px;transition:height 0.12s ease;";
    const seekFill = document.createElement("div");
    seekFill.style.cssText = "position:absolute;left:0;top:0;bottom:0;width:0%;background:#ffffff;border-radius:99px;pointer-events:none;will-change:width;";
    const seekThumb = document.createElement("div");
    seekThumb.style.cssText = "position:absolute;top:50%;left:0%;width:12px;height:12px;border-radius:50%;background:#ffffff;transform:translate(-50%,-50%) scale(0);transition:transform 0.12s ease;box-shadow:0 1px 4px rgba(0,0,0,0.55);pointer-events:none;will-change:left;";
    seekTrack.appendChild(seekFill);
    seekTrack.appendChild(seekThumb);
    seekHit.appendChild(seekTrack);

    // Controls row
    const ctrlRow = document.createElement("div");
    ctrlRow.style.cssText = "display:flex;align-items:center;justify-content:space-between;gap:10px;";

    const leftCtrls = document.createElement("div");
    leftCtrls.style.cssText = "display:flex;align-items:center;gap:10px;";

    const iconBtnCss = "background:transparent;border:none;color:#ffffff;width:30px;height:30px;padding:0;border-radius:6px;cursor:pointer;font-size:15px;display:inline-flex;align-items:center;justify-content:center;opacity:0.9;";
    const selectCss = "background:#141822;border:1px solid rgba(255,255,255,0.18);color:#f8fafc;height:28px;padding:0 8px;border-radius:6px;cursor:pointer;font-size:12px;font-weight:600;width:100%;";

    const playPauseBtn = document.createElement("button");
    playPauseBtn.title = "Play / Pause (Space)";
    playPauseBtn.style.cssText = iconBtnCss;
    playPauseBtn.innerHTML = ICON_PAUSE;

    const timeLabel = document.createElement("span");
    timeLabel.style.cssText = "color:rgba(255,255,255,0.92);font-size:12px;font-weight:600;font-variant-numeric:tabular-nums;letter-spacing:0.02em;";
    timeLabel.textContent = "00:00 / 00:00";

    const muteBtn = document.createElement("button");
    muteBtn.title = "Ton an/aus (M)";
    muteBtn.style.cssText = iconBtnCss;
    muteBtn.innerHTML = (isMuted || currentVol === 0) ? ICON_VOL_OFF : ICON_VOL_ON;

    const volSlider = document.createElement("input");
    volSlider.type = "range";
    volSlider.min = "0";
    volSlider.max = "1";
    volSlider.step = "0.02";
    volSlider.value = String(currentVol);
    volSlider.style.cssText = "width:68px;height:3px;accent-color:#ffffff;cursor:pointer;";

    leftCtrls.appendChild(playPauseBtn);
    leftCtrls.appendChild(timeLabel);
    leftCtrls.appendChild(muteBtn);
    leftCtrls.appendChild(volSlider);

    const rightCtrls = document.createElement("div");
    rightCtrls.style.cssText = "display:flex;align-items:center;gap:6px;position:relative;";

    const copyLinkBtn = document.createElement("button");
    copyLinkBtn.title = "Link teilen / kopieren";
    copyLinkBtn.style.cssText = "background:rgba(255,255,255,0.16);border:1px solid rgba(255,255,255,0.24);color:#ffffff;height:32px;padding:0 13px;border-radius:8px;cursor:pointer;font-size:12px;font-weight:700;display:inline-flex;align-items:center;gap:6px;white-space:nowrap;";
    copyLinkBtn.innerHTML = `${ICON_LINK}<span>Link teilen</span>`;

    const openMedalBtn = document.createElement("button");
    openMedalBtn.title = "In Medal öffnen";
    openMedalBtn.style.cssText = iconBtnCss;
    openMedalBtn.innerHTML = ICON_EXT;

    const settingsBtn = document.createElement("button");
    settingsBtn.title = "Einstellungen";
    settingsBtn.style.cssText = iconBtnCss;
    settingsBtn.innerHTML = ICON_GEAR;

    const fsBtn = document.createElement("button");
    fsBtn.title = "Vollbild (F)";
    fsBtn.style.cssText = iconBtnCss;
    fsBtn.innerHTML = ICON_FS;

    // Settings popover (Quality + Speed)
    const settingsPopover = document.createElement("div");
    settingsPopover.style.cssText = [
      "position:absolute;bottom:40px;right:0;background:rgba(14,17,24,0.96);",
      "border:1px solid rgba(255,255,255,0.16);border-radius:10px;padding:10px 12px;",
      "display:none;flex-direction:column;gap:8px;min-width:172px;z-index:8;",
      "box-shadow:0 8px 24px rgba(0,0,0,0.55);"
    ].join("");
    settingsPopover.onclick = (e) => e.stopPropagation();

    const qWrap = document.createElement("div");
    qWrap.style.cssText = "display:flex;flex-direction:column;gap:4px;";
    const qLbl = document.createElement("span");
    qLbl.textContent = "Qualität";
    qLbl.style.cssText = "color:rgba(255,255,255,0.7);font-size:11px;font-weight:600;";
    const qualitySelect = document.createElement("select");
    qualitySelect.style.cssText = selectCss;
    qWrap.appendChild(qLbl);
    qWrap.appendChild(qualitySelect);

    const sWrap = document.createElement("div");
    sWrap.style.cssText = "display:flex;flex-direction:column;gap:4px;";
    const sLbl = document.createElement("span");
    sLbl.textContent = "Geschwindigkeit";
    sLbl.style.cssText = "color:rgba(255,255,255,0.7);font-size:11px;font-weight:600;";
    const speedSelect = document.createElement("select");
    speedSelect.style.cssText = selectCss;
    for (const sp of [0.5, 1, 1.5, 2]) {
      const opt = document.createElement("option");
      opt.value = String(sp);
      opt.textContent = `${sp}x`;
      if (sp === 1) opt.selected = true;
      speedSelect.appendChild(opt);
    }
    sWrap.appendChild(sLbl);
    sWrap.appendChild(speedSelect);

    settingsPopover.appendChild(qWrap);
    settingsPopover.appendChild(sWrap);

    rightCtrls.appendChild(copyLinkBtn);
    rightCtrls.appendChild(openMedalBtn);
    rightCtrls.appendChild(settingsBtn);
    rightCtrls.appendChild(fsBtn);
    rightCtrls.appendChild(settingsPopover);

    ctrlRow.appendChild(leftCtrls);
    ctrlRow.appendChild(rightCtrls);

    ctrlOverlay.appendChild(titleRow);
    ctrlOverlay.appendChild(seekHit);
    ctrlOverlay.appendChild(ctrlRow);

    stage.appendChild(video);
    stage.appendChild(spinnerOverlay);
    stage.appendChild(emptyMsg);
    stage.appendChild(prevZone);
    stage.appendChild(nextZone);
    stage.appendChild(ctrlOverlay);

    card.appendChild(stage);
    card.appendChild(topOverlay);
    overlay.appendChild(card);

    let rafId = 0;
    let isSeeking = false;
    let isHoveringSeek = false;
    let isHoveringCtrls = false;
    let settingsOpen = false;
    let idleTimer = null;

    function fmtTime(sec) {
      if (!Number.isFinite(sec) || sec < 0) return "00:00";
      const m = Math.floor(sec / 60);
      const s = Math.floor(sec % 60);
      return `${m < 10 ? "0" : ""}${m}:${s < 10 ? "0" : ""}${s}`;
    }

    function updateSeekVisuals() {
      const expanded = isHoveringSeek || isSeeking;
      seekTrack.style.height = expanded ? "5px" : "3px";
      seekThumb.style.transform = expanded ? "translate(-50%,-50%) scale(1)" : "translate(-50%,-50%) scale(0)";
    }

    function syncProgress(overrideRatio) {
      const dur = video.duration;
      if (!Number.isFinite(dur) || dur <= 0) return;
      const ratio = typeof overrideRatio === "number"
        ? Math.max(0, Math.min(1, overrideRatio))
        : Math.max(0, Math.min(1, video.currentTime / dur));
      const pct = (ratio * 100).toFixed(3) + "%";
      if (barFills[idx]) barFills[idx].style.width = pct;
      seekFill.style.width = pct;
      seekThumb.style.left = pct;
      timeLabel.textContent = `${fmtTime(ratio * dur)} / ${fmtTime(dur)}`;
    }

    function stopProgressLoop() {
      if (rafId) {
        cancelAnimationFrame(rafId);
        rafId = 0;
      }
    }

    function startProgressLoop() {
      stopProgressLoop();
      const tick = () => {
        if (!isSeeking) syncProgress();
        if (!video.paused && !video.ended) {
          rafId = requestAnimationFrame(tick);
        } else {
          rafId = 0;
        }
      };
      rafId = requestAnimationFrame(tick);
    }

    function showControlsTemporarily() {
      ctrlOverlay.style.opacity = "1";
      topOverlay.style.opacity = "1";
      if (idleTimer) clearTimeout(idleTimer);
      idleTimer = setTimeout(() => {
        if (!video.paused && !isHoveringCtrls && !isSeeking && !settingsOpen) {
          ctrlOverlay.style.opacity = "0";
          topOverlay.style.opacity = "0";
        }
      }, 2200);
    }

    function populateQualities(clip) {
      qualitySelect.innerHTML = "";
      for (let i = 0; i < clip.qualities.length; i++) {
        const q = clip.qualities[i];
        const opt = document.createElement("option");
        opt.value = String(i);
        opt.textContent = q.label;
        qualitySelect.appendChild(opt);
      }
      qualitySelect.value = "0";
      qWrap.style.display = clip.qualities.length > 0 ? "flex" : "none";
    }

    function loadClip(newIdx) {
      if (!playable.length) {
        stopProgressLoop();
        video.style.display = "none";
        emptyMsg.style.display = "block";
        pill.textContent = "RÜCKBLICK";
        ctrlTitle.textContent = "Keine abspielbaren Clips gefunden";
        ctrlSub.textContent = "";
        countBadge.textContent = "0 / 0";
        return;
      }
      if (newIdx < 0) newIdx = 0;
      if (newIdx >= playable.length) {
        closeModal();
        return;
      }
      idx = newIdx;
      const clip = playable[idx];

      for (let i = 0; i < barFills.length; i++) {
        barFills[i].style.width = i < idx ? "100%" : "0%";
      }

      pill.textContent = clip.yearsAgo === 1 ? "VOR 1 JAHR" : `VOR ${clip.yearsAgo} JAHREN`;
      ctrlTitle.textContent = clip.title;
      ctrlSub.textContent = `${formatDate(clip.createdAt)}${clip.game ? " • " + clip.game : ""}`;
      countBadge.textContent = `${idx + 1} / ${playable.length}`;
      timeLabel.textContent = "00:00 / 00:00";
      seekFill.style.width = "0%";
      seekThumb.style.left = "0%";
      copyLinkBtn.innerHTML = `${ICON_LINK}<span>Link teilen</span>`;
      settingsOpen = false;
      settingsPopover.style.display = "none";

      populateQualities(clip);
      video.dataset.qIdx = "0";
      video.dataset.watchedMarked = "";
      setLoading(true);
      video.src = clip.qualities[0].url;
      video.muted = isMuted;
      video.volume = currentVol;
      video.playbackRate = currentRate;
      video.play().catch(() => {});
      playPauseBtn.innerHTML = ICON_PAUSE;
      showControlsTemporarily();
    }

    function togglePlay() {
      settingsOpen = false;
      settingsPopover.style.display = "none";
      if (video.paused) {
        video.play().catch(() => {});
      } else {
        video.pause();
      }
      showControlsTemporarily();
    }

    function ratioFromPointer(clientX) {
      const rect = seekHit.getBoundingClientRect();
      if (rect.width <= 0) return 0;
      return Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
    }

    function onSeekMove(e) {
      if (!isSeeking) return;
      const r = ratioFromPointer(e.clientX);
      syncProgress(r);
      if (Number.isFinite(video.duration) && video.duration > 0) {
        video.currentTime = r * video.duration;
      }
    }

    function onSeekUp() {
      if (!isSeeking) return;
      isSeeking = false;
      updateSeekVisuals();
      showControlsTemporarily();
      if (!video.paused) startProgressLoop();
    }

    function closeModal() {
      stopProgressLoop();
      if (idleTimer) clearTimeout(idleTimer);
      window.removeEventListener("mousemove", onSeekMove);
      window.removeEventListener("mouseup", onSeekUp);
      if (document.fullscreenElement === card) {
        document.exitFullscreen().catch(() => {});
      }
      video.pause();
      video.removeAttribute("src");
      video.load();
      window.removeEventListener("keydown", onKey);
      overlay.remove();
    }

    seekHit.onmouseenter = () => { isHoveringSeek = true; updateSeekVisuals(); };
    seekHit.onmouseleave = () => { isHoveringSeek = false; updateSeekVisuals(); };
    seekHit.onmousedown = (e) => {
      e.preventDefault();
      e.stopPropagation();
      isSeeking = true;
      updateSeekVisuals();
      const r = ratioFromPointer(e.clientX);
      syncProgress(r);
      if (Number.isFinite(video.duration) && video.duration > 0) {
        video.currentTime = r * video.duration;
      }
    };
    window.addEventListener("mousemove", onSeekMove);
    window.addEventListener("mouseup", onSeekUp);

    ctrlOverlay.onmouseenter = () => { isHoveringCtrls = true; showControlsTemporarily(); };
    ctrlOverlay.onmouseleave = () => { isHoveringCtrls = false; showControlsTemporarily(); };
    stage.onmousemove = showControlsTemporarily;

    settingsBtn.onclick = (e) => {
      e.stopPropagation();
      settingsOpen = !settingsOpen;
      settingsPopover.style.display = settingsOpen ? "flex" : "none";
      showControlsTemporarily();
    };

    video.addEventListener("play", () => {
      playPauseBtn.innerHTML = ICON_PAUSE;
      startProgressLoop();
      showControlsTemporarily();
    });
    video.addEventListener("pause", () => {
      playPauseBtn.innerHTML = ICON_PLAY;
      stopProgressLoop();
      syncProgress();
      showControlsTemporarily();
    });
    video.addEventListener("waiting", () => setLoading(true));
    video.addEventListener("loadstart", () => setLoading(true));
    video.addEventListener("canplay", () => setLoading(false));
    video.addEventListener("loadeddata", () => setLoading(false));
    video.addEventListener("playing", () => {
      setLoading(false);
      startProgressLoop();
      const current = playable[idx];
      if (current && !video.dataset.watchedMarked) {
        video.dataset.watchedMarked = "1";
        markWatched(current.id);
        refreshStoryCardStyle(clips);
      }
    });
    video.addEventListener("loadedmetadata", () => syncProgress());
    video.addEventListener("seeked", () => syncProgress());

    video.addEventListener("error", () => {
      if (!playable.length) return;
      const current = playable[idx];
      const nextQ = Number(video.dataset.qIdx || "0") + 1;
      if (current && nextQ < current.qualities.length) {
        video.dataset.qIdx = String(nextQ);
        qualitySelect.value = String(nextQ);
        video.src = current.qualities[nextQ].url;
        video.play().catch(() => {});
        return;
      }

      console.warn("[reMedal] Skipping unplayable clip:", current?.remoteId);
      playable.splice(idx, 1);
      if (idx < barTracks.length) {
        const track = barTracks.splice(idx, 1)[0];
        barFills.splice(idx, 1);
        track.remove();
      }
      if (!playable.length) {
        setLoading(false);
        loadClip(0);
        return;
      }
      if (idx >= playable.length) {
        closeModal();
        return;
      }
      loadClip(idx);
    });

    video.addEventListener("timeupdate", () => { if (!isSeeking) syncProgress(); });
    video.addEventListener("ended", () => {
      stopProgressLoop();
      loadClip(idx + 1);
    });

    volSlider.addEventListener("input", () => {
      currentVol = Number(volSlider.value);
      video.volume = currentVol;
      isMuted = currentVol === 0;
      video.muted = isMuted;
      muteBtn.innerHTML = isMuted ? ICON_VOL_OFF : ICON_VOL_ON;
      saveVolumePrefs(currentVol, isMuted);
    });

    qualitySelect.addEventListener("change", () => {
      const clip = playable[idx];
      const qIdx = Number(qualitySelect.value);
      if (!clip || !clip.qualities[qIdx]) return;
      const t = video.currentTime;
      const wasPaused = video.paused;
      video.dataset.qIdx = String(qIdx);
      video.src = clip.qualities[qIdx].url;
      video.addEventListener("loadedmetadata", () => {
        if (Number.isFinite(t) && t > 0) video.currentTime = t;
        video.playbackRate = currentRate;
        if (!wasPaused) video.play().catch(() => {});
      }, { once: true });
    });

    speedSelect.addEventListener("change", () => {
      currentRate = Number(speedSelect.value) || 1;
      video.playbackRate = currentRate;
    });

    copyLinkBtn.onclick = async () => {
      const clip = playable[idx];
      if (!clip?.shareUrl) return;
      try {
        if (electron.clipboard?.writeText) {
          electron.clipboard.writeText(clip.shareUrl);
        } else if (navigator.clipboard?.writeText) {
          await navigator.clipboard.writeText(clip.shareUrl);
        }
        copyLinkBtn.innerHTML = `${ICON_CHECK}<span>Kopiert!</span>`;
        setTimeout(() => { copyLinkBtn.innerHTML = `${ICON_LINK}<span>Link teilen</span>`; }, 1800);
      } catch {
        copyLinkBtn.textContent = "Fehler";
      }
    };

    openMedalBtn.onclick = () => {
      const clip = playable[idx];
      if (clip?.shareUrl) window.open(clip.shareUrl, "_blank");
    };

    fsBtn.onclick = () => {
      if (document.fullscreenElement === card) {
        document.exitFullscreen().catch(() => {});
      } else {
        card.requestFullscreen?.().catch(() => {});
      }
    };

    video.onclick = togglePlay;
    prevZone.onclick = () => loadClip(idx - 1);
    nextZone.onclick = () => loadClip(idx + 1);
    playPauseBtn.onclick = togglePlay;
    muteBtn.onclick = () => {
      isMuted = !isMuted;
      video.muted = isMuted;
      muteBtn.innerHTML = isMuted ? ICON_VOL_OFF : ICON_VOL_ON;
      saveVolumePrefs(currentVol, isMuted);
    };
    closeBtn.onclick = closeModal;
    overlay.onclick = (e) => {
      if (e.target === overlay) closeModal();
    };

    function onKey(e) {
      if (e.key === "Escape") closeModal();
      else if (e.key === "ArrowRight") loadClip(idx + 1);
      else if (e.key === "ArrowLeft") loadClip(idx - 1);
      else if (e.key === " ") {
        e.preventDefault();
        togglePlay();
      }
    }
    window.addEventListener("keydown", onKey);

    document.body.appendChild(overlay);
    loadClip(idx);
  }

  // --- Seamless Medal Home integration (new 2648+ Home section + legacy Stories bar fallback) ---
  function findHomeSectionAnchor() {
    const targetTitles = ["friends", "freunde", "top 10s", "patch notes"];
    const matches = new Map();
    const maxLeft = Math.max(420, window.innerWidth * 0.55);

    for (const el of document.querySelectorAll("h1, h2, h3, h4, span, div")) {
      if (el.children.length > 0 || el.id === STORY_ITEM_ID || el.closest(`#${STORY_ITEM_ID}`)) continue;
      const txt = (el.textContent || "").trim().toLowerCase();
      if (!targetTitles.includes(txt) || matches.has(txt)) continue;
      const r = el.getBoundingClientRect();
      if (r.width <= 0 || r.height <= 0 || r.left > maxLeft) continue;

      let cur = el.parentElement;
      for (let depth = 0; depth < 6 && cur && cur !== document.body; depth++) {
        const cr = cur.getBoundingClientRect();
        const parent = cur.parentElement;
        if (cr.width >= 440 && cr.height >= 100 && parent && parent.children.length >= 2) {
          matches.set(txt, cur);
          break;
        }
        cur = parent;
      }
    }

    for (const title of targetTitles) {
      if (matches.has(title)) {
        return { el: matches.get(title), after: false, mode: "section" };
      }
    }
    return null;
  }

  function findStoryAnchor() {
    const own = document.querySelector('[data-testid="story-avatar-card"][data-own-story]');
    if (own) return { el: own, after: true, mode: "story" };
    const first = document.querySelector('[data-testid="story-avatar-card"]');
    if (first) return { el: first, after: false, mode: "story" };
    return findHomeSectionAnchor();
  }

  function measureNativeRingSize(anchorEl) {
    const candidates = [];
    const peer = document.querySelector('[data-testid="story-avatar-card"]:not([data-own-story])');
    if (peer) candidates.push(peer);
    if (anchorEl) candidates.push(anchorEl);
    for (const root of candidates) {
      let best = 0;
      for (const el of root.querySelectorAll("div, span")) {
        const r = el.getBoundingClientRect();
        if (r.width >= 72 && r.width <= 112 && Math.abs(r.width - r.height) <= 2) {
          const br = getComputedStyle(el).borderRadius;
          if (br && (br.includes("999") || br.includes("50%") || parseFloat(br) >= r.width * 0.45)) {
            if (r.width > best) best = Math.round(r.width);
          }
        }
      }
      if (best >= 72) return best;
    }
    return 88;
  }

  function applyStoryCardStyle(card, ring, label, clips) {
    const watched = getWatchedSet();
    const unwatchedCount = clips.filter((c) => !watched.has(c.id)).length;
    const hasUnwatched = unwatchedCount > 0;
    console.info("[reMedal] Story state:", { total: clips.length, unwatched: unwatchedCount });

    if (hasUnwatched) {
      card.style.opacity = "1";
      ring.style.background = "linear-gradient(135deg,#facc15,#f97316,#ec4899)";
      ring.style.boxShadow = "0 4px 12px rgba(250,204,21,0.32)";
      label.style.color = "#fde047";
    } else {
      card.style.opacity = "0.72";
      ring.style.background = "#475569";
      ring.style.boxShadow = "none";
      label.style.color = "#94a3b8";
    }
  }

  function refreshStoryCardStyle(clips) {
    const card = document.getElementById(STORY_ITEM_ID);
    if (card && typeof card._remedalUpdateStyle === "function") {
      card._remedalUpdateStyle(clips);
    }
  }

  function buildNativeStoryCard(clips, anchorEl) {
    const ringSize = measureNativeRingSize(anchorEl);
    const card = document.createElement("div");
    card.id = STORY_ITEM_ID;
    card.className = "relative w-24 select-none";
    card.style.cssText = "width:96px;flex-shrink:0;display:flex;flex-direction:column;align-items:center;user-select:none;transition:opacity 0.2s ease;";

    const btn = document.createElement("button");
    btn.type = "button";
    btn.title = `Rückblick: ${clips.length} hochgeladene(r) Clip(s)`;
    btn.style.cssText = [
      "width:100%;display:flex;flex-direction:column;align-items:center;gap:8px;",
      "background:transparent;border:none;padding:0;cursor:pointer;",
      "font-family:system-ui,-apple-system,Segoe UI,sans-serif;"
    ].join("");
    btn.onclick = () => openStoryModal(clips);

    const ring = document.createElement("div");
    ring.style.cssText = `position:relative;box-sizing:border-box;flex-shrink:0;width:${ringSize}px;height:${ringSize}px;border-radius:9999px;padding:3px;`;

    const inner = document.createElement("div");
    inner.style.cssText = "width:100%;height:100%;box-sizing:border-box;border-radius:9999px;overflow:hidden;border:2px solid #0b0e14;background:#1e293b;display:flex;align-items:center;justify-content:center;transition:transform 0.14s ease;";
    btn.onmouseenter = () => { inner.style.transform = "scale(0.93)"; };
    btn.onmouseleave = () => { inner.style.transform = "none"; };

    const thumbSources = [];
    for (const c of clips) {
      if (c.thumbPath) thumbSources.push(toMedalFsUrl(c.thumbPath));
      if (c.cloudThumbUrl) thumbSources.push(c.cloudThumbUrl);
      if (c.thumbPath) thumbSources.push(toFileUrl(c.thumbPath));
    }

    if (thumbSources.length) {
      let tIdx = 0;
      const img = document.createElement("img");
      img.src = thumbSources[0];
      img.onerror = () => {
        tIdx++;
        if (tIdx < thumbSources.length) {
          img.src = thumbSources[tIdx];
        } else {
          img.style.display = "none";
          inner.textContent = "★";
          inner.style.color = "#facc15";
          inner.style.fontSize = "26px";
        }
      };
      img.style.cssText = "width:100%;height:100%;object-fit:cover;display:block;";
      inner.appendChild(img);
    } else {
      inner.textContent = "★";
      inner.style.color = "#facc15";
      inner.style.fontSize = "26px";
    }

    ring.appendChild(inner);

    const label = document.createElement("span");
    label.textContent = "Rückblick";
    label.style.cssText = "font-size:14px;font-weight:500;line-height:20px;width:100%;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;text-align:center;";

    btn.appendChild(ring);
    btn.appendChild(label);
    card.appendChild(btn);

    card._remedalUpdateStyle = (list) => applyStoryCardStyle(card, ring, label, list);
    applyStoryCardStyle(card, ring, label, clips);
    return card;
  }

  function measureNativeHomeCardWidth(anchorEl) {
    if (anchorEl) {
      for (const el of anchorEl.querySelectorAll("div, a, button")) {
        const r = el.getBoundingClientRect();
        if (r.width >= 220 && r.width <= 420 && r.height >= 120 && r.height <= 260) {
          const ratio = r.width / r.height;
          if (ratio >= 1.45 && ratio <= 2.05) {
            return Math.round(r.width);
          }
        }
      }
    }
    return 300;
  }

  function buildHomeThrowbacksSection(clips, anchorEl) {
    const first = clips[0];
    const cardWidth = measureNativeHomeCardWidth(anchorEl);
    const stackLayers = Math.min(2, Math.max(0, clips.length - 1)); // max 3 cards high (front + up to 2 behind)
    const years = Array.from(new Set(clips.map((c) => c.yearsAgo).filter(Boolean))).sort((a, b) => a - b);
    const yearTitle = years.length > 1
      ? `Vor ${years[0]}–${years[years.length - 1]} Jahren`
      : (first.yearsAgo === 1 ? "Vor 1 Jahr" : `Vor ${first.yearsAgo} Jahren`);

    const section = document.createElement("div");
    section.id = STORY_ITEM_ID;
    section.style.cssText = "width:100%;margin:0 0 24px 0;font-family:system-ui,-apple-system,Segoe UI,sans-serif;user-select:none;";

    const heading = document.createElement("div");
    heading.textContent = "Rückblicke";
    heading.style.cssText = "font-size:20px;font-weight:700;color:#f8fafc;margin:0 0 14px 0;letter-spacing:-0.01em;";

    const row = document.createElement("div");
    row.style.cssText = "display:flex;align-items:flex-start;gap:14px;";

    const btn = document.createElement("button");
    btn.type = "button";
    btn.title = `Rückblicke öffnen (${clips.length} Clip${clips.length === 1 ? "" : "s"})`;
    btn.style.cssText = `width:${cardWidth}px;max-width:100%;display:flex;flex-direction:column;gap:8px;background:transparent;border:none;padding:0;text-align:left;cursor:pointer;transition:opacity 0.2s ease;`;
    btn.onclick = () => openStoryModal(clips);

    // Stack container (front card + up to 2 peeking cards behind at the top, max 3 high)
    const stackWrap = document.createElement("div");
    stackWrap.style.cssText = `position:relative;width:100%;margin-top:${stackLayers * 6}px;`;

    const backCards = [];
    for (let layer = stackLayers; layer >= 1; layer--) {
      const inset = layer * 10;
      const topOffset = layer * 6;
      const bg = layer === 1 ? "#2a3140" : "#1c222e";
      const borderCol = layer === 1 ? "rgba(255,255,255,0.14)" : "rgba(255,255,255,0.08)";
      const back = document.createElement("div");
      back.style.cssText = [
        `position:absolute;left:${inset}px;right:${inset}px;top:-${topOffset}px;height:18px;`,
        `border-radius:10px 10px 0 0;background:${bg};border:1px solid ${borderCol};border-bottom:none;`,
        `z-index:${3 - layer};pointer-events:none;transition:transform 0.16s ease;`
      ].join("");
      stackWrap.appendChild(back);
      backCards.push({ el: back, lift: layer + 1 });
    }

    const thumbWrap = document.createElement("div");
    thumbWrap.style.cssText = "position:relative;z-index:3;width:100%;aspect-ratio:16/9;border-radius:10px;overflow:hidden;background:#141822;box-sizing:border-box;transition:box-shadow 0.16s ease,border-color 0.16s ease;";

    const thumbSources = [];
    for (const c of clips) {
      if (c.thumbPath) thumbSources.push(toMedalFsUrl(c.thumbPath));
      if (c.cloudThumbUrl) thumbSources.push(c.cloudThumbUrl);
      if (c.thumbPath) thumbSources.push(toFileUrl(c.thumbPath));
    }

    let thumbImg = null;
    if (thumbSources.length) {
      let tIdx = 0;
      thumbImg = document.createElement("img");
      thumbImg.src = thumbSources[0];
      thumbImg.style.cssText = "width:100%;height:100%;object-fit:cover;display:block;transition:transform 0.18s ease;";
      thumbImg.onerror = () => {
        tIdx++;
        if (tIdx < thumbSources.length) {
          thumbImg.src = thumbSources[tIdx];
        } else {
          thumbImg.style.display = "none";
        }
      };
      thumbWrap.appendChild(thumbImg);
    }

    const scrim = document.createElement("div");
    scrim.style.cssText = "position:absolute;inset:0;background:linear-gradient(180deg,rgba(0,0,0,0.08) 0%,rgba(0,0,0,0.45) 100%);pointer-events:none;";
    thumbWrap.appendChild(scrim);

    const statusPill = document.createElement("span");
    statusPill.style.cssText = "position:absolute;top:8px;right:8px;font-size:11px;font-weight:700;padding:3px 9px;border-radius:999px;pointer-events:none;";
    thumbWrap.appendChild(statusPill);

    const playBadge = document.createElement("span");
    playBadge.innerHTML = '<svg viewBox="0 0 24 24" width="14" height="14" fill="currentColor" style="display:block;"><path d="M8 5.14v14l11-7-11-7z"/></svg>';
    playBadge.style.cssText = "position:absolute;left:8px;bottom:8px;width:28px;height:28px;border-radius:999px;background:rgba(0,0,0,0.68);color:#ffffff;display:flex;align-items:center;justify-content:center;pointer-events:none;";
    thumbWrap.appendChild(playBadge);

    btn.onmouseenter = () => {
      if (thumbImg) thumbImg.style.transform = "scale(1.04)";
      for (const b of backCards) b.el.style.transform = `translateY(-${b.lift}px)`;
    };
    btn.onmouseleave = () => {
      if (thumbImg) thumbImg.style.transform = "none";
      for (const b of backCards) b.el.style.transform = "none";
    };

    stackWrap.appendChild(thumbWrap);

    const meta = document.createElement("div");
    meta.style.cssText = "display:flex;flex-direction:column;gap:2px;padding:0 2px;";
    const titleEl = document.createElement("div");
    titleEl.textContent = yearTitle;
    titleEl.style.cssText = "font-size:14px;font-weight:600;color:#f8fafc;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;";
    const subEl = document.createElement("div");
    const clipCountTxt = `${clips.length} Clip${clips.length === 1 ? "" : "s"}`;
    subEl.textContent = first.game ? `${first.game} • ${clipCountTxt}` : `${formatDate(first.createdAt)} • ${clipCountTxt}`;
    subEl.style.cssText = "font-size:12.5px;color:#94a3b8;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;";
    meta.appendChild(titleEl);
    meta.appendChild(subEl);

    btn.appendChild(stackWrap);
    btn.appendChild(meta);
    row.appendChild(btn);
    section.appendChild(heading);
    section.appendChild(row);

    const updateSectionStyle = (list) => {
      const watched = getWatchedSet();
      const unwatchedCount = list.filter((c) => !watched.has(c.id)).length;
      if (unwatchedCount > 0) {
        btn.style.opacity = "1";
        thumbWrap.style.border = "2px solid #facc15";
        thumbWrap.style.boxShadow = "0 4px 18px rgba(250,204,21,0.22)";
        statusPill.textContent = `${unwatchedCount} neu`;
        statusPill.style.background = "#facc15";
        statusPill.style.color = "#0f172a";
      } else {
        btn.style.opacity = "0.78";
        thumbWrap.style.border = "1px solid rgba(255,255,255,0.14)";
        thumbWrap.style.boxShadow = "none";
        statusPill.textContent = `${list.length} Clip${list.length === 1 ? "" : "s"}`;
        statusPill.style.background = "rgba(0,0,0,0.68)";
        statusPill.style.color = "#cbd5e1";
      }
    };

    section._remedalUpdateStyle = updateSectionStyle;
    updateSectionStyle(clips);
    return section;
  }

  async function ensureStoryInjected() {
    sweepAdElements(document.documentElement);

    const todayKey = getLocalDayKey();
    const existing = document.getElementById(STORY_ITEM_ID);
    const anchor = findStoryAnchor();

    if (!anchor) {
      if (existing) existing.remove();
      return;
    }

    if (existing && cachedDayKey === todayKey) {
      if (cachedClips) refreshStoryCardStyle(cachedClips);
      return;
    }
    if (existing && cachedDayKey !== todayKey) {
      existing.remove();
      cachedClips = null;
    }

    const clips = await loadThrowbacks(CFG_WINDOW_DAYS);
    if (!clips.length || document.getElementById(STORY_ITEM_ID)) return;

    const liveAnchor = findStoryAnchor();
    if (!liveAnchor) return;

    const el = liveAnchor.mode === "section"
      ? buildHomeThrowbacksSection(clips, liveAnchor.el)
      : buildNativeStoryCard(clips, liveAnchor.el);
    liveAnchor.el.insertAdjacentElement(liveAnchor.after ? "afterend" : "beforebegin", el);
  }

  function scheduleEnsure() {
    if (ensureTimer) return;
    ensureTimer = setTimeout(() => {
      ensureTimer = null;
      ensureStoryInjected();
    }, 250);
  }

  function init() {
    sweepAdElements(document.documentElement);
    setTimeout(ensureStoryInjected, 1000);
    setTimeout(ensureStoryInjected, 2800);
    window.addEventListener("hashchange", () => setTimeout(ensureStoryInjected, 300));

    setInterval(() => {
      const todayKey = getLocalDayKey();
      if ((cachedDayKey && cachedDayKey !== todayKey) || (!document.getElementById(STORY_ITEM_ID) && findStoryAnchor())) {
        ensureStoryInjected();
      }
    }, 30 * 60 * 1000);

    if (document.body) {
      const obs = new MutationObserver((mutations) => {
        for (const m of mutations) {
          if (m.type === "attributes" && m.target) {
            neutralizeAdElement(m.target);
            continue;
          }
          for (const node of m.addedNodes) {
            if (node.nodeType === 1) sweepAdElements(node);
          }
        }
        if (!document.getElementById(STORY_ITEM_ID) && findStoryAnchor()) {
          scheduleEnsure();
        }
      });
      obs.observe(document.body, {
        childList: true,
        subtree: true,
        attributes: true,
        attributeFilter: ["src"]
      });
    }
  }

  if (document.readyState === "loading") {
    window.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
