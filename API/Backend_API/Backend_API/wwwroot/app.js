"use strict";

const POLL_DELAY_MS = 15_000;
const STALE_AFTER_MS = 45_000;

const state = {
    user: null,
    vehicle: null,
    snapshot: null,
    pollTimer: null,
    refreshController: null,
    refreshVersion: 0,
    refreshFailed: false,
    commandPending: false,
    schedulePending: false,
    scheduleDirty: false
};

const elements = {
    userSection: document.querySelector("#user-section"),
    userOptions: document.querySelector("#user-options"),
    vehicleSection: document.querySelector("#vehicle-section"),
    vehicleOptions: document.querySelector("#vehicle-options"),
    vehicleUserAvatar: document.querySelector("#vehicle-user-avatar"),
    vehicleUserName: document.querySelector("#vehicle-user-name"),
    dashboardSection: document.querySelector("#dashboard-section"),
    dashboardUserName: document.querySelector("#dashboard-user-name"),
    vehicleName: document.querySelector("#vehicle-name"),
    batteryLevel: document.querySelector("#battery-level"),
    batteryArc: document.querySelector("#battery-arc"),
    batteryText: document.querySelector("#battery-text"),
    chargingIndicator: document.querySelector("#charging-indicator"),
    chargingState: document.querySelector("#charging-state"),
    telemetryTime: document.querySelector("#telemetry-time"),
    chargingButton: document.querySelector("#charging-button"),
    scheduleForm: document.querySelector("#schedule-form"),
    scheduleEnabled: document.querySelector("#schedule-enabled"),
    scheduleTime: document.querySelector("#schedule-time"),
    saveSchedule: document.querySelector("#save-schedule"),
    scheduleStatus: document.querySelector("#schedule-status"),
    changeUser: document.querySelector("#change-user"),
    dashboardChangeUser: document.querySelector("#dashboard-change-user"),
    changeVehicle: document.querySelector("#change-vehicle"),
    status: document.querySelector("#status")
};

async function api(path, options = {}) {
    const headers = new Headers(options.headers);

    if (state.user) {
        headers.set("X-User-Id", String(state.user.id));
    }

    if (options.body !== undefined) {
        headers.set("Content-Type", "application/json");
    }

    const response = await fetch(path, { ...options, headers });
    const text = await response.text();
    let body = null;

    if (text) {
        try {
            body = JSON.parse(text);
        } catch {
            body = null;
        }
    }

    if (!response.ok) {
        throw new Error(body?.error || `Request failed (${response.status}).`);
    }

    return body;
}

function setStatus(message) {
    elements.status.textContent = message;
}

function showSection(section, direction) {
    const sections = {
        users: elements.userSection,
        vehicles: elements.vehicleSection,
        dashboard: elements.dashboardSection
    };
    const update = () => {
        for (const [name, element] of Object.entries(sections)) {
            element.hidden = name !== section;
        }
        sections[section].querySelector(".status-slot")?.append(elements.status);
        if (direction) {
            document.getElementById(sections[section].getAttribute("aria-labelledby"))
                ?.focus({ preventScroll: true });
        }
    };

    if (!direction || typeof document.startViewTransition !== "function") {
        update();
        return;
    }

    document.documentElement.dataset.navigationDirection = direction;
    document.startViewTransition(update);
}

function displayName(value, fallback) {
    return typeof value === "string" && value.trim() ? value.trim() : fallback;
}

function userInitials(name) {
    return name.split(/\s+/).slice(0, 2).map((part) => part[0]).join("").toUpperCase() || "U";
}

function createUserButton(user) {
    const name = displayName(user.displayName, "User");
    const button = document.createElement("button");
    button.type = "button";
    button.className = "option-button user-option";

    const avatar = document.createElement("span");
    avatar.className = "option-avatar";
    avatar.setAttribute("aria-hidden", "true");
    avatar.textContent = userInitials(name);

    const label = document.createElement("span");
    label.className = "option-name";
    label.textContent = name;
    button.append(avatar, label);
    button.addEventListener("click", () => selectUser(user));
    return button;
}

function createVehicleButton(vehicle) {
    const name = displayName(vehicle.displayName, "Vehicle");
    const button = document.createElement("button");
    button.type = "button";
    button.className = "option-button vehicle-option";

    const image = document.createElement("img");
    image.className = "vehicle-option-image";
    image.src = "/vehicle.svg";
    image.alt = "";
    image.setAttribute("aria-hidden", "true");

    const label = document.createElement("span");
    label.className = "option-name";
    label.textContent = name;
    button.append(image, label);
    button.addEventListener("click", () => selectVehicle(vehicle));
    return button;
}

function renderSelectedUser() {
    const name = displayName(state.user?.displayName, "User");
    elements.vehicleUserAvatar.textContent = userInitials(name);
    elements.vehicleUserName.textContent = name;
    elements.dashboardUserName.textContent = name;
}

async function loadUsers(direction) {
    showSection("users", direction);
    elements.userOptions.replaceChildren();
    setStatus("Loading users...");

    try {
        const users = await api("/users");

        if (!Array.isArray(users) || users.length === 0) {
            setStatus("No users are available.");
            return;
        }

        for (const user of users) {
            elements.userOptions.append(createUserButton(user));
        }

        setStatus("Choose a user.");
    } catch (error) {
        setStatus(error.message);
    }
}

async function selectUser(user) {
    stopPolling();
    state.user = user;
    state.vehicle = null;
    state.snapshot = null;
    state.refreshFailed = false;
    await loadVehicles("forward");
}

async function loadVehicles(direction) {
    renderSelectedUser();
    showSection("vehicles", direction);
    elements.vehicleOptions.replaceChildren();
    setStatus("Loading vehicles...");

    try {
        const vehicles = await api("/vehicles");

        if (!Array.isArray(vehicles) || vehicles.length === 0) {
            setStatus("This user has no vehicles.");
            return;
        }

        for (const vehicle of vehicles) {
            elements.vehicleOptions.append(createVehicleButton(vehicle));
        }

        setStatus("Choose a vehicle.");
    } catch (error) {
        setStatus(error.message);
    }
}

function selectVehicle(vehicle) {
    stopPolling();
    state.vehicle = vehicle;
    state.snapshot = null;
    state.refreshFailed = false;
    state.scheduleDirty = false;
    elements.vehicleName.textContent = displayName(vehicle.displayName, "Vehicle");
    renderSelectedUser();
    showSection("dashboard", "forward");
    renderSnapshot();
    setStatus("Loading vehicle state...");
    refreshVehicleState(true);
}

function stopPolling() {
    clearTimeout(state.pollTimer);
    state.pollTimer = null;
    state.refreshVersion += 1;
    state.refreshController?.abort();
    state.refreshController = null;
}

function scheduleNextPoll(deviceId, requestVersion) {
    if (
        document.hidden ||
        state.vehicle?.deviceId !== deviceId ||
        state.refreshVersion !== requestVersion
    ) {
        return;
    }

    state.pollTimer = setTimeout(() => refreshVehicleState(true), POLL_DELAY_MS);
}

async function refreshVehicleState(announceFailure) {
    if (!state.vehicle || document.hidden) {
        return;
    }

    clearTimeout(state.pollTimer);
    const deviceId = state.vehicle.deviceId;
    const requestVersion = ++state.refreshVersion;
    state.refreshController?.abort();
    const controller = new AbortController();
    state.refreshController = controller;

    try {
        const snapshot = await api(
            `/vehicles/${encodeURIComponent(deviceId)}/state`,
            { signal: controller.signal }
        );

        if (state.vehicle?.deviceId !== deviceId || state.refreshVersion !== requestVersion) {
            return;
        }

        const firstLoad = state.snapshot === null;
        const recovered = state.refreshFailed;
        state.refreshFailed = false;
        state.snapshot = snapshot;
        renderSnapshot();
        if (firstLoad) {
            setStatus("Vehicle state loaded.");
        } else if (recovered) {
            setStatus("Vehicle state refreshed.");
        }
    } catch (error) {
        if (error.name !== "AbortError" && announceFailure) {
            state.refreshFailed = true;
            setStatus("Unable to refresh vehicle state. Retrying.");
        }
    } finally {
        if (state.refreshVersion === requestVersion) {
            state.refreshController = null;
            scheduleNextPoll(deviceId, requestVersion);
        }
    }
}

function validBattery(value) {
    return typeof value === "number" && Number.isFinite(value) && value >= 0 && value <= 100;
}

function telemetryDate(value) {
    if (typeof value !== "string") {
        return null;
    }

    const parsed = new Date(value);
    return Number.isFinite(parsed.getTime()) ? parsed : null;
}

function toLocalTimeValue(value) {
    const date = telemetryDate(value);
    if (!date) {
        return "";
    }

    const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
    return local.toISOString().slice(11, 16);
}

function nextStartTime(value, now = new Date()) {
    const [hours, minutes] = value.split(":").map(Number);
    const start = new Date(now);
    start.setHours(hours, minutes, 0, 0);

    if (start <= now) {
        start.setDate(start.getDate() + 1);
    }

    return start;
}

function renderSnapshot() {
    const snapshot = state.snapshot;
    const batteryKnown = validBattery(snapshot?.batteryPercentage);
    const chargingKnown = typeof snapshot?.isCharging === "boolean";
    const timestamp = telemetryDate(snapshot?.telemetryTimestampUtc);
    const telemetryFresh = timestamp && Date.now() - timestamp.getTime() <= STALE_AFTER_MS;

    const batteryValue = batteryKnown ? snapshot.batteryPercentage : 0;
    const batteryState = !batteryKnown
        ? "unavailable"
        : batteryValue > 50 ? "high" : batteryValue >= 20 ? "medium" : "low";
    elements.batteryLevel.style.setProperty("--battery-value", batteryValue);
    elements.batteryLevel.dataset.level = batteryState;
    const chargingState = !chargingKnown
        ? "unavailable"
        : snapshot.isCharging ? "charging" : "not-charging";
    elements.batteryLevel.dataset.charging = chargingState;
    elements.chargingIndicator.dataset.charging = chargingState;
    elements.batteryArc.style.strokeDashoffset = String(100 - batteryValue);
    if (batteryKnown) {
        elements.batteryLevel.setAttribute("aria-valuenow", String(batteryValue));
        elements.batteryLevel.setAttribute("aria-valuetext", `${batteryValue}% battery`);
    } else {
        elements.batteryLevel.removeAttribute("aria-valuenow");
        elements.batteryLevel.setAttribute("aria-valuetext", "Battery level unavailable");
    }
    elements.batteryText.textContent = batteryKnown
        ? `${batteryValue}%`
        : "—";
    elements.chargingState.textContent = chargingKnown
        ? snapshot.isCharging ? "Charging." : "Not charging."
        : "Charging state unavailable.";
    elements.telemetryTime.textContent = telemetryFresh
        ? `${timestamp.toLocaleString()}`
        : "No recent data.";
    elements.chargingButton.textContent = !chargingKnown
        ? "—"
        : state.commandPending
            ? snapshot.isCharging ? "Stopping…" : "Starting…"
            : snapshot.isCharging ? "Stop" : "Start";
    elements.chargingButton.setAttribute(
        "aria-label",
        !chargingKnown
            ? "Charging control unavailable"
            : state.commandPending
                ? snapshot.isCharging ? "Stopping charging" : "Starting charging"
                : snapshot.isCharging ? "Stop charging" : "Start charging");
    elements.chargingButton.disabled = !chargingKnown || state.commandPending;

    if (!snapshot) {
        elements.scheduleEnabled.checked = false;
        elements.scheduleTime.value = "";
    } else if (!state.scheduleDirty && !state.schedulePending) {
        elements.scheduleEnabled.checked = snapshot.scheduleEnabled === true;
        elements.scheduleTime.value = toLocalTimeValue(snapshot.chargingStartUtc);
    }

    elements.scheduleTime.disabled = !snapshot || !elements.scheduleEnabled.checked || state.schedulePending;
    elements.scheduleTime.required = elements.scheduleEnabled.checked;
    elements.scheduleEnabled.disabled = !snapshot || state.schedulePending;
    elements.saveSchedule.disabled = !snapshot || state.schedulePending;
    elements.saveSchedule.textContent = state.schedulePending
        ? "Saving…"
        : state.scheduleDirty ? "Save changes" : "Save schedule";

    if (!snapshot?.scheduleStatus) {
        elements.scheduleStatus.dataset.state = "unavailable";
        elements.scheduleStatus.textContent = "Schedule status unavailable.";
    } else if (snapshot.scheduleStatus === "rejected" && snapshot.scheduleError) {
        elements.scheduleStatus.dataset.state = "rejected";
        elements.scheduleStatus.textContent = `Schedule rejected: ${snapshot.scheduleError}`;
    } else {
        elements.scheduleStatus.dataset.state = snapshot.scheduleStatus;
        elements.scheduleStatus.textContent = `Schedule: ${snapshot.scheduleStatus}.`;
    }
}

async function setCharging() {
    if (typeof state.snapshot?.isCharging !== "boolean" || state.commandPending) {
        return;
    }

    const deviceId = state.vehicle.deviceId;
    const charging = !state.snapshot.isCharging;
    stopPolling();
    state.commandPending = true;
    renderSnapshot();
    setStatus(charging ? "Starting charging..." : "Stopping charging...");
    let resultMessage;

    try {
        await api(
            `/vehicles/${encodeURIComponent(deviceId)}/charging`,
            {
                method: "PUT",
                body: JSON.stringify({ charging })
            }
        );

        if (state.vehicle?.deviceId === deviceId) {
            state.snapshot = { ...state.snapshot, isCharging: charging };
        }
        resultMessage = charging ? "Charging started." : "Charging stopped.";
    } catch (error) {
        resultMessage = error.message;
    } finally {
        state.commandPending = false;
        if (state.vehicle?.deviceId === deviceId) {
            renderSnapshot();
            scheduleNextPoll(deviceId, state.refreshVersion);
            setStatus(resultMessage);
        }
    }
}

async function saveSchedule(event) {
    event.preventDefault();
    if (!state.vehicle || state.schedulePending) {
        return;
    }

    const enabled = elements.scheduleEnabled.checked;
    const payload = { scheduleEnabled: enabled };

    if (enabled) {
        const start = nextStartTime(elements.scheduleTime.value);
        if (!elements.scheduleTime.value || !Number.isFinite(start.getTime())) {
            setStatus("Choose a start time.");
            elements.scheduleTime.focus();
            return;
        }
        payload.startTime = start.toISOString();
    }

    state.schedulePending = true;
    renderSnapshot();
    setStatus("Saving schedule...");

    try {
        await api(
            `/vehicles/${encodeURIComponent(state.vehicle.deviceId)}/charging-schedule`,
            {
                method: "PATCH",
                body: JSON.stringify(payload)
            }
        );
        state.scheduleDirty = false;
        state.snapshot = {
            ...state.snapshot,
            scheduleEnabled: enabled,
            chargingStartUtc: enabled
                ? payload.startTime
                : state.snapshot?.chargingStartUtc ?? null,
            scheduleStatus: "pending",
            scheduleError: null
        };
        setStatus("Schedule update pending.");
    } catch (error) {
        setStatus(error.message);
    } finally {
        state.schedulePending = false;
        renderSnapshot();
    }
}

function showUserPicker() {
    stopPolling();
    state.user = null;
    state.vehicle = null;
    state.snapshot = null;
    loadUsers("backward");
}

function showVehiclePicker() {
    stopPolling();
    state.vehicle = null;
    state.snapshot = null;
    state.scheduleDirty = false;
    loadVehicles("backward");
}

elements.chargingButton.addEventListener("click", setCharging);
elements.scheduleForm.addEventListener("submit", saveSchedule);
elements.scheduleEnabled.addEventListener("change", () => {
    state.scheduleDirty = true;
    renderSnapshot();
});
elements.scheduleTime.addEventListener("input", () => {
    state.scheduleDirty = true;
    renderSnapshot();
});
elements.changeUser.addEventListener("click", showUserPicker);
elements.dashboardChangeUser.addEventListener("click", showUserPicker);
elements.changeVehicle.addEventListener("click", showVehiclePicker);
document.addEventListener("visibilitychange", () => {
    if (document.hidden) {
        stopPolling();
    } else if (state.vehicle) {
        refreshVehicleState(true);
    }
});

loadUsers();
