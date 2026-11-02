/*
 * Offline DAoC BotRealm — web dashboard (preview).
 *
 * Vue 3 is vendored in wwwroot/vendor (no build step, no npm at run time). Everything this file reads is the
 * local read-only API: api/overview, api/population, api/world.
 */
(() => {
  "use strict";

  const { createApp, ref, reactive, computed, watch, onMounted, onBeforeUnmount, nextTick } = Vue;

  const PAGE_SIZE = 50;
  const REALMS = [
    { id: 1, name: "Albion", color: "#d0574f" },
    { id: 2, name: "Midgard", color: "#5b8dd6" },
    { id: 3, name: "Hibernia", color: "#4caf7d" },
  ];
  const REALM_COLORS = { 1: "#d0574f", 2: "#5b8dd6", 3: "#4caf7d", 0: "#93a7bb" };
  const POLL_POPULATION_MS = 5000;
  const POLL_MAP_MS = 2000;

  async function getJson(url) {
    const response = await fetch(url, { cache: "no-store" });
    if (!response.ok) throw new Error(`${url} answered HTTP ${response.status}`);
    return response.json();
  }

  function compareText(a, b) {
    return String(a ?? "").localeCompare(String(b ?? ""), undefined, { sensitivity: "base", numeric: true });
  }

  createApp({
    setup() {
      // ------------------------------------------------------------------ shared state
      const tab = ref("population");
      const loading = ref(false);
      const error = ref("");
      const serverRunning = ref(false);
      const live = ref(false);
      const updatedUtc = ref(null);
      const installName = ref("");
      const databaseError = ref("");
      const counts = reactive({ total: 0, online: 0, realms: [] });
      const bots = ref([]);
      const map = reactive({ regions: [], bots: [] });
      const paintedMaps = ref(false);
      const zoneImages = new Map();
      const now = ref(Date.now());

      // ------------------------------------------------------------------ launcher state
      const launcher = reactive({
        state: "Stopped", ready: false, listening: false, pid: null, memoryMb: 0, uptimeSeconds: 0,
        clientAvailable: false, serverPackagePresent: true, modeOk: true, modeProblem: "", notice: "",
        version: "", refreshRunning: false,
      });
      const launcherToken = ref("");
      const notice = ref("");
      const noticeOk = ref(true);
      const busy = ref(false);
      const logLines = ref([]);
      const logBody = ref(null);
      const autoScroll = ref(true);
      let logNext = 0;
      let statusTimer = 0;
      let logTimer = 0;

      // ------------------------------------------------------------------ population state
      const search = ref("");
      const searchInput = ref(null);
      const realmFilter = ref(0);
      const onlineOnly = ref(false);
      const sortKey = ref("online");
      const sortDir = ref("asc");
      const page = ref(1);
      const selectedBotId = ref(null);
      const pageSize = PAGE_SIZE;

      // ------------------------------------------------------------------ map state
      const mapRegionId = ref(0);
      const mapStage = ref(null);
      const mapCanvas = ref(null);
      const zoomPercent = ref(100);
      const hover = ref(null);
      const view = { scale: 1, offsetX: 0, offsetY: 0, fitScale: 1, fitOffsetX: 0, fitOffsetY: 0 };
      let cssWidth = 0;
      let cssHeight = 0;
      let dpr = 1;
      let drag = null;
      let fittedFor = 0;

      // ------------------------------------------------------------------ options state
      const options = reactive({
        xpRate: "1", makeMeGm: false, rvrAnnouncements: true, pveAnnouncements: true,
        botCount: 0, serverStopped: true,
      });
      const goals = reactive({
        levels1To19: { soloPve: 60, groupPve: 40, rvr: 0, battlegrounds: 0 },
        levels20To49: { soloPve: 40, groupPve: 40, rvr: 20, battlegrounds: 0 },
        level50: { soloPve: 20, groupPve: 40, rvr: 40, battlegrounds: 0 },
      });
      const generate = reactive({ realm: 1, count: 10, level: 1 });
      const goalRows = [
        { key: "levels1To19", label: "Levels 1–19" },
        { key: "levels20To49", label: "Levels 20–49" },
        { key: "level50", label: "Level 50" },
      ];
      const goalFields = [
        { key: "soloPve", label: "Solo PvE" },
        { key: "groupPve", label: "Group PvE" },
        { key: "rvr", label: "RvR" },
        { key: "battlegrounds", label: "Battlegrounds" },
      ];

      const realms = REALMS;

      // ------------------------------------------------------------------ launcher derived state
      const serverUp = computed(() =>
        launcher.listening || launcher.state === "Running" || (launcher.state === "Starting" && launcher.listening));
      const starting = computed(() => launcher.state === "Starting" || launcher.refreshRunning);
      const ready = computed(() => launcher.ready === true);
      const canEnterRealm = computed(() => serverUp.value && ready.value && launcher.clientAvailable && !busy.value);
      const enterRealmHint = computed(() => {
        if (!serverUp.value) return "Start the server first.";
        if (!ready.value) return "The server is still starting: ENTER REALM unlocks when port 10300 is ready.";
        if (!launcher.clientAvailable) return "The new client is not installed (new-client\\ next to runtime).";
        return "Start the new client logged in to this server.";
      });
      const stateClass = computed(() => ({
        "state-up": launcher.state === "Running",
        "state-wait": launcher.state === "Starting" || launcher.state === "Stopping",
        "state-down": launcher.state === "Stopped",
      }));

      // ------------------------------------------------------------------ launcher calls
      async function refreshStatus() {
        try {
          const data = await getJson("api/status");
          if (data.token) launcherToken.value = data.token;
          if (data.launcher) {
            Object.assign(launcher, data.launcher);
            if (data.launcher.install) installName.value = data.launcher.install;
            // World writes are only safe when the server is fully stopped; keep the Options form in step.
            options.serverStopped = launcher.state === "Stopped" && !launcher.listening && !launcher.pid;
          }
        } catch (exception) {
          // A status hiccup must not spam the UI; actions report their own failures.
        }
      }

      async function postAction(path, confirmation) {
        if (confirmation && !window.confirm(confirmation)) return;
        if (!launcherToken.value) await refreshStatus();
        busy.value = true;
        notice.value = "";
        try {
          const response = await fetch(path, { method: "POST", headers: { "X-Launcher-Token": launcherToken.value } });
          const data = await response.json().catch(() => ({}));
          notice.value = data.message || (data.ok ? "Done." : "The action failed.");
          noticeOk.value = !!data.ok;
          if (data.launcher) Object.assign(launcher, data.launcher);
        } catch (exception) {
          notice.value = "The launcher could not be reached: " + exception.message;
          noticeOk.value = false;
        } finally {
          busy.value = false;
          await refreshStatus();
          await refreshLog();
        }
      }

      const startServer = () => postAction("api/server/start");
      const stopServer = () => postAction("api/server/stop", "Stop the server? The world is saved first, then the process shuts down.");
      const enterRealm = () => postAction("api/client/start");

      async function refreshLog() {
        try {
          const data = await getJson(`api/log?after=${logNext}`);
          if (data.lines && data.lines.length > 0) {
            const merged = logLines.value.concat(data.lines);
            logLines.value = merged.length > 3000 ? merged.slice(-3000) : merged;
            if (autoScroll.value) nextTick(scrollLogToEnd);
          }
          if (typeof data.next === "number") logNext = data.next;
        } catch (exception) {
          // The console is best-effort while the launcher is busy.
        }
      }

      function clearLog() {
        logLines.value = [];
      }

      function scrollLogToEnd() {
        const element = logBody.value;
        if (element) element.scrollTop = element.scrollHeight;
      }

      // ------------------------------------------------------------------ options and world writes
      async function loadOptions() {
        try {
          const data = await getJson("api/options");
          options.xpRate = data.xpRate;
          options.makeMeGm = data.makeMeGm;
          options.rvrAnnouncements = data.rvrAnnouncements;
          options.pveAnnouncements = data.pveAnnouncements;
          options.botCount = data.botCount;
          options.serverStopped = data.serverStopped;
          if (data.botGoals) {
            for (const [key, weights] of Object.entries(data.botGoals)) {
              if (goals[key]) Object.assign(goals[key], weights);
            }
          }
        } catch (exception) {
          // The Options tab is refreshed on entry and after every action.
        }
      }

      function openOptions() {
        tab.value = "options";
        loadOptions();
      }

      function goalTotal(key) {
        const row = goals[key];
        return ["soloPve", "groupPve", "rvr", "battlegrounds"]
          .reduce((sum, field) => sum + (Number(row[field]) || 0), 0);
      }

      const goalsValid = computed(() => goalRows.every((row) => goalTotal(row.key) === 100));

      function restoreGoalDefaults() {
        Object.assign(goals.levels1To19, { soloPve: 60, groupPve: 40, rvr: 0, battlegrounds: 0 });
        Object.assign(goals.levels20To49, { soloPve: 40, groupPve: 40, rvr: 20, battlegrounds: 0 });
        Object.assign(goals.level50, { soloPve: 20, groupPve: 40, rvr: 40, battlegrounds: 0 });
      }

      function goalQuery() {
        const prefix = { levels1To19: "l1", levels20To49: "l2", level50: "l50" };
        const names = { soloPve: "Solo", groupPve: "Group", rvr: "RvR", battlegrounds: "Bg" };
        const parts = [];
        for (const row of goalRows) {
          for (const field of goalFields) {
            parts.push(`${prefix[row.key]}${names[field.key]}=${encodeURIComponent(Number(goals[row.key][field.key]) || 0)}`);
          }
        }
        return parts.join("&");
      }

      async function saveXpRate() {
        await postAction(`api/options/xp?value=${encodeURIComponent(options.xpRate)}`);
        await loadOptions();
      }

      async function saveGm() {
        await postAction(`api/options/gm?enabled=${options.makeMeGm ? "true" : "false"}`);
        await loadOptions();
      }

      async function saveAnnouncements() {
        const rvr = options.rvrAnnouncements ? "true" : "false";
        const pve = options.pveAnnouncements ? "true" : "false";
        await postAction(`api/options/announcements?rvr=${rvr}&pve=${pve}`);
        await loadOptions();
      }

      async function generateBots() {
        const label = generate.count === 1 ? "one bot" : `${generate.count} bots`;
        if (!window.confirm(`Generate ${label} for ${REALMS[generate.realm - 1].name} at level ${generate.level}?`)) return;
        await postAction(`api/bots/generate?realm=${generate.realm}&count=${generate.count}&level=${generate.level}`);
        await loadOptions();
        await refreshPopulation();
        await refreshMap();
      }

      async function deleteBot(bot) {
        const warning = `Permanently delete ${bot.name}, the level ${bot.level} ${bot.className}?\n\n` +
          "This removes the character, its equipment, coins and Realm Exchange listings. This cannot be undone.";
        if (!window.confirm(warning)) return;
        await postAction(`api/bots/delete?botId=${bot.id}`);
        await loadOptions();
        await refreshPopulation();
      }

      async function deleteAllBots() {
        const warning = `Permanently delete all ${options.botCount.toLocaleString()} world bots?\n\n` +
          "This removes every bot's progress, equipment, coins and Realm Exchange records. Your player characters are not affected. This cannot be undone.";
        if (!window.confirm(warning)) return;
        await postAction("api/bots/delete-all");
        await loadOptions();
        await refreshPopulation();
      }

      async function teleportToBot(bot) {
        await postAction(`api/bots/teleport?botId=${bot.id}`);
      }

      async function saveGoals() {
        if (!goalsValid.value) return;
        await postAction(`api/bots/goals?${goalQuery()}`);
        await loadOptions();
      }

      function formatUptime(seconds) {
        const total = Math.max(0, Math.round(seconds));
        const hours = Math.floor(total / 3600);
        const minutes = Math.floor((total % 3600) / 60);
        if (hours > 0) return `${hours} h ${minutes} min`;
        if (minutes > 0) return `${minutes} min`;
        return `${total} s`;
      }

      // ------------------------------------------------------------------ derived population data
      const filteredBots = computed(() => {
        const tokens = search.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
        const realm = realmFilter.value;
        const wanted = onlineOnly.value;
        let list = bots.value;
        if (realm) list = list.filter((bot) => bot.realm === realm);
        if (wanted) list = list.filter((bot) => bot.online);
        if (tokens.length > 0) {
          list = list.filter((bot) => {
            const haystack = `${bot.name} ${bot.className} ${bot.zone} ${bot.activity} ${bot.realmName} ${bot.race}`.toLowerCase();
            return tokens.every((token) => haystack.includes(token));
          });
        }
        const key = sortKey.value;
        const direction = sortDir.value === "asc" ? 1 : -1;
        return [...list].sort((a, b) => {
          let result;
          if (key === "level" || key === "realm") result = (a[key] ?? 0) - (b[key] ?? 0);
          else if (key === "online") result = (a.online ? 0 : 1) - (b.online ? 0 : 1) || (b.level ?? 0) - (a.level ?? 0) || compareText(a.name, b.name);
          else result = compareText(a[key], b[key]);
          return result * direction;
        });
      });

      const pageCount = computed(() => Math.max(1, Math.ceil(filteredBots.value.length / pageSize)));

      watch(pageCount, (value) => {
        if (page.value > value) page.value = value;
      });

      const pageBots = computed(() => {
        const start = (page.value - 1) * pageSize;
        return filteredBots.value.slice(start, start + pageSize);
      });

      const pageWindow = computed(() => {
        const total = pageCount.value;
        const current = page.value;
        const span = 2;
        let start = Math.max(1, current - span);
        let end = Math.min(total, start + span * 2);
        start = Math.max(1, end - span * 2);
        const pages = [];
        for (let value = start; value <= end; value++) pages.push(value);
        return pages;
      });

      const selectedBot = computed(() => bots.value.find((bot) => bot.id === selectedBotId.value) || null);

      watch([search, realmFilter, onlineOnly], () => {
        page.value = 1;
      });

      function sortBy(key) {
        if (sortKey.value === key) {
          sortDir.value = sortDir.value === "asc" ? "desc" : "asc";
        } else {
          sortKey.value = key;
          sortDir.value = key === "level" ? "desc" : "asc";
        }
        page.value = 1;
      }

      function arrow(key) {
        if (sortKey.value !== key) return "";
        return sortDir.value === "asc" ? "▲" : "▼";
      }

      function goPage(value) {
        page.value = Math.min(Math.max(1, value), pageCount.value);
      }

      // ------------------------------------------------------------------ derived map data
      const currentRegion = computed(() => map.regions.find((region) => region.id === mapRegionId.value) || null);

      const mapBots = computed(() => map.bots.filter((bot) => bot.regionId === mapRegionId.value));

      const mapRegionsOnline = computed(() => map.regions.filter((region) => region.online > 0).length);

      // ------------------------------------------------------------------ polling
      function applyShared(data) {
        serverRunning.value = !!data.serverRunning;
        live.value = !!data.live;
        updatedUtc.value = data.updatedUtc ? new Date(data.updatedUtc) : null;
        if (data.install) installName.value = data.install;
        if (typeof data.paintedMaps === "boolean") paintedMaps.value = data.paintedMaps;
        databaseError.value = data.databaseError || "";
        if (data.counts) {
          counts.total = data.counts.total;
          counts.online = data.counts.online;
          counts.realms = data.counts.realms || [];
        }
      }

      async function refreshPopulation() {
        loading.value = true;
        try {
          const data = await getJson("api/population");
          applyShared(data);
          bots.value = data.bots || [];
          error.value = "";
        } catch (exception) {
          error.value = `Could not read the dashboard data: ${exception.message}`;
        } finally {
          loading.value = false;
        }
      }

      async function refreshMap() {
        try {
          const data = await getJson("api/world");
          applyShared(data);
          map.regions = data.regions || [];
          map.bots = data.bots || [];
          if (!mapRegionId.value || !map.regions.some((region) => region.id === mapRegionId.value)) {
            const suggested = map.regions.find((region) => region.id === data.suggestedRegionId);
            const busiest = [...map.regions].sort((a, b) => b.online - a.online)[0];
            mapRegionId.value = suggested ? suggested.id : busiest ? busiest.id : 0;
            fittedFor = 0;
          }
          if (fittedFor !== mapRegionId.value) {
            await nextTick();
            resetView();
          } else {
            draw();
          }
        } catch (exception) {
          error.value = `Could not read the map data: ${exception.message}`;
        }
      }

      function refreshNow() {
        refreshPopulation();
        if (tab.value === "map") refreshMap();
      }

      // ------------------------------------------------------------------ canvas: geometry
      function updateSize() {
        const canvas = mapCanvas.value;
        const stage = mapStage.value;
        if (!canvas || !stage) return;
        const bounds = stage.getBoundingClientRect();
        cssWidth = Math.max(1, Math.floor(bounds.width));
        cssHeight = Math.max(1, Math.floor(bounds.height));
        dpr = window.devicePixelRatio || 1;
        canvas.width = Math.floor(cssWidth * dpr);
        canvas.height = Math.floor(cssHeight * dpr);
        if (fittedFor === 0) resetView();
        else draw();
      }

      function worldToScreen(x, y, region) {
        return {
          x: (x - region.minX) * view.scale + view.offsetX,
          y: (y - region.minY) * view.scale + view.offsetY,
        };
      }

      function resetView() {
        const region = currentRegion.value;
        if (!region || !cssWidth || !cssHeight) {
          draw();
          return;
        }
        const padding = 28;
        const width = Math.max(1, region.width);
        const height = Math.max(1, region.height);
        const scale = Math.min((cssWidth - padding * 2) / width, (cssHeight - padding * 2) / height);
        view.fitScale = scale > 0 ? scale : 1;
        view.scale = view.fitScale;
        view.fitOffsetX = (cssWidth - width * scale) / 2;
        view.fitOffsetY = (cssHeight - height * scale) / 2;
        view.offsetX = view.fitOffsetX;
        view.offsetY = view.fitOffsetY;
        fittedFor = region.id;
        zoomPercent.value = 100;
        draw();
      }

      function zoomAt(factor, anchorX, anchorY) {
        const region = currentRegion.value;
        if (!region) return;
        const minScale = view.fitScale * 0.5;
        const maxScale = view.fitScale * 20;
        const next = Math.min(Math.max(view.scale * factor, minScale), maxScale);
        const applied = next / view.scale;
        if (!Number.isFinite(applied) || applied === 1) return;
        view.offsetX = anchorX - (anchorX - view.offsetX) * applied;
        view.offsetY = anchorY - (anchorY - view.offsetY) * applied;
        view.scale = next;
        zoomPercent.value = Math.round((view.scale / view.fitScale) * 100);
        draw();
      }

      function zoomBy(factor) {
        zoomAt(factor, cssWidth / 2, cssHeight / 2);
      }

      function canvasPoint(event) {
        const canvas = mapCanvas.value;
        const bounds = canvas.getBoundingClientRect();
        return { x: event.clientX - bounds.left, y: event.clientY - bounds.top };
      }

      function botAt(point, tolerance) {
        const region = currentRegion.value;
        if (!region) return null;
        let best = null;
        let bestDistance = tolerance;
        for (const bot of mapBots.value) {
          const screen = worldToScreen(bot.x, bot.y, region);
          const distance = Math.hypot(screen.x - point.x, screen.y - point.y);
          if (distance <= bestDistance) {
            bestDistance = distance;
            best = bot;
          }
        }
        return best;
      }

      // ------------------------------------------------------------------ canvas: drawing
      function draw() {
        const canvas = mapCanvas.value;
        if (!canvas) return;
        const context = canvas.getContext("2d");
        context.setTransform(dpr, 0, 0, dpr, 0, 0);
        context.clearRect(0, 0, cssWidth, cssHeight);

        const region = currentRegion.value;
        if (!region) return;

        // Sea glow behind the land.
        const glow = context.createRadialGradient(cssWidth / 2, cssHeight / 2, 40, cssWidth / 2, cssHeight / 2,
          Math.max(cssWidth, cssHeight) * 0.75);
        glow.addColorStop(0, "rgba(23, 42, 63, .85)");
        glow.addColorStop(1, "rgba(7, 11, 17, 0)");
        context.fillStyle = glow;
        context.fillRect(0, 0, cssWidth, cssHeight);

        context.save();
        context.beginPath();
        context.rect(0, 0, cssWidth, cssHeight);
        context.clip();

        // Zones: the new client's painted map when the install has one, the database's rectangle otherwise.
        context.font = '11px "Segoe UI", system-ui, sans-serif';
        for (const zone of region.zones) {
          const corner = worldToScreen(zone.x, zone.y, region);
          const width = zone.width * view.scale;
          const height = zone.height * view.scale;
          if (corner.x + width < -20 || corner.y + height < -20 || corner.x > cssWidth + 20 || corner.y > cssHeight + 20) continue;
          const image = zoneImage(zone.id, zone.hasMap);
          if (image) {
            context.drawImage(image, corner.x, corner.y, width, height);
            // A light veil keeps the map in the night palette and the dots readable on bright terrain.
            context.fillStyle = "rgba(8, 12, 18, .22)";
            context.fillRect(corner.x, corner.y, width, height);
            context.strokeStyle = "rgba(201, 168, 106, .18)";
            context.lineWidth = 1;
            context.strokeRect(corner.x + 0.5, corner.y + 0.5, Math.max(0, width - 1), Math.max(0, height - 1));
            continue;
          }
          context.fillStyle = zoneFill(zone.realm);
          context.fillRect(corner.x, corner.y, width, height);
          context.strokeStyle = "rgba(201, 168, 106, .28)";
          context.lineWidth = 1;
          context.strokeRect(corner.x + 0.5, corner.y + 0.5, Math.max(0, width - 1), Math.max(0, height - 1));
          if (width > 90 && height > 40) {
            context.fillStyle = "rgba(147, 167, 187, .75)";
            context.textAlign = "center";
            context.textBaseline = "middle";
            context.fillText(zone.name, corner.x + width / 2, corner.y + height / 2, width - 12);
          }
        }

        // Bots on top.
        for (const bot of mapBots.value) {
          const point = worldToScreen(bot.x, bot.y, region);
          if (point.x < -10 || point.y < -10 || point.x > cssWidth + 10 || point.y > cssHeight + 10) continue;
          const color = REALM_COLORS[bot.realm] || REALM_COLORS[0];
          const highlighted = hover.value && hover.value.bot.id === bot.id;
          const radius = highlighted ? 5 : 3.2;
          context.beginPath();
          context.arc(point.x, point.y, radius, 0, Math.PI * 2);
          context.fillStyle = color;
          context.shadowColor = color;
          context.shadowBlur = highlighted ? 16 : 7;
          context.fill();
          context.shadowBlur = 0;
          if (highlighted) {
            context.beginPath();
            context.arc(point.x, point.y, radius + 3.5, 0, Math.PI * 2);
            context.strokeStyle = "rgba(255, 255, 255, .85)";
            context.lineWidth = 1.2;
            context.stroke();
          }
        }

        context.restore();
      }

      function zoneFill(realm) {
        switch (realm) {
          case 1: return "rgba(208, 87, 79, .16)";
          case 2: return "rgba(91, 141, 214, .16)";
          case 3: return "rgba(76, 175, 125, .15)";
          default: return "rgba(93, 116, 138, .13)";
        }
      }

      /**
       * The painted zone map from the install, loaded once per zone. A missing image (many dungeons and battleground
       * zones have none) falls back to the plain rectangle and is never asked for again.
       */
      function zoneImage(zoneId, hasMap) {
        if (!hasMap) return null;
        let entry = zoneImages.get(zoneId);
        if (!entry) {
          const img = new Image();
          entry = { img, state: "loading" };
          zoneImages.set(zoneId, entry);
          img.onload = () => {
            entry.state = "ready";
            if (tab.value === "map") draw();
          };
          img.onerror = () => {
            entry.state = "missing";
          };
          img.src = `zone-map/${zoneId}`;
        }
        return entry.state === "ready" ? entry.img : null;
      }

      // ------------------------------------------------------------------ canvas: input
      function onPointerMove(event) {
        const point = canvasPoint(event);
        if (drag) {
          view.offsetX = drag.offsetX + (point.x - drag.x);
          view.offsetY = drag.offsetY + (point.y - drag.y);
          drag.moved = true;
          draw();
          return;
        }
        const bot = botAt(point, 11);
        if (bot) {
          hover.value = { bot, x: point.x, y: point.y };
          if (mapCanvas.value) mapCanvas.value.style.cursor = "pointer";
        } else {
          hover.value = null;
          if (mapCanvas.value) mapCanvas.value.style.cursor = "grab";
        }
        draw();
      }

      function onPointerDown(event) {
        if (event.button !== 0) return;
        const point = canvasPoint(event);
        drag = { x: point.x, y: point.y, offsetX: view.offsetX, offsetY: view.offsetY, moved: false };
        mapCanvas.value.setPointerCapture?.(event.pointerId);
        mapCanvas.value.classList.add("dragging");
      }

      function onPointerUp(event) {
        if (drag && !drag.moved) {
          const bot = botAt(canvasPoint(event), 11);
          if (bot) focusInRoster(bot);
        }
        drag = null;
        if (mapCanvas.value) {
          mapCanvas.value.classList.remove("dragging");
          mapCanvas.value.releasePointerCapture?.(event.pointerId);
        }
      }

      function onPointerLeave() {
        hover.value = null;
        draw();
      }

      function onWheel(event) {
        event.preventDefault();
        const point = canvasPoint(event);
        zoomAt(Math.exp(-event.deltaY * 0.0015), point.x, point.y);
      }

      // ------------------------------------------------------------------ navigation
      function openMap() {
        tab.value = "map";
        nextTick(() => {
          updateSize();
          if (!fittedFor) resetView();
          else draw();
          refreshMap();
        });
      }

      function showOnMap(bot) {
        mapRegionId.value = bot.regionId;
        fittedFor = 0;
        openMap();
      }

      function focusInRoster(bot) {
        selectedBotId.value = bot.id;
        search.value = bot.name;
        realmFilter.value = 0;
        onlineOnly.value = false;
        tab.value = "population";
        nextTick(() => {
          if (searchInput.value) searchInput.value.focus();
        });
      }

      // ------------------------------------------------------------------ formatting
      const updatedText = computed(() => {
        if (!updatedUtc.value) return "";
        const age = Math.max(0, Math.round((now.value - updatedUtc.value.getTime()) / 1000));
        if (age <= 1) return "updated now";
        if (age < 60) return `updated ${age} s ago`;
        return `updated ${Math.round(age / 60)} min ago`;
      });

      const refreshNote = computed(() => {
        const when = updatedUtc.value ? updatedUtc.value.toLocaleTimeString() : "—";
        const source = live.value
          ? "live server snapshot"
          : serverRunning.value ? "server publishing, waiting for a snapshot" : "saved roster (server stopped)";
        return `${source} · last read ${when} · refreshing every ${POLL_POPULATION_MS / 1000} s`;
      });

      const footerText = computed(() => {
        const state = serverUp.value ? (ready.value ? "Server running" : "Server starting") : "Server stopped";
        return `${state} · ${refreshNote.value}`;
      });

      // ------------------------------------------------------------------ lifecycle
      let populationTimer = 0;
      let mapTimer = 0;
      let clockTimer = 0;
      let observer = null;

      function keydown(event) {
        if (event.key === "/" && document.activeElement !== searchInput.value) {
          event.preventDefault();
          tab.value = "population";
          nextTick(() => searchInput.value?.focus());
        }
      }

      onMounted(() => {
        refreshPopulation();
        refreshMap();
        refreshStatus();
        refreshLog();
        loadOptions();
        // Deep links: #map, #server, #options or #population, so every screen can be bookmarked.
        const initial = (location.hash || "").replace("#", "");
        if (initial === "map") openMap();
        else if (initial === "server") tab.value = "server";
        else if (initial === "options") tab.value = "options";
        watch(tab, (value) => {
          const hash = "#" + value;
          if (location.hash !== hash) history.replaceState(null, "", hash);
        });
        statusTimer = window.setInterval(() => {
          if (!document.hidden) refreshStatus();
        }, 1000);
        logTimer = window.setInterval(() => {
          if (!document.hidden) refreshLog();
        }, 1000);
        populationTimer = window.setInterval(() => {
          if (!document.hidden) refreshPopulation();
        }, POLL_POPULATION_MS);
        mapTimer = window.setInterval(() => {
          if (!document.hidden && tab.value === "map") refreshMap();
        }, POLL_MAP_MS);
        clockTimer = window.setInterval(() => {
          now.value = Date.now();
          if (hover.value) draw();
        }, 1000);
        document.addEventListener("keydown", keydown);
        if (mapCanvas.value) {
          mapCanvas.value.addEventListener("wheel", onWheel, { passive: false });
        }
        if (mapStage.value && "ResizeObserver" in window) {
          observer = new ResizeObserver(() => updateSize());
          observer.observe(mapStage.value);
        }
      });

      onBeforeUnmount(() => {
        window.clearInterval(populationTimer);
        window.clearInterval(mapTimer);
        window.clearInterval(clockTimer);
        window.clearInterval(statusTimer);
        window.clearInterval(logTimer);
        document.removeEventListener("keydown", keydown);
        observer?.disconnect();
      });

      return {
        // shared
        tab, loading, error, databaseError, serverRunning, live, updatedText, refreshNote, installName,
        counts, realms, refreshNow, footerText,
        // launcher
        launcher, notice, noticeOk, busy, serverUp, starting, ready, canEnterRealm, enterRealmHint, stateClass,
        startServer, stopServer, enterRealm, logLines, logBody, autoScroll, clearLog, formatUptime,
        // options
        options, goals, goalRows, goalFields, goalsValid, goalTotal, generate, openOptions, loadOptions,
        saveXpRate, saveGm, saveAnnouncements, generateBots, deleteBot, deleteAllBots, teleportToBot, saveGoals,
        restoreGoalDefaults,
        // population
        search, searchInput, realmFilter, onlineOnly, sortBy, arrow, page, pageSize,
        pageBots, pageCount, pageWindow, goPage, filteredBots, selectedBot, selectedBotId,
        bots,
        // map
        map, mapRegionId, mapStage, mapCanvas, zoomPercent, currentRegion, mapBots, mapRegionsOnline,
        paintedMaps, hover, openMap, showOnMap, zoomBy, resetView,
        onPointerMove, onPointerDown, onPointerUp, onPointerLeave,
      };
    },
  }).mount("#app");
})();


