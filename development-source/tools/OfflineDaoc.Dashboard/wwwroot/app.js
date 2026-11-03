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

  function realmName(realm) {
    return ({ 1: "Albion", 2: "Midgard", 3: "Hibernia" })[realm] || "Battleground";
  }
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

  /** A small generic table used by the read-only world screens (columns are declarative or taken from the data). */
  const SimpleTable = {
    props: {
      columns: { type: Array, default: () => [] },
      rows: { type: Array, default: () => [] },
      auto: { type: Boolean, default: false },
      empty: { type: String, default: "Nothing to show." },
      compact: { type: Boolean, default: false },
      selectable: { type: Boolean, default: false },
      selected: { type: Function, default: null },
    },
    computed: {
      effectiveColumns() {
        if (!this.auto) return this.columns;
        const first = (this.rows && this.rows[0]) || {};
        return Object.keys(first).map((key) => ({ key, label: key }));
      },
    },
    methods: {
      cell(row, column) {
        const value = row[column.key];
        if (column.format) return column.format(value, row);
        return value === null || value === undefined || value === "" ? "—" : value;
      },
    },
    template: `
      <div class="table-wrap compact-table">
        <table class="bots">
          <thead><tr><th v-for="column in effectiveColumns" :key="column.key" :class="column.className">{{ column.label }}</th></tr></thead>
          <tbody>
            <tr v-for="(row, index) in rows" :key="index"
                :class="{ 'row-selected': selectable && selected && selected(row) }"
                @click="selectable && $emit('row-click', row)">
              <td v-for="column in effectiveColumns" :key="column.key" :class="column.className">{{ cell(row, column) }}</td>
            </tr>
            <tr v-if="!rows || rows.length === 0">
              <td :colspan="effectiveColumns.length || 1" class="empty">{{ empty }}</td>
            </tr>
          </tbody>
        </table>
      </div>`,
  };
  const app = createApp({
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
      const paintedMaps = ref(false);
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
      // The map is two kinds of view: a realm overview (the client's own realm map, composed from the painted
      // zone maps at the squares it places them on) or one zone's own painted map. The API answers for the view
      // the page asks for and includes only the bots that view can show.
      const mapStage = ref(null);
      const mapCanvas = ref(null);
      const mapData = reactive({
        loaded: false,
        view: "",
        kind: "",
        overviews: [],
        zones: [],
        bots: [],
        zone: null,
        unplaced: 0,
      });
      const mapZoom = ref(100);
      const mapHover = ref(null);
      const mapCursor = ref("grab");
      const mapFocusBotId = ref(0);
      const mapLayer = ref("realm");
      const mapTransform = { scale: 1, offsetX: 0, offsetY: 0, fitScale: 1 };
      const zoneImages = new Map();
      const regionImages = new Map();
      let mapContentWidth = 256;
      let mapContentHeight = 256;
      let cssWidth = 0;
      let cssHeight = 0;
      let dpr = 1;
      let drag = null;
      let fittedView = "";
      let mapDots = [];
      let mapRequest = 0;
      let mapFocusPending = false;
      let mapFollowBotId = 0;

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
      const generate = reactive({ realm: 1, count: 10 });
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

      // ------------------------------------------------------------------ world screens state
      const groups = ref([]);
      const groupsLoaded = ref(false);
      const rvr = ref(null);
      const rvrLoaded = ref(false);
      const stats = ref(null);
      const statsLoaded = ref(false);
      const eventRealm = ref("");
      const eventKind = ref("");
      const eventActiveOnly = ref(false);
      const eventSearch = ref("");
      const selectedObjectiveId = ref("");
      const rvrRealm = ref(0);
      const rvrSearch = ref("");
      const rvrSortKey = ref("default");
      const rvrSortDir = ref("asc");
      const inspector = ref(null);
      const inspectorBot = ref(null);
      const inspectorLoading = ref(false);
      const inspectorSection = ref("Stats");
      const exchange = reactive({
        available: false, error: "", tab: "listings", realm: "", search: "", page: 1, pageSize: 50,
        listings: [], purchases: [],
      });
      const exchangeListingColumns = [
        { key: "itemName", label: "Item" },
        { key: "itemLevel", label: "Level", className: "num" },
        { key: "quantity", label: "Qty", className: "num" },
        { key: "seller", label: "Listed by" },
        { key: "price", label: "Price", className: "num" },
        { key: "realm", label: "Realm" },
        { key: "expires", label: "Expires / remaining" },
      ];
      const exchangePurchaseColumns = [
        { key: "sold", label: "Sold" },
        { key: "itemName", label: "Item" },
        { key: "quantity", label: "Qty", className: "num" },
        { key: "seller", label: "Seller" },
        { key: "buyer", label: "Buyer" },
        { key: "price", label: "Price", className: "num" },
        { key: "realm", label: "Realm" },
      ];
      const records = reactive({
        available: true, error: "", rows: [], total: 0, page: 1, pageSize: 100,
        realm: "", kind: "", outcome: "", search: "",
      });
      const inspectorSections = ["Stats", "Equipment", "Bag", "Vault", "Specs", "Abilities", "Listings", "Effects"];

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
        if (!window.confirm(`Generate ${label} for ${REALMS[generate.realm - 1].name}? ` +
          "They start at level 1 and level up in the world.")) return;
        await postAction(`api/bots/generate?realm=${generate.realm}&count=${generate.count}`);
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

      // ------------------------------------------------------------------ world screens: loaders
      async function loadGroups() {
        try {
          const data = await getJson("api/groups");
          groups.value = data.groups || [];
          groupsLoaded.value = true;
        } catch (exception) { /* refreshed by the poll */ }
      }

      async function loadRvr() {
        try {
          const data = await getJson("api/rvr");
          if (data.available) rvr.value = data.snapshot;
          rvrLoaded.value = true;
        } catch (exception) { /* refreshed by the poll */ }
      }

      async function loadStats() {
        try {
          const data = await getJson("api/stats");
          if (data.available) stats.value = data.snapshot;
          statsLoaded.value = true;
        } catch (exception) { /* refreshed by the poll */ }
      }

      async function loadExchange() {
        try {
          const data = await getJson("api/exchange");
          exchange.available = data.available;
          exchange.error = data.error || "";
          exchange.listings = data.listings || [];
          exchange.purchases = data.purchases || [];
          if (exchange.page > exchangePageCount.value) exchange.page = exchangePageCount.value;
        } catch (exception) {
          exchange.error = "The Realm Exchange could not be read: " + exception.message;
        }
      }

      async function loadRecords(page) {
        records.page = Math.max(1, page || 1);
        const query = new URLSearchParams({
          page: String(records.page), realm: records.realm, kind: records.kind,
          outcome: records.outcome, search: records.search,
        });
        try {
          const data = await getJson("api/records?" + query.toString());
          records.available = data.available;
          records.error = data.error || "";
          records.rows = data.rows || [];
          records.total = data.total || 0;
        } catch (exception) {
          records.error = "The event records could not be read: " + exception.message;
        }
      }

      async function loadInspector(bot) {
        if (!bot) return;
        inspectorBot.value = bot;
        selectedBotId.value = bot.id;
        inspectorLoading.value = true;
        inspectorSection.value = "Stats";
        try {
          const data = await getJson(`api/inspect?botId=${bot.id}`);
          if (data.available) {
            inspector.value = data.snapshot;
            if (notice.value.includes("could not be inspected") || notice.value.includes("cannot be inspected"))
              notice.value = "";
          } else {
            // Keep the last description on screen; the notice says why it did not refresh.
            notice.value = data.reason === "stopped"
              ? `${bot.name} cannot be inspected: the server is not running, so there is no live description.`
              : data.reason === "timeout"
                ? `${bot.name} could not be inspected: the server was busy and did not answer in time. Press Refresh to try again.`
                : `${bot.name} could not be inspected right now. Press Refresh to try again.`;
            noticeOk.value = false;
          }
        } catch (exception) {
          notice.value = "The Bot Inspector could not read that bot: " + exception.message;
          noticeOk.value = false;
        } finally {
          inspectorLoading.value = false;
        }
      }

      function openGroups() { tab.value = "groups"; loadGroups(); }
      // Active RvR is a view of the roster (same poll as Active Population): no separate snapshot to fetch.
      function openRvr() { tab.value = "rvr"; }
      function openBattlegrounds() { tab.value = "battlegrounds"; loadRvr(); }
      function openEvents() { tab.value = "events"; loadRvr(); }
      function openStats() { tab.value = "stats"; loadStats(); }
      function openExchange() { tab.value = "exchange"; loadExchange(); }
      function openRecords() { tab.value = "records"; loadRecords(1); }
      function openInspector(bot) { tab.value = "inspector"; loadInspector(bot); }
      function selectObjective(event) { selectedObjectiveId.value = event.Id || event.Name; }
      function inspectFromRoster(bot) { openInspector(bot); }

      // ------------------------------------------------------------------ world screens: derived data
      const rvrObjectives = computed(() => (rvr.value && rvr.value.Objectives) || []);
      const rvrUpdated = computed(() => (rvr.value ? new Date(rvr.value.UpdatedUtc) : null));
      const rvrElapsed = computed(() => (rvrUpdated.value ? Math.max(0, now.value - rvrUpdated.value.getTime()) : 0));
      const rvrRunning = computed(() => !!(rvr.value && rvr.value.Running));
      const selectedObjective = computed(() => rvrObjectives.value.find(
        (objective) => (objective.Id || objective.Name) === selectedObjectiveId.value) || null);
      const eventParticipants = computed(() => ((rvr.value && rvr.value.Participants) || [])
        .filter((participant) => !selectedObjective.value || participant.EventId === selectedObjective.value.Id));
      const participantColumns = [
        { key: "Name", label: "Name" },
        { key: "Realm", label: "Realm" },
        { key: "GroupId", label: "Group" },
        { key: "Location", label: "Zone" },
        { key: "X", label: "X", className: "num" },
        { key: "Y", label: "Y", className: "num" },
        { key: "Z", label: "Z", className: "num" },
        { key: "Activity", label: "Current activity" },
      ];

      // The desktop launcher's card roster: who is in the party, their level, class and where they are. The card
      // itself already carries the group's shared goal, status and clock, so the per-bot activity is not repeated.
      const groupMemberColumns = [
        { key: "name", label: "Member", format: (value, row) => memberLabel(row) },
        { key: "level", label: "Level", className: "num" },
        { key: "className", label: "Class" },
        { key: "zone", label: "Zone" },
      ];

      /**
       * Group and RvR task clocks, ported from the desktop launcher's BotRow (TaskTimerDisplay is a display-only
       * clock: minutes and seconds, never a new read). The deadlines come with the roster; the ticking is local.
       */
      function parseDeadline(value) {
        if (!value) return null;
        // The server writes ISO-8601 with up to seven fractional digits; Date.parse expects at most three.
        const time = Date.parse(String(value).replace(/\.(\d{3})\d+/, ".$1"));
        return Number.isFinite(time) ? time : null;
      }

      function remainingMilliseconds(deadline) {
        const time = parseDeadline(deadline);
        return time === null ? null : Math.max(0, Math.ceil(time - now.value));
      }

      function timerClock(milliseconds) {
        const seconds = Math.ceil(Math.max(0, Number(milliseconds) || 0) / 1000);
        return `${String(Math.floor(seconds / 60)).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`;
      }

      /** The meet-up deadline only exists while a group is still assembling. */
      function assemblyRemaining(bot) {
        if (!bot || !bot.online) return null;
        return bot.groupPhase === "Leader staging" || bot.groupPhase === "Meeting up"
          ? remainingMilliseconds(bot.meetUpDeadlineUtc) : null;
      }

      /** The task clock: the assembly clock while a group forms, the group's own task clock, else the objective's. */
      function botRemaining(bot) {
        if (!bot || !bot.online || bot.retired || bot.groupPhase === "Player-led") return null;
        const assembly = assemblyRemaining(bot);
        if (assembly !== null) return assembly;
        if (bot.hasGroupTaskClock && bot.taskTimerPaused) return Number(bot.taskRemainingMilliseconds) || 0;
        return remainingMilliseconds(bot.hasGroupTaskClock ? bot.groupTaskExpiresUtc : bot.objectiveExpiresUtc);
      }

      function botTaskRemaining(bot) {
        if (!bot || !bot.online) return "Offline";
        if (bot.groupPhase === "Player-led") return "Player-led";
        const staging = bot.groupPhase === "Leader staging" ? assemblyRemaining(bot) : null;
        if (staging !== null) return timerClock(staging) + " (staging)";
        const meetup = bot.groupPhase === "Meeting up" ? assemblyRemaining(bot) : null;
        if (meetup !== null) return timerClock(meetup) + " (meetup)";
        const remaining = botRemaining(bot);
        if (remaining === null) return "—";
        if (String(bot.objectiveAssignmentId || "").startsWith("between-pve-services-"))
          return "Town " + timerClock(remaining);
        return timerClock(remaining) + (bot.hasGroupTaskClock && bot.taskTimerPaused ? " (paused)" : "");
      }

      function botNameWithMeetUp(bot) {
        if (bot && bot.online && bot.groupPhase === "Meeting up") {
          const remaining = remainingMilliseconds(bot.meetUpDeadlineUtc);
          if (remaining !== null) return `${bot.name} (${timerClock(remaining)})`;
        }
        return bot ? bot.name : "";
      }

      function botFormation(bot) {
        return bot && bot.groupId ? `Warband ${bot.groupId}` : "Solo roamer";
      }

      function botGroupTimerText(bot) {
        if (!bot) return "";
        if (bot.groupPhase === "Leader staging") return "Leader staging: " + timerClock(assemblyRemaining(bot) ?? 0);
        if (bot.groupPhase === "Meeting up") return "Meet-up left: " + timerClock(assemblyRemaining(bot) ?? 0);
        const task = botTaskRemaining(bot);
        return task === "—" || task === "Offline" ? "" : "Task left: " + task;
      }

      function memberLabel(member) {
        const badges = [];
        if (member.groupRole) badges.push(String(member.groupRole).toUpperCase());
        const name = String(member.name || "").toLowerCase();
        if (name && name === String(member.groupLeader || "").toLowerCase()) badges.push("LEADER");
        if (name && name === String(member.groupPuller || "").toLowerCase()) badges.push("PULLER");
        const suffix = badges.length > 0 ? " [" + badges.join(" ") + "]" : "";
        return botNameWithMeetUp(member) + suffix;
      }

      function levelRange(group) {
        if (!group.members || group.members.length === 0) return "—";
        const levels = group.members.map((member) => Number(member.level) || 0);
        const low = Math.min(...levels);
        const high = Math.max(...levels);
        return low === high ? String(low) : `${low}–${high}`;
      }

      /** The card's clock belongs to the member who still has the longest meet-up deadline, as in the desktop launcher. */
      function groupTimerText(group) {
        const members = group.members || [];
        let owner = null;
        if (group.phase === "Meeting up") {
          const assembling = members.filter((member) => assemblyRemaining(member) !== null);
          owner = assembling.sort((a, b) => assemblyRemaining(b) - assemblyRemaining(a))[0] || null;
        } else {
          owner = members[0] || null;
        }
        return owner ? botGroupTimerText(owner) : "";
      }

      // Active RvR: the bots whose current objective is RvR, in the desktop launcher's wording and columns.
      const rvrBots = computed(() => bots.value.filter((bot) => bot.online && !bot.retired &&
        String(bot.objectiveKind || "").toLowerCase() === "rvr"));

      const filteredRvrBots = computed(() => {
        const tokens = rvrSearch.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
        let list = rvrBots.value;
        if (rvrRealm.value) list = list.filter((bot) => bot.realm === rvrRealm.value);
        if (tokens.length > 0) {
          list = list.filter((bot) => {
            const haystack = `${bot.name} ${bot.className} ${bot.zone} ${bot.activity} ${bot.objectivePhase} ${bot.groupId}`.toLowerCase();
            return tokens.every((token) => haystack.includes(token));
          });
        }
        if (rvrSortKey.value === "default") {
          // Realm, then warband, then level (highest first), then name: the order the desktop launcher opens with.
          return [...list].sort((a, b) => (a.realm - b.realm)
            || compareText(a.groupId || "~", b.groupId || "~")
            || (b.level - a.level)
            || compareText(a.name, b.name));
        }
        const direction = rvrSortDir.value === "asc" ? 1 : -1;
        const value = (bot) => {
          switch (rvrSortKey.value) {
            case "realm": return bot.realm;
            case "level": return bot.level;
            case "formation": return botFormation(bot);
            case "phase": return bot.objectivePhase || "";
            case "taskLeft": {
              const remaining = botRemaining(bot);
              return remaining === null ? Number.MAX_SAFE_INTEGER : remaining;
            }
            case "className": return bot.className || "";
            case "zone": return bot.zone || "";
            case "activity": return bot.activity || "";
            default: return bot.name || "";
          }
        };
        return [...list].sort((a, b) => {
          const first = value(a);
          const second = value(b);
          const result = typeof first === "number" && typeof second === "number"
            ? first - second : compareText(first, second);
          return (result || compareText(a.name, b.name)) * direction;
        });
      });

      function sortRvr(key) {
        if (rvrSortKey.value === key) rvrSortDir.value = rvrSortDir.value === "asc" ? "desc" : "asc";
        else {
          rvrSortKey.value = key;
          rvrSortDir.value = "asc";
        }
      }

      function rvrArrow(key) {
        if (rvrSortKey.value !== key) return "";
        return rvrSortDir.value === "asc" ? "▲" : "▼";
      }

      function objectiveSelected(row) {
        return (row.Id || row.Name) === selectedObjectiveId.value;
      }

      function markerFor(kind) {
        return kind === "Dragon" ? "DRAGON" : kind === "Epic dungeon" ? "DUNGEON" : kind === "Neutral raid" ? "NEUTRAL RAID"
          : kind === "Relic" ? "RELIC" : kind === "Relic keep" ? "RELIC KEEP" : kind === "Keep" ? "KEEP" : "PORTAL";
      }

      function clockText(milliseconds) {
        const total = Math.max(0, Math.round(milliseconds / 1000));
        const hours = Math.floor(total / 3600);
        const minutes = Math.floor((total % 3600) / 60);
        const seconds = total % 60;
        return hours > 0 ? `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`
          : `${minutes}:${String(seconds).padStart(2, "0")}`;
      }

      function objectiveClock(objective) {
        const remaining = Number(objective.PhaseRemainingMilliseconds) || 0;
        if (remaining < 0) return (objective.Phase || "Running") + " (no time limit)";
        return clockText(Math.max(0, remaining - (rvrRunning.value ? rvrElapsed.value : 0)));
      }

      function objectiveCooldown(objective) {
        if (objective.IsCatalogOnly) return "Unknown";
        const cooldown = Number(objective.CooldownMilliseconds) || 0;
        if (cooldown > 0) return Math.ceil(cooldown / 60000) + " min";
        if (objective.State === "Encounter unavailable") return "Not ready";
        return "Ready";
      }

      function activeEvent(objective) {
        const phase = objective.Phase || "";
        const state = objective.State || "";
        return ["Muster", "Staging", "Waiting", "Battle"].includes(phase)
          || /rally|SIEGE|RAID|ESCORT|DROPPED|Under attack/i.test(state);
      }

      const objectivesColumns = [
        { key: "Kind", label: "Marker", format: markerFor },
        { key: "Name", label: "Objective" },
        { key: "Owner", label: "Realm" },
        { key: "State", label: "State" },
        { key: "Forces", label: "Forces" },
        { key: "PhaseRemainingMilliseconds", label: "Stage / battle", format: (value, row) => objectiveClock(row) },
        { key: "CooldownMilliseconds", label: "Cooldown", format: (value, row) => objectiveCooldown(row) },
        { key: "Carrier", label: "Carrier" },
      ];

      const eventKinds = computed(() => [...new Set(rvrObjectives.value.map((objective) => objective.Kind))].sort());
      const eventRealms = computed(() => [...new Set(rvrObjectives.value.map((objective) => objective.Location))].sort());

      const filteredEvents = computed(() => {
        const search = eventSearch.value.trim().toLowerCase();
        return rvrObjectives.value.filter((objective) => {
          if (eventRealm.value && objective.Location !== eventRealm.value) return false;
          if (eventKind.value && objective.Kind !== eventKind.value) return false;
          if (eventActiveOnly.value && !activeEvent(objective)) return false;
          if (search) {
            const haystack = `${objective.Name} ${objective.Kind} ${objective.State} ${objective.Location} ${objective.Forces}`.toLowerCase();
            if (!haystack.includes(search)) return false;
          }
          return true;
        });
      });

      watch(filteredEvents, (list) => {
        if (!list.some((objective) => (objective.Id || objective.Name) === selectedObjectiveId.value))
          selectedObjectiveId.value = list.length ? (list[0].Id || list[0].Name) : "";
      }, { immediate: true });

      const battlegroundRows = computed(() => ((rvr.value && rvr.value.Battlegrounds) || []).map((battleground) => ({
        name: battleground.Name,
        levels: `${battleground.MinLevel}–${battleground.MaxLevel}`,
        centralKeep: battleground.CentralKeep,
        owner: battleground.Owner,
        albion: battleground.AlbionInside,
        midgard: battleground.MidgardInside,
        hibernia: battleground.HiberniaInside,
        total: battleground.AlbionInside + battleground.MidgardInside + battleground.HiberniaInside,
        travelling: battleground.AlbionTravelling + battleground.MidgardTravelling + battleground.HiberniaTravelling,
        monsters: battleground.Monsters,
      })));
      const battlegroundColumns = [
        { key: "name", label: "Battleground" },
        { key: "levels", label: "Levels" },
        { key: "centralKeep", label: "Central keep" },
        { key: "owner", label: "Held by" },
        { key: "albion", label: "Albion", className: "num" },
        { key: "midgard", label: "Midgard", className: "num" },
        { key: "hibernia", label: "Hibernia", className: "num" },
        { key: "total", label: "Inside", className: "num" },
        { key: "travelling", label: "On their way", className: "num" },
        { key: "monsters", label: "Monsters", className: "num" },
      ];

      const statsTotals = computed(() => {
        const snapshot = stats.value;
        if (!snapshot) return [];
        return [
          { label: "Monsters killed", value: snapshot.MobKills.toLocaleString() },
          { label: "Deaths", value: snapshot.Deaths.toLocaleString() },
          { label: "Money looted", value: worldMoney(snapshot.MoneyLootedCopper) },
          { label: "PvP kills", value: snapshot.PvpKills.toLocaleString() },
          { label: "Realm points", value: snapshot.RealmPoints.toLocaleString() },
          { label: "Keeps captured", value: snapshot.KeepsCaptured.toLocaleString() },
          { label: "Players tracked", value: snapshot.PlayersTracked.toLocaleString() },
          { label: "Bots tracked", value: snapshot.BotsTracked.toLocaleString() },
        ];
      });
      const statsBoards = computed(() => {
        const snapshot = stats.value;
        if (!snapshot) return [];
        const ranked = (rows) => (rows || []).map((row, index) => ({ ...row, rank: index + 1 }));
        return [
          { title: "Top mob kills", rows: ranked(snapshot.TopMobKills) },
          { title: "Top deaths", rows: ranked(snapshot.TopDeaths) },
          { title: "Top PvP kills", rows: ranked(snapshot.TopPvpKills) },
          { title: "Top keeps captured", rows: ranked(snapshot.TopKeepsCaptured) },
        ];
      });
      const statsBoardColumns = [
        { key: "rank", label: "#", className: "num",
          format: (value) => value },
        { key: "Name", label: "Name" },
        { key: "Kind", label: "Kind" },
        { key: "Realm", label: "Realm", format: (value) => REALMS.find((realm) => realm.id === Number(value))?.name || value },
        { key: "Level", label: "Level", className: "num" },
        { key: "Value", label: "Value", className: "num" },
      ];
      function worldMoney(copper) {
        if (!Number.isFinite(Number(copper))) return "—";
        const total = Number(copper);
        const platinum = Math.floor(total / 1000000);
        const gold = Math.floor((total % 1000000) / 10000);
        const silver = Math.floor((total % 10000) / 100);
        const remainder = total % 100;
        const parts = [];
        if (platinum > 0) parts.push(`${platinum}p`);
        if (gold > 0) parts.push(`${gold}g`);
        if (silver > 0) parts.push(`${silver}s`);
        if (remainder > 0 || parts.length === 0) parts.push(`${remainder}c`);
        return parts.join(" ");
      }

      const exchangeRows = computed(() => {
        const list = exchange.tab === "listings" ? exchange.listings : exchange.purchases;
        const realm = exchange.realm;
        const search = exchange.search.trim().toLowerCase();
        return list.filter((row) => {
          if (realm && row.realm !== realm) return false;
          if (!search) return true;
          const haystack = `${row.itemName} ${row.seller || ""} ${row.buyer || ""} ${row.realm}`.toLowerCase();
          return haystack.includes(search);
        });
      });
      const exchangePageCount = computed(() => Math.max(1, Math.ceil(exchangeRows.value.length / exchange.pageSize)));
      const exchangePageRows = computed(() => exchangeRows.value.slice(
        (exchange.page - 1) * exchange.pageSize, exchange.page * exchange.pageSize));
      const exchangeColumns = computed(() => (exchange.tab === "listings" ? exchangeListingColumns : exchangePurchaseColumns));
      watch(() => [exchange.realm, exchange.search, exchange.tab], () => { exchange.page = 1; });
      function exchangeGoPage(page) { exchange.page = Math.min(Math.max(1, page), exchangePageCount.value); }

      const recordsPageCount = computed(() => Math.max(1, Math.ceil(records.total / records.pageSize)));
      const recordsColumns = [
        { key: "Name", label: "Event" },
        { key: "Kind", label: "Kind" },
        { key: "Realm", label: "Realm" },
        { key: "StartedUtc", label: "Started" },
        { key: "EndedUtc", label: "Ended" },
        { key: "Phase", label: "Phase" },
        { key: "Outcome", label: "Outcome" },
        { key: "Assigned", label: "Assigned", className: "num" },
        { key: "Present", label: "Present", className: "num" },
        { key: "Details", label: "Details" },
      ];
      function recordsGoPage(page) { loadRecords(Math.min(Math.max(1, page), recordsPageCount.value)); }

      const inspectorData = computed(() => (inspector.value && inspector.value[inspectorSection.value]) || []);
      const inspectorOverview = computed(() => {
        const snapshot = inspector.value;
        if (!snapshot) return [];
        return [
          ["Level", snapshot.Level],
          ["Realm", snapshot.RealmName],
          ["Class", snapshot.ClassName],
          ["Race", snapshot.RaceName],
          ["Zone", snapshot.ZoneName],
          ["State", snapshot.Online ? "live in the world" : "last saved state"],
          ["Money", snapshot.Money],
          ["Health", `${snapshot.Health} / ${snapshot.MaxHealth}`],
          ["Mana", `${snapshot.Mana} / ${snapshot.MaxMana}`],
          ["Endurance", `${snapshot.Endurance} / ${snapshot.MaxEndurance}`],
          ["Deaths", snapshot.DeathCount],
          ["Craft", snapshot.Craft],
          ["Spec points spent", snapshot.SpecPointsSpent],
          ["Guild", snapshot.Guild],
          ["Group", snapshot.Group],
          ["Activity", snapshot.Activity],
          ["Goal", snapshot.CurrentGoal],
          ["Target", snapshot.TargetName],
          ["Warning", snapshot.Warning],
        ].filter((pair) => pair[1] !== null && pair[1] !== undefined && pair[1] !== "");
      });

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
      const mapBots = computed(() => mapData.bots);

      const mapCurrentOverview = computed(
        () => mapData.overviews.find((overview) => overview.id === mapData.view) || null);

      const mapZonesWithBots = computed(() => mapData.zones
        .filter((zone) => zone.online > 0)
        .sort((a, b) => b.online - a.online || compareText(a.name, b.name)));

      /** The zone filter's list, grouped by realm; only zones that have bots in them appear. */
      const realmZoneGroups = computed(() => {
        const groups = [];
        for (const realm of [1, 2, 3, 0]) {
          const zones = mapZonesWithBots.value.filter((zone) => zone.realm === realm);
          if (zones.length === 0) continue;
          groups.push({ realm, label: `${zones.length} ${realmName(realm)} zone${zones.length === 1 ? "" : "s"} with bots`, zones });
        }
        return groups;
      });

      /** The zone currently shown, when it has no bots and so is not in the filter's own list. */
      const currentZoneUnlisted = computed(() => mapData.kind === "zone" && mapData.zone &&
        !mapZonesWithBots.value.some((zone) => zone.id === mapData.zone.id));

      const mapZonesOnline = computed(() => mapZonesWithBots.value.length);

      const mapOverview = computed(() => mapCurrentOverview.value || null);

      const mapSummary = computed(() => {
        const online = mapData.bots.length;
        const bots = `${online} bot${online === 1 ? "" : "s"}`;
        if (mapData.kind === "zone" && mapData.zone) {
          return `${mapData.zone.name} · ${mapData.zone.kind} · ${mapData.zone.realmName} · ${bots}`;
        }
        const overview = mapCurrentOverview.value;
        return overview ? `${bots} on ${overview.zones.length} zones` : `${bots} on the map`;
      });

      const tooltipStyle = computed(() => {
        const hover = mapHover.value;
        if (!hover) return {};
        const width = hover.kind === "bot" ? 300 : 240;
        const height = hover.kind === "bot" ? 130 : 84;
        const maxLeft = Math.max(8, (cssWidth || 480) - width - 10);
        const maxTop = Math.max(8, (cssHeight || 320) - height - 10);
        return {
          left: `${Math.min(Math.max(8, hover.x + 16), maxLeft)}px`,
          top: `${Math.min(Math.max(8, hover.y + 16), maxTop)}px`,
        };
      });

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
          if (tab.value === "inspector" && !inspector.value && !inspectorLoading.value) {
            const bot = bots.value.find((candidate) => candidate.online) || bots.value[0];
            if (bot) loadInspector(bot);
          }
        } catch (exception) {
          error.value = `Could not read the dashboard data: ${exception.message}`;
        } finally {
          loading.value = false;
        }
      }

      async function refreshMap() {
        const request = ++mapRequest;
        try {
          const url = mapData.view ? `api/world?view=${encodeURIComponent(mapData.view)}` : "api/world";
          const data = await getJson(url);
          // A slower answer for a view the user has already left must not overwrite the current one.
          if (request !== mapRequest) return;
          applyShared(data);
          mapData.overviews = data.overviews || [];
          mapData.zones = data.zones || [];
          mapData.bots = data.bots || [];
          mapData.zone = data.zone || null;
          mapData.kind = data.viewKind || "";
          mapData.unplaced = data.unplaced || 0;
          if (mapHover.value && mapHover.value.kind === "bot") mapHover.value = null;
          const view = data.view || "";
          if (view !== mapData.view) {
            mapData.view = view;
            fittedView = "";
          }
          mapData.loaded = true;
          if (fittedView !== mapData.view) {
            await nextTick();
            resetView();
          } else {
            draw();
          }
          focusOnSelectedBot();
        } catch (exception) {
          if (request !== mapRequest) return;
          error.value = `Could not read the map data: ${exception.message}`;
        }
      }

      /** "Show on map": centre the selected bot and keep the camera on it while it is in view. */
      function focusOnSelectedBot() {
        if (!mapFollowBotId) return;
        const bot = mapData.bots.find((candidate) => candidate.id === mapFocusBotId.value);
        if (!bot) return;
        const point = botContentPoint(bot);
        if (!point) return;
        if (mapFocusPending) {
          mapFocusPending = false;
          mapTransform.scale = mapTransform.fitScale * 2.2;
          mapZoom.value = 220;
        }
        mapTransform.offsetX = cssWidth / 2 - point.x * mapTransform.scale;
        mapTransform.offsetY = cssHeight / 2 - point.y * mapTransform.scale;
        draw();
      }

      function refreshNow() {
        refreshPopulation();
        if (tab.value === "map") refreshMap();
      }

      // ------------------------------------------------------------------ map: views
      function selectMapView(next) {
        if (!next || next === mapData.view) {
          draw();
          return;
        }
        mapData.view = next;
        mapData.bots = [];
        mapData.zone = null;
        mapData.kind = next.startsWith("zone:") ? "zone" : "overview";
        mapData.unplaced = 0;
        mapHover.value = null;
        mapFocusBotId.value = 0;
        mapFocusPending = false;
        mapFollowBotId = 0;
        fittedView = "";
        nextTick(() => {
          updateSize();
          resetView();
          refreshMap();
        });
      }

      function openZone(zoneId) {
        // An install without the painted maps has no zone views: the outline maps are the whole map.
        if (!mapData.zones.some((zone) => zone.id === zoneId)) return;
        selectMapView(`zone:${zoneId}`);
      }

      function mapOverviewName(viewId) {
        const overview = mapData.overviews.find((candidate) => candidate.id === viewId);
        return overview ? overview.name : "the overview";
      }

      // ------------------------------------------------------------------ map: geometry
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
        // A resized window re-fits a view that is still at 100%; a zoomed view keeps the user's zoom.
        if (!fittedView || mapZoom.value === 100) resetView();
        else draw();
      }

      /** Content units are the overview's own layout (the realm map's pixels) or a zone map's 256 pixels. */
      function updateContentSize() {
        if (mapData.kind === "zone") {
          mapContentWidth = 256;
          mapContentHeight = 256;
          return;
        }
        const overview = mapCurrentOverview.value;
        if (overview && overview.hasImage && mapLayer.value !== "zones") {
          mapContentWidth = 256;
          mapContentHeight = 256;
          return;
        }
        mapContentWidth = Math.max(1, overview ? overview.width : 256);
        mapContentHeight = Math.max(1, overview ? overview.height : 256);
      }

      function contentToScreen(x, y) {
        return {
          x: x * mapTransform.scale + mapTransform.offsetX,
          y: y * mapTransform.scale + mapTransform.offsetY,
        };
      }

      /** On the painted realm layer the content is the whole 256-unit realm map; the zone squares sit inside it. */
      function mapOffset() {
        const overview = mapCurrentOverview.value;
        if (overview && overview.hasImage && mapLayer.value !== "zones")
          return { x: overview.imageLeft, y: overview.imageTop };
        return { x: 0, y: 0 };
      }

      function screenToContent(x, y) {
        return {
          x: (x - mapTransform.offsetX) / mapTransform.scale,
          y: (y - mapTransform.offsetY) / mapTransform.scale,
        };
      }

      function resetView() {
        if (!cssWidth || !cssHeight || !mapData.loaded) {
          draw();
          return;
        }
        updateContentSize();
        const padding = 26;
        const scale = Math.min((cssWidth - padding * 2) / mapContentWidth,
          (cssHeight - padding * 2) / mapContentHeight);
        mapTransform.fitScale = scale > 0 ? scale : 1;
        mapTransform.scale = mapTransform.fitScale;
        mapTransform.offsetX = (cssWidth - mapContentWidth * mapTransform.scale) / 2;
        mapTransform.offsetY = (cssHeight - mapContentHeight * mapTransform.scale) / 2;
        fittedView = mapData.view;
        mapZoom.value = 100;
        draw();
      }

      /** A zone map can be read at 900%; a realm overview is a picture of a whole realm and stops at 500%. */
      function maxZoomFactor() {
        return mapData.kind === "zone" ? 9 : 5;
      }

      function zoomAt(factor, anchorX, anchorY) {
        if (!mapData.loaded) return;
        const minScale = mapTransform.fitScale * 0.4;
        const maxScale = mapTransform.fitScale * maxZoomFactor();
        const next = Math.min(Math.max(mapTransform.scale * factor, minScale), maxScale);
        const applied = next / mapTransform.scale;
        if (!Number.isFinite(applied) || applied === 1) return;
        mapTransform.offsetX = anchorX - (anchorX - mapTransform.offsetX) * applied;
        mapTransform.offsetY = anchorY - (anchorY - mapTransform.offsetY) * applied;
        mapTransform.scale = next;
        mapZoom.value = Math.round((mapTransform.scale / mapTransform.fitScale) * 100);
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

      function zoomFactor() {
        return mapTransform.scale / Math.max(0.0001, mapTransform.fitScale);
      }

      /** Where a bot stands, in content units: inside its zone's square of the overview, or inside a zone map. */
      function botContentPoint(bot) {
        if (mapData.kind === "zone") {
          return { x: bot.mx * mapContentWidth, y: bot.my * mapContentHeight };
        }
        const overview = mapCurrentOverview.value;
        if (!overview) return null;
        const tile = overview.zones.find((zone) => zone.id === bot.zoneId);
        if (!tile) return null;
        const offset = mapOffset();
        return { x: offset.x + tile.left + bot.mx * tile.width, y: offset.y + tile.top + bot.my * tile.height };
      }

      function botScreenPoint(bot) {
        const content = botContentPoint(bot);
        return content ? contentToScreen(content.x, content.y) : null;
      }

      function botAt(point, tolerance) {
        let best = null;
        let bestDistance = tolerance;
        for (const dot of mapDots) {
          const distance = Math.hypot(dot.x - point.x, dot.y - point.y);
          if (distance <= bestDistance) {
            bestDistance = distance;
            best = dot.bot;
          }
        }
        return best;
      }

      function zoneAt(point) {
        if (mapData.kind === "zone") return null;
        const overview = mapCurrentOverview.value;
        if (!overview) return null;
        const content = screenToContent(point.x, point.y);
        const offset = mapOffset();
        content.x -= offset.x;
        content.y -= offset.y;
        for (const tile of overview.zones) {
          if (content.x >= tile.left && content.x <= tile.left + tile.width &&
              content.y >= tile.top && content.y <= tile.top + tile.height) return tile;
        }
        return null;
      }

      // ------------------------------------------------------------------ map: drawing
      function draw() {
        const canvas = mapCanvas.value;
        if (!canvas) return;
        const context = canvas.getContext("2d");
        context.setTransform(dpr, 0, 0, dpr, 0, 0);
        context.clearRect(0, 0, cssWidth, cssHeight);

        // Sea glow behind the land.
        const glow = context.createRadialGradient(cssWidth / 2, cssHeight / 2, 40, cssWidth / 2, cssHeight / 2,
          Math.max(cssWidth, cssHeight) * 0.75);
        glow.addColorStop(0, "rgba(23, 42, 63, .85)");
        glow.addColorStop(1, "rgba(7, 11, 17, 0)");
        context.fillStyle = glow;
        context.fillRect(0, 0, cssWidth, cssHeight);

        if (!mapData.loaded) return;

        context.save();
        context.beginPath();
        context.rect(0, 0, cssWidth, cssHeight);
        context.clip();
        mapDots = [];
        if (mapData.kind === "zone") drawZoneMap(context);
        else drawOverviewMap(context);
        drawBotDots(context);
        context.restore();
      }

      function drawMapMessage(context, text) {
        context.fillStyle = "rgba(224, 214, 194, .72)";
        context.font = '14px "Segoe UI", system-ui, sans-serif';
        context.textAlign = "center";
        context.textBaseline = "middle";
        context.fillText(text, cssWidth / 2, cssHeight / 2);
        context.textAlign = "left";
        context.textBaseline = "top";
      }

      function drawOverviewMap(context) {
        const overview = mapCurrentOverview.value;
        if (!overview) {
          drawMapMessage(context, "No realm overview yet.");
          return;
        }

        const corner = contentToScreen(0, 0);
        const width = mapContentWidth * mapTransform.scale;
        const height = mapContentHeight * mapTransform.scale;
        const hoveredZone = mapHover.value && mapHover.value.kind === "zone" ? mapHover.value.zone.id : 0;
        // The client's own painted realm map is the overview: it is the picture that shows the zones connected.
        // The zone pictures composed at the client's own squares are the second layer, for reading the terrain.
        const realmLayer = overview.hasImage && mapLayer.value !== "zones";
        if (realmLayer) {
          const image = regionImage(overview);
          if (image) {
            const natural = image.naturalWidth || 256;
            context.drawImage(image, 0, 0, natural, natural,
              corner.x, corner.y, width, height);
            context.fillStyle = "rgba(8, 12, 18, .12)";
            context.fillRect(corner.x, corner.y, width, height);
          } else {
            context.fillStyle = "rgba(12, 20, 30, .92)";
            context.fillRect(corner.x, corner.y, width, height);
            context.fillStyle = "rgba(224, 214, 194, .7)";
            context.font = '14px "Segoe UI", system-ui, sans-serif';
            context.textAlign = "center";
            context.textBaseline = "middle";
            context.fillText("Loading the realm map…", corner.x + width / 2, corner.y + height / 2);
            context.textAlign = "left";
            context.textBaseline = "top";
          }
        }

        context.font = '11px "Segoe UI", system-ui, sans-serif';
        context.textAlign = "left";
        context.textBaseline = "top";
        const offset = mapOffset();
        for (const tile of overview.zones) {
          const at = contentToScreen(offset.x + tile.left, offset.y + tile.top);
          const tileWidth = tile.width * mapTransform.scale;
          const tileHeight = tile.height * mapTransform.scale;
          if (at.x + tileWidth < -24 || at.y + tileHeight < -24 || at.x > cssWidth + 24 || at.y > cssHeight + 24)
            continue;

          const hovered = hoveredZone === tile.id;
          if (!realmLayer) {
            const image = tile.hasMap ? zoneImage(tile.id, false) : null;
            if (image) {
              context.drawImage(image, at.x, at.y, tileWidth, tileHeight);
              // A light veil keeps the map in the night palette and the dots readable on bright terrain.
              context.fillStyle = "rgba(8, 12, 18, .2)";
              context.fillRect(at.x, at.y, tileWidth, tileHeight);
            } else {
              context.fillStyle = zoneFill(tile.realm);
              context.fillRect(at.x, at.y, tileWidth, tileHeight);
            }
          }

          if (hovered) {
            context.fillStyle = realmLayer ? "rgba(255, 236, 190, .18)" : "rgba(255, 236, 190, .13)";
            context.fillRect(at.x, at.y, tileWidth, tileHeight);
          }
          // The zone squares are clickable in both layers; on the painted realm map they stay faint until hovered.
          context.strokeStyle = hovered ? "rgba(236, 202, 133, .95)"
            : realmLayer ? "rgba(201, 168, 106, .07)" : "rgba(201, 168, 106, .2)";
          context.lineWidth = hovered ? 2 : 1;
          context.strokeRect(at.x + 0.5, at.y + 0.5, Math.max(0, tileWidth - 1), Math.max(0, tileHeight - 1));

          if (!realmLayer && tileWidth >= 96 && tileHeight >= 52) {
            drawZoneChip(context, tile, at, tileWidth, hovered);
          } else if (!realmLayer && !tile.hasMap && tileWidth >= 44 && tileHeight >= 26) {
            context.fillStyle = "rgba(168, 187, 206, .8)";
            context.fillText(tile.name, at.x + 4, at.y + 4, tileWidth - 8);
          } else if (realmLayer && hovered) {
            drawZoneChip(context, tile, at, tileWidth, true);
          }
        }
      }
      /** The zone's name chip: the composed tiles are painted maps, so the name has to be added on top. */
      function drawZoneChip(context, tile, at, tileWidth, hovered) {
        const name = tile.name;
        const width = Math.min(tileWidth - 8, context.measureText(name).width + 12);
        context.fillStyle = hovered ? "rgba(9, 14, 21, .92)" : "rgba(9, 14, 21, .68)";
        context.fillRect(at.x + 4, at.y + 4, width, 17);
        context.fillStyle = hovered ? "#f0dca6" : "rgba(230, 221, 201, .92)";
        context.fillText(name, at.x + 10, at.y + 7, width - 12);
        if (tile.online > 0) {
          const badge = `${tile.online}`;
          const badgeWidth = context.measureText(badge).width + 10;
          context.fillStyle = "rgba(9, 14, 21, .85)";
          context.fillRect(at.x + tileWidth - badgeWidth - 4, at.y + 4, badgeWidth, 17);
          context.fillStyle = "#cfe2f2";
          context.fillText(badge, at.x + tileWidth - badgeWidth + 1, at.y + 7);
        }
      }

      function drawZoneMap(context) {
        const zone = mapData.zone;
        if (!zone) {
          drawMapMessage(context, "No zone chosen.");
          return;
        }

        const at = contentToScreen(0, 0);
        const width = mapContentWidth * mapTransform.scale;
        const height = mapContentHeight * mapTransform.scale;
        const image = zoneImage(zone.id, true);
        if (image) {
          context.drawImage(image, at.x, at.y, width, height);
        } else {
          context.fillStyle = "rgba(12, 20, 30, .92)";
          context.fillRect(at.x, at.y, width, height);
          drawMapMessage(context, "Loading the painted zone map…");
        }
        context.strokeStyle = "rgba(201, 168, 106, .22)";
        context.lineWidth = 1;
        context.strokeRect(at.x + 0.5, at.y + 0.5, width, height);
      }

      function drawBotDots(context) {
        const zoom = zoomFactor();
        const hoveredBot = mapHover.value && mapHover.value.kind === "bot" ? mapHover.value.bot.id : 0;
        for (const bot of mapData.bots) {
          const point = botScreenPoint(bot);
          if (!point) continue;
          if (point.x < -14 || point.y < -14 || point.x > cssWidth + 14 || point.y > cssHeight + 14) continue;
          const hovered = hoveredBot === bot.id;
          const focused = mapFocusBotId.value === bot.id;
          const color = REALM_COLORS[bot.realm] || REALM_COLORS[0];
          const radius = Math.min(hovered ? 7.5 : 5.5,
            Math.max(2.8, (hovered ? 4.4 : 3.1) * Math.sqrt(Math.max(1, zoom))));

          context.beginPath();
          context.arc(point.x, point.y, radius + 1.2, 0, Math.PI * 2);
          context.fillStyle = "rgba(6, 9, 13, .78)";
          context.fill();

          context.beginPath();
          context.arc(point.x, point.y, radius, 0, Math.PI * 2);
          if (bot.alive) {
            context.fillStyle = color;
            context.shadowColor = color;
            context.shadowBlur = hovered || focused ? 14 : 6;
            context.fill();
            context.shadowBlur = 0;
          } else {
            context.strokeStyle = color;
            context.lineWidth = 1.5;
            context.stroke();
          }

          if (hovered || focused) {
            context.beginPath();
            context.arc(point.x, point.y, radius + 2.8, 0, Math.PI * 2);
            context.strokeStyle = focused ? "rgba(236, 202, 133, .95)" : "rgba(255, 255, 255, .85)";
            context.lineWidth = 1.3;
            context.stroke();
            if (focused) drawBotNameTag(context, bot, point, radius);
          }

          mapDots.push({ bot, x: point.x, y: point.y });
        }
      }

      /** The followed bot's name chip, so "Show on map" cannot be lost among the other dots. */
      function drawBotNameTag(context, bot, point, radius) {
        const name = bot.name;
        if (!name) return;
        context.font = '11px "Segoe UI", system-ui, sans-serif';
        const width = context.measureText(name).width + 12;
        const left = Math.max(2, Math.min(cssWidth - width - 2, point.x - width / 2));
        const top = Math.min(cssHeight - 20, point.y + radius + 6);
        context.fillStyle = "rgba(9, 14, 21, .92)";
        context.fillRect(left, top, width, 17);
        context.strokeStyle = "rgba(201, 168, 106, .8)";
        context.lineWidth = 1;
        context.strokeRect(left + 0.5, top + 0.5, width, 17);
        context.fillStyle = "#f0dca6";
        context.textAlign = "left";
        context.textBaseline = "top";
        context.fillText(name, left + 6, top + 3);
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
       * The painted map from the install, loaded once per zone and size. The overview tiles use the small 256px
       * pictures; a zone's own view asks for the 4x one. A missing image is never asked for again.
       */
      function zoneImage(zoneId, full) {
        const key = `${zoneId}:${full ? 1 : 0}`;
        let entry = zoneImages.get(key);
        if (!entry) {
          const img = new Image();
          entry = { img, state: "loading" };
          zoneImages.set(key, entry);
          img.onload = () => {
            entry.state = "ready";
            if (tab.value === "map") draw();
          };
          img.onerror = () => {
            entry.state = "missing";
          };
          img.src = full ? `zone-map/${zoneId}?full=1` : `zone-map/${zoneId}`;
        }
        return entry.state === "ready" ? entry.img : null;
      }

      /** The painted realm map for an overview, loaded once per region. */
      function regionImage(overview) {
        const id = overview.regionId;
        let entry = regionImages.get(id);
        if (!entry) {
          const img = new Image();
          entry = { img, state: "loading" };
          regionImages.set(id, entry);
          img.onload = () => {
            entry.state = "ready";
            if (tab.value === "map") draw();
          };
          img.onerror = () => {
            entry.state = "missing";
          };
          img.src = `region-map/${id}`;
        }
        return entry.state === "ready" ? entry.img : null;
      }

      watch(mapLayer, () => {
        fittedView = "";
        nextTick(() => {
          updateSize();
          resetView();
        });
      });

      // ------------------------------------------------------------------ map: input
      function botHitTolerance() {
        return Math.max(11, 6 * Math.sqrt(Math.max(1, zoomFactor())));
      }

      function onMapPointerMove(event) {
        if (!mapData.loaded) return;
        const point = canvasPoint(event);
        if (drag) {
          const dx = point.x - drag.x;
          const dy = point.y - drag.y;
          if (!drag.moved && Math.hypot(dx, dy) > 3) drag.moved = true;
          if (drag.moved) {
            mapTransform.offsetX = drag.offsetX + dx;
            mapTransform.offsetY = drag.offsetY + dy;
            draw();
          }
          return;
        }

        const bot = botAt(point, botHitTolerance());
        if (bot) {
          mapHover.value = { kind: "bot", bot, x: point.x, y: point.y };
          mapCursor.value = "pointer";
          draw();
          return;
        }

        const zone = zoneAt(point);
        if (zone) {
          mapHover.value = { kind: "zone", zone, x: point.x, y: point.y };
          mapCursor.value = "pointer";
          draw();
          return;
        }

        if (mapHover.value) {
          mapHover.value = null;
          draw();
        }
        mapCursor.value = "grab";
      }

      function onMapPointerDown(event) {
        if (event.button !== 0 || !mapData.loaded) return;
        const point = canvasPoint(event);
        drag = { x: point.x, y: point.y, offsetX: mapTransform.offsetX, offsetY: mapTransform.offsetY, moved: false };
        mapFollowBotId = 0;
        mapCursor.value = "grabbing";
        mapCanvas.value.setPointerCapture?.(event.pointerId);
      }

      function onMapPointerUp(event) {
        if (!drag) return;
        const point = canvasPoint(event);
        const moved = drag.moved;
        drag = null;
        mapCursor.value = "grab";
        mapCanvas.value?.releasePointerCapture?.(event.pointerId);
        if (moved) return;

        const bot = botAt(point, botHitTolerance());
        if (bot) {
          focusInRoster(bot);
          return;
        }

        const zone = zoneAt(point);
        if (zone) openZone(zone.id);
      }

      function onMapPointerLeave() {
        if (drag) return;
        if (mapHover.value) {
          mapHover.value = null;
          draw();
        }
        mapCursor.value = "grab";
      }

      function onMapWheel(event) {
        if (!mapData.loaded) return;
        event.preventDefault();
        mapFollowBotId = 0;
        const point = canvasPoint(event);
        zoomAt(Math.exp(-event.deltaY * 0.0013), point.x, point.y);
      }

      // ------------------------------------------------------------------ map: navigation
      function openMap() {
        tab.value = "map";
        nextTick(() => {
          updateSize();
          if (fittedView !== mapData.view) resetView();
          else draw();
          refreshMap();
        });
      }

      function showOnMap(bot) {
        mapFocusBotId.value = bot.id;
        mapFollowBotId = bot.id;
        mapFocusPending = true;
        // The overview that places the bot's zone; a capital or a dungeon has no overview, so its own map.
        const next = bot.mapView || (bot.zoneId > 0 ? `zone:${bot.zoneId}` : "");
        if (next && next !== mapData.view) {
          mapData.view = next;
          mapData.kind = next.startsWith("zone:") ? "zone" : "overview";
          mapData.zone = null;
          mapData.bots = [];
          fittedView = "";
        }
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
        // Every screen can be bookmarked; the tab watcher below does the loading, so register it first.
        const initial = (location.hash || "").replace("#", "");
        watch(tab, (value) => {
          const hash = "#" + value;
          if (location.hash !== hash) history.replaceState(null, "", hash);
          if (value === "groups") loadGroups();
          else if (value === "battlegrounds" || value === "events") loadRvr();
          else if (value === "stats") loadStats();
          else if (value === "exchange") loadExchange();
          else if (value === "records") loadRecords(records.page);
          else if (value === "inspector" && !inspector.value) {
            const bot = bots.value.find((candidate) => candidate.id === selectedBotId.value)
              || bots.value.find((candidate) => candidate.online)
              || bots.value[0];
            if (bot) loadInspector(bot);
          }
        });
        if (initial === "map") openMap();
        else if (["server", "options", "groups", "rvr", "battlegrounds", "events", "records", "exchange", "stats", "inspector"].includes(initial))
          tab.value = initial;
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
          if (mapHover.value) draw();
        }, 1000);
        window.setInterval(() => {
          if (document.hidden) return;
          if (tab.value === "groups") loadGroups();
          else if (tab.value === "battlegrounds" || tab.value === "events") loadRvr();
        }, 4000);
        window.setInterval(() => {
          if (!document.hidden && tab.value === "stats") loadStats();
        }, 15000);
        document.addEventListener("keydown", keydown);
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
        // world screens
        groups, groupsLoaded, rvr, rvrLoaded, rvrObjectives, rvrUpdated, rvrRunning, objectivesColumns,
        selectedObjective, selectedObjectiveId, eventParticipants, participantColumns, selectObjective,
        groupMemberColumns, objectiveSelected, loadGroups, loadRvr, loadStats, loadExchange, loadRecords, loadInspector,
        eventRealm, eventKind, eventActiveOnly, eventSearch, eventKinds, eventRealms, filteredEvents,
        rvrBots, filteredRvrBots, rvrRealm, rvrSearch, sortRvr, rvrArrow,
        botNameWithMeetUp, botTaskRemaining, botFormation, groupTimerText, levelRange, memberLabel, timerClock,
        battlegroundRows, battlegroundColumns,
        stats, statsLoaded, statsTotals, statsBoards, statsBoardColumns, worldMoney,
        exchange, exchangeRows, exchangePageRows, exchangePageCount, exchangeColumns, exchangeListingColumns,
        exchangePurchaseColumns, exchangeGoPage,
        records, recordsColumns, recordsPageCount, recordsGoPage,
        inspector, inspectorBot, inspectorLoading, inspectorSection, inspectorSections, inspectorData, inspectorOverview,
        openGroups, openRvr, openBattlegrounds, openEvents, openStats, openExchange, openRecords, openInspector,
        inspectFromRoster,
        // population
        search, searchInput, realmFilter, onlineOnly, sortBy, arrow, page, pageSize,
        pageBots, pageCount, pageWindow, goPage, filteredBots, selectedBot, selectedBotId,
        bots,
        // map
        mapData, mapStage, mapCanvas, mapZoom, mapHover, mapCursor, mapBots, mapCurrentOverview,
        mapZonesWithBots, realmZoneGroups, currentZoneUnlisted, mapZonesOnline, mapSummary, tooltipStyle, mapLayer,
        paintedMaps, openMap, showOnMap, selectMapView, openZone, mapOverviewName, zoomBy, resetView,
        onMapPointerMove, onMapPointerDown, onMapPointerUp, onMapPointerLeave, onMapWheel,
      };
    },
  });

  app.component("simple-table", SimpleTable);
  app.mount("#app");
})();


