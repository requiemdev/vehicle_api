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
    dashboardSection: document.querySelector("#dashboard-section"),
    vehicleName: document.querySelector("#vehicle-name"),
    batteryLevel: document.querySelector("#battery-level"),
    batteryText: document.querySelector("#battery-text"),
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

function showSection(section) {
    elements.userSection.hidden = section !== "users";
    elements.vehicleSection.hidden = section !== "vehicles";
    elements.dashboardSection.hidden = section !== "dashboard";
}

function createOptionButton(label, onClick) {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = label;
    button.addEventListener("click", onClick);
    return button;
}

async function loadUsers() {
    showSection("users");
    elements.userOptions.replaceChildren();
    setStatus("Loading users...");

    try {
        const users = await api("/users");

        if (!Array.isArray(users) || users.length === 0) {
            setStatus("No users are available.");
            return;
        }

        for (const user of users) {
            elements.userOptions.append(
                createOptionButton(user.displayName, () => selectUser(user))
            );
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
    await loadVehicles();
}

async function loadVehicles() {
    showSection("vehicles");
    elements.vehicleOptions.replaceChildren();
    setStatus("Loading vehicles...");

    try {
        const vehicles = await api("/vehicles");

        if (!Array.isArray(vehicles) || vehicles.length === 0) {
            setStatus("This user has no vehicles.");
            return;
        }

        for (const vehicle of vehicles) {
            elements.vehicleOptions.append(
                createOptionButton(vehicle.displayName, () => selectVehicle(vehicle))
            );
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
    elements.vehicleName.textContent = vehicle.displayName;
    showSection("dashboard");
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

    if (batteryKnown) {
        elements.batteryLevel.value = snapshot.batteryPercentage;
    } else {
        elements.batteryLevel.removeAttribute("value");
    }
    elements.batteryText.textContent = batteryKnown
        ? `${snapshot.batteryPercentage}%`
        : "No recent data.";
    elements.chargingState.textContent = chargingKnown
        ? snapshot.isCharging ? "Charging." : "Not charging."
        : "Charging state unavailable.";
    elements.telemetryTime.textContent = telemetryFresh
        ? `Last update: ${timestamp.toLocaleString()}`
        : "No recent data.";
    elements.chargingButton.textContent = chargingKnown && snapshot.isCharging
        ? "Stop charging"
        : "Start charging";
    elements.chargingButton.disabled = !chargingKnown || state.commandPending;

    if (!snapshot) {
        elements.scheduleEnabled.checked = false;
        elements.scheduleTime.value = "";
    } else if (!state.scheduleDirty && !state.schedulePending) {
        elements.scheduleEnabled.checked = snapshot.scheduleEnabled === true;
        elements.scheduleTime.value = toLocalTimeValue(snapshot.chargingStartUtc);
    }

    elements.scheduleTime.disabled = !elements.scheduleEnabled.checked || state.schedulePending;
    elements.scheduleTime.required = elements.scheduleEnabled.checked;
    elements.scheduleEnabled.disabled = state.schedulePending;
    elements.saveSchedule.disabled = state.schedulePending;

    if (!snapshot?.scheduleStatus) {
        elements.scheduleStatus.textContent = "Schedule status unavailable.";
    } else if (snapshot.scheduleStatus === "rejected" && snapshot.scheduleError) {
        elements.scheduleStatus.textContent = `Schedule rejected: ${snapshot.scheduleError}`;
    } else {
        elements.scheduleStatus.textContent = `Schedule: ${snapshot.scheduleStatus}.`;
    }
}

async function setCharging() {
    if (typeof state.snapshot?.isCharging !== "boolean" || state.commandPending) {
        return;
    }

    const charging = !state.snapshot.isCharging;
    state.commandPending = true;
    renderSnapshot();
    setStatus(charging ? "Starting charging..." : "Stopping charging...");
    let resultMessage;

    try {
        const response = await api(
            `/vehicles/${encodeURIComponent(state.vehicle.deviceId)}/charging`,
            {
                method: "PUT",
                body: JSON.stringify({ charging })
            }
        );

        if (typeof response?.isCharging === "boolean") {
            state.snapshot = { ...state.snapshot, isCharging: response.isCharging };
        }
        resultMessage = charging ? "Charging started." : "Charging stopped.";
    } catch (error) {
        resultMessage = error.message;
    } finally {
        state.commandPending = false;
        renderSnapshot();
        await refreshVehicleState(false);
        setStatus(resultMessage);
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
    loadUsers();
}

function showVehiclePicker() {
    stopPolling();
    state.vehicle = null;
    state.snapshot = null;
    state.scheduleDirty = false;
    loadVehicles();
}

elements.chargingButton.addEventListener("click", setCharging);
elements.scheduleForm.addEventListener("submit", saveSchedule);
elements.scheduleEnabled.addEventListener("change", () => {
    state.scheduleDirty = true;
    elements.scheduleTime.disabled = !elements.scheduleEnabled.checked;
    elements.scheduleTime.required = elements.scheduleEnabled.checked;
});
elements.scheduleTime.addEventListener("input", () => {
    state.scheduleDirty = true;
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
