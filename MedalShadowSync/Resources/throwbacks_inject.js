
/* REMEDAL_THROWBACK_PATCH_V10 */
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
  const CFG_MAX_YEARS = REMEDAL_CFG?.onlyLastYear ? 1 : 6;

  console.info("[reMedal] V10 loaded", { windowDays: CFG_WINDOW_DAYS, blockAds: CFG_BLOCK_ADS, maxYears: CFG_MAX_YEARS });

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
    style.textContent = `${selectors} {\n  display: none !important;\n  width: 0 !important;\n  height: 0 !important;\n  pointer-events: none !important;\n}`;
    document.head.appendChild(style);
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

    const parent = el.parentElement;
    if (parent && parent.id !== "root" && parent.tagName !== "BODY" && parent.tagName !== "MAIN") {
      const hasOtherVisible = Array.from(parent.children).some(
        (ch) => ch !== el && ch.tagName !== "STYLE" && ch.tagName !== "SCRIPT"
      );
      if (!hasOtherVisible) {
        parent.style.setProperty("display", "none", "important");
      }
    }
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
      "background:linear-gradient(180deg,rgba(0,0,0,0.78) 0%,rgba(0,0,0,0) 100%);pointer-events:none;"
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
      fill.style.cssText = "height:100%;width:0%;background:#facc15;transition:width 0.08s linear;";
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

    const closeBtn = document.createElement("button");
    closeBtn.style.cssText = "background:rgba(15,23,42,0.75);border:1px solid rgba(255,255,255,0.18);color:#f8fafc;width:32px;height:32px;border-radius:8px;cursor:pointer;font-size:15px;";
    closeBtn.textContent = "✕";

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

    const prevZone = document.createElement("div");
    prevZone.title = "Vorheriger Clip (←)";
    prevZone.style.cssText = "position:absolute;left:0;top:64px;bottom:118px;width:14%;z-index:3;cursor:pointer;display:flex;align-items:center;padding-left:14px;";
    const prevHint = document.createElement("span");
    prevHint.textContent = "‹";
    prevHint.style.cssText = "width:38px;height:38px;border-radius:999px;background:rgba(0,0,0,0.52);color:#fff;font-size:24px;display:flex;align-items:center;justify-content:center;opacity:0;transition:opacity 0.15s;";
    prevZone.appendChild(prevHint);
    prevZone.onmouseenter = () => { if (idx > 0) prevHint.style.opacity = "1"; };
    prevZone.onmouseleave = () => { prevHint.style.opacity = "0"; };

    const nextZone = document.createElement("div");
    nextZone.title = "Nächster Clip (→)";
    nextZone.style.cssText = "position:absolute;right:0;top:64px;bottom:118px;width:14%;z-index:3;cursor:pointer;display:flex;align-items:center;justify-content:flex-end;padding-right:14px;";
    const nextHint = document.createElement("span");
    nextHint.textContent = "›";
    nextHint.style.cssText = "width:38px;height:38px;border-radius:999px;background:rgba(0,0,0,0.52);color:#fff;font-size:24px;display:flex;align-items:center;justify-content:center;opacity:0;transition:opacity 0.15s;";
    nextZone.appendChild(nextHint);
    nextZone.onmouseenter = () => { if (idx < playable.length - 1) nextHint.style.opacity = "1"; };
    nextZone.onmouseleave = () => { nextHint.style.opacity = "0"; };

    // Medal-style docked control overlay
    const ctrlOverlay = document.createElement("div");
    ctrlOverlay.style.cssText = [
      "position:absolute;left:0;right:0;bottom:0;z-index:6;padding:34px 18px 14px 18px;",
      "background:linear-gradient(180deg,rgba(0,0,0,0) 0%,rgba(9,11,16,0.88) 42%,rgba(9,11,16,0.97) 100%);",
      "display:flex;flex-direction:column;gap:8px;"
    ].join("");
    ctrlOverlay.onclick = (e) => e.stopPropagation();

    // Title + Game/Date row
    const titleRow = document.createElement("div");
    titleRow.style.cssText = "display:flex;align-items:baseline;justify-content:space-between;gap:12px;";
    const ctrlTitle = document.createElement("div");
    ctrlTitle.style.cssText = "color:#ffffff;font-size:15px;font-weight:700;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;";
    const ctrlSub = document.createElement("div");
    ctrlSub.style.cssText = "color:#cbd5e1;font-size:12px;white-space:nowrap;flex-shrink:0;";
    titleRow.appendChild(ctrlTitle);
    titleRow.appendChild(ctrlSub);

    // Seek bar
    const seekBar = document.createElement("input");
    seekBar.type = "range";
    seekBar.min = "0";
    seekBar.max = "1000";
    seekBar.value = "0";
    seekBar.style.cssText = "width:100%;height:5px;accent-color:#facc15;cursor:pointer;margin:0;";

    // Controls row
    const ctrlRow = document.createElement("div");
    ctrlRow.style.cssText = "display:flex;align-items:center;justify-content:space-between;gap:10px;flex-wrap:wrap;";

    const leftCtrls = document.createElement("div");
    leftCtrls.style.cssText = "display:flex;align-items:center;gap:8px;";

    const btnCss = "background:rgba(255,255,255,0.1);border:1px solid rgba(255,255,255,0.14);color:#f8fafc;height:30px;padding:0 10px;border-radius:7px;cursor:pointer;font-size:12px;font-weight:600;display:inline-flex;align-items:center;gap:5px;";
    const selectCss = "background:#151a24;border:1px solid rgba(255,255,255,0.16);color:#f8fafc;height:30px;padding:0 8px;border-radius:7px;cursor:pointer;font-size:12px;font-weight:600;";

    const playPauseBtn = document.createElement("button");
    playPauseBtn.style.cssText = "background:#facc15;border:none;color:#0f172a;height:30px;min-width:34px;padding:0 10px;border-radius:7px;cursor:pointer;font-size:13px;font-weight:800;";
    playPauseBtn.textContent = "⏸";

    const timeLabel = document.createElement("span");
    timeLabel.style.cssText = "color:#e2e8f0;font-size:12px;font-variant-numeric:tabular-nums;min-width:86px;";
    timeLabel.textContent = "0:00 / 0:00";

    const muteBtn = document.createElement("button");
    muteBtn.style.cssText = btnCss;
    muteBtn.textContent = (isMuted || currentVol === 0) ? "🔇" : "🔊";

    const volSlider = document.createElement("input");
    volSlider.type = "range";
    volSlider.min = "0";
    volSlider.max = "1";
    volSlider.step = "0.05";
    volSlider.value = String(currentVol);
    volSlider.style.cssText = "width:74px;height:4px;accent-color:#facc15;cursor:pointer;";

    leftCtrls.appendChild(playPauseBtn);
    leftCtrls.appendChild(timeLabel);
    leftCtrls.appendChild(muteBtn);
    leftCtrls.appendChild(volSlider);

    const rightCtrls = document.createElement("div");
    rightCtrls.style.cssText = "display:flex;align-items:center;gap:8px;";

    const qualitySelect = document.createElement("select");
    qualitySelect.title = "Videoqualität";
    qualitySelect.style.cssText = selectCss;

    const speedSelect = document.createElement("select");
    speedSelect.title = "Geschwindigkeit";
    speedSelect.style.cssText = selectCss;
    for (const sp of [0.5, 1, 1.5, 2]) {
      const opt = document.createElement("option");
      opt.value = String(sp);
      opt.textContent = `${sp}x`;
      if (sp === 1) opt.selected = true;
      speedSelect.appendChild(opt);
    }

    const copyLinkBtn = document.createElement("button");
    copyLinkBtn.style.cssText = btnCss;
    copyLinkBtn.textContent = "🔗 Link teilen";

    const openMedalBtn = document.createElement("button");
    openMedalBtn.style.cssText = btnCss;
    openMedalBtn.textContent = "↗ Öffnen";

    const fsBtn = document.createElement("button");
    fsBtn.title = "Vollbild";
    fsBtn.style.cssText = btnCss;
    fsBtn.textContent = "⛶";

    rightCtrls.appendChild(qualitySelect);
    rightCtrls.appendChild(speedSelect);
    rightCtrls.appendChild(copyLinkBtn);
    rightCtrls.appendChild(openMedalBtn);
    rightCtrls.appendChild(fsBtn);

    ctrlRow.appendChild(leftCtrls);
    ctrlRow.appendChild(rightCtrls);

    ctrlOverlay.appendChild(titleRow);
    ctrlOverlay.appendChild(seekBar);
    ctrlOverlay.appendChild(ctrlRow);

    stage.appendChild(video);
    stage.appendChild(emptyMsg);
    stage.appendChild(prevZone);
    stage.appendChild(nextZone);
    stage.appendChild(ctrlOverlay);

    card.appendChild(stage);
    card.appendChild(topOverlay);
    overlay.appendChild(card);

    function fmtTime(sec) {
      if (!Number.isFinite(sec) || sec < 0) return "0:00";
      const m = Math.floor(sec / 60);
      const s = Math.floor(sec % 60);
      return `${m}:${s < 10 ? "0" : ""}${s}`;
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
      qualitySelect.style.display = clip.qualities.length > 0 ? "inline-block" : "none";
    }

    function loadClip(newIdx) {
      if (!playable.length) {
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
      timeLabel.textContent = "0:00 / 0:00";
      seekBar.value = "0";
      copyLinkBtn.textContent = "🔗 Link teilen";

      populateQualities(clip);
      video.dataset.qIdx = "0";
      video.dataset.watchedMarked = "";
      video.src = clip.qualities[0].url;
      video.muted = isMuted;
      video.volume = currentVol;
      video.playbackRate = currentRate;
      video.play().catch(() => {});
      playPauseBtn.textContent = "⏸";
    }

    function togglePlay() {
      if (video.paused) {
        video.play().catch(() => {});
      } else {
        video.pause();
      }
    }

    function closeModal() {
      if (document.fullscreenElement === card) {
        document.exitFullscreen().catch(() => {});
      }
      video.pause();
      video.removeAttribute("src");
      video.load();
      window.removeEventListener("keydown", onKey);
      overlay.remove();
    }

    video.addEventListener("play", () => { playPauseBtn.textContent = "⏸"; });
    video.addEventListener("pause", () => { playPauseBtn.textContent = "▶"; });
    video.addEventListener("playing", () => {
      const current = playable[idx];
      if (current && !video.dataset.watchedMarked) {
        video.dataset.watchedMarked = "1";
        markWatched(current.id);
        refreshStoryCardStyle(clips);
      }
    });

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
        loadClip(0);
        return;
      }
      if (idx >= playable.length) {
        closeModal();
        return;
      }
      loadClip(idx);
    });

    video.addEventListener("timeupdate", () => {
      if (video.duration > 0) {
        const ratio = Math.min(1, video.currentTime / video.duration);
        if (barFills[idx]) barFills[idx].style.width = (ratio * 100).toFixed(2) + "%";
        seekBar.value = String(Math.round(ratio * 1000));
        timeLabel.textContent = `${fmtTime(video.currentTime)} / ${fmtTime(video.duration)}`;
      }
    });
    video.addEventListener("ended", () => loadClip(idx + 1));

    seekBar.addEventListener("input", () => {
      if (video.duration > 0) {
        video.currentTime = (Number(seekBar.value) / 1000) * video.duration;
      }
    });

    volSlider.addEventListener("input", () => {
      currentVol = Number(volSlider.value);
      video.volume = currentVol;
      isMuted = currentVol === 0;
      video.muted = isMuted;
      muteBtn.textContent = isMuted ? "🔇" : "🔊";
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
        copyLinkBtn.textContent = "✓ Kopiert!";
        setTimeout(() => { copyLinkBtn.textContent = "🔗 Link teilen"; }, 2000);
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
      muteBtn.textContent = isMuted ? "🔇" : "🔊";
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

  // --- Seamless Medal Stories bar integration (exact 82px ring matching story-avatar-card) ---
  function findStoryAnchor() {
    const own = document.querySelector('[data-testid="story-avatar-card"][data-own-story]');
    if (own) return { el: own, after: true };
    const first = document.querySelector('[data-testid="story-avatar-card"]');
    if (first) return { el: first, after: false };
    return null;
  }

  function applyStoryCardStyle(card, ring, badge, label, clips) {
    const watched = getWatchedSet();
    const unwatchedCount = clips.filter((c) => !watched.has(c.id)).length;
    const hasUnwatched = unwatchedCount > 0;
    console.info("[reMedal] Story state:", { total: clips.length, unwatched: unwatchedCount });

    if (hasUnwatched) {
      card.style.opacity = "1";
      ring.style.background = "linear-gradient(135deg,#facc15,#f97316,#ec4899)";
      ring.style.boxShadow = "0 4px 12px rgba(250,204,21,0.32)";
      badge.style.background = "#facc15";
      badge.style.color = "#0f172a";
      label.style.color = "#fde047";
    } else {
      card.style.opacity = "0.72";
      ring.style.background = "#475569";
      ring.style.boxShadow = "none";
      badge.style.background = "#334155";
      badge.style.color = "#94a3b8";
      label.style.color = "#94a3b8";
    }
  }

  function refreshStoryCardStyle(clips) {
    const card = document.getElementById(STORY_ITEM_ID);
    if (card && typeof card._remedalUpdateStyle === "function") {
      card._remedalUpdateStyle(clips);
    }
  }

  function buildNativeStoryCard(clips) {
    const first = clips[0];
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
      "font-family:system-ui,-apple-system,Segoe UI,sans-serif;transition:transform 0.14s ease;"
    ].join("");
    btn.onmouseenter = () => { btn.style.transform = "scale(1.03)"; };
    btn.onmouseleave = () => { btn.style.transform = "none"; };
    btn.onclick = () => openStoryModal(clips);

    const ring = document.createElement("div");
    ring.style.cssText = "position:relative;width:82px;height:82px;border-radius:9999px;padding:3px;";

    const inner = document.createElement("div");
    inner.style.cssText = "width:100%;height:100%;border-radius:9999px;overflow:hidden;border:3px solid #0b0e14;background:#1e293b;display:flex;align-items:center;justify-content:center;";

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

    const badge = document.createElement("span");
    badge.textContent = first.yearsAgo === 1 ? "1J" : `${first.yearsAgo}J`;
    badge.style.cssText = [
      "position:absolute;bottom:0;right:0;",
      "font-size:11px;font-weight:800;padding:1px 6px;border-radius:9999px;border:2px solid #0b0e14;"
    ].join("");

    ring.appendChild(inner);
    ring.appendChild(badge);

    const label = document.createElement("span");
    label.textContent = "Rückblick";
    label.style.cssText = "font-size:14px;font-weight:500;line-height:20px;width:100%;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;text-align:center;";

    btn.appendChild(ring);
    btn.appendChild(label);
    card.appendChild(btn);

    card._remedalUpdateStyle = (list) => applyStoryCardStyle(card, ring, badge, label, list);
    applyStoryCardStyle(card, ring, badge, label, clips);
    return card;
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

    const card = buildNativeStoryCard(clips);
    liveAnchor.el.insertAdjacentElement(liveAnchor.after ? "afterend" : "beforebegin", card);
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
