"use strict";

const POLL_DELAY_MS = 15_000;
const STALE_AFTER_MS = 45_000;
// local user state
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

// map elements to variables
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

// call the backend api 
async function api(path, options = {}) {
    const headers = new Headers(options.headers);
    // add the user Id as a header for basic auth
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

// set the status element message
function setStatus(message) {
    elements.status.textContent = message;
}


// show the next "page", make each sections elements visible
function showSection(section) {
    const sections = {
        users: elements.userSection,
        vehicles: elements.vehicleSection,
        dashboard: elements.dashboardSection
    };

    // update the sections, and move the status element to that slot if required
    const update = () => {
        for (const [name, element] of Object.entries(sections)) {
            element.hidden = name !== section;
        }
        sections[section].querySelector(".status-slot")?.append(elements.status);
    };

    update();
}

// name fallback
function displayName(value, fallback) {
    return typeof value === "string" && value.trim() ? value.trim() : fallback;
}

// user initial generator
function userInitials(name) {
    return name.split(/\s+/).slice(0, 2).map((part) => part[0]).join("").toUpperCase() || "U";
}

// function to create the user button on load
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

// function to create the vehicle buttons on user select
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

// render the elements related to the user
function renderSelectedUser() {
    const name = displayName(state.user?.displayName, "User");
    elements.vehicleUserAvatar.textContent = userInitials(name);
    elements.vehicleUserName.textContent = name;
    elements.dashboardUserName.textContent = name;
}


// grab the users, create the buttons and add them to the userOptions element
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
            elements.userOptions.append(createUserButton(user));
        }

        setStatus("Choose a user.");
    } catch (error) {
        setStatus(error.message);
    }
}

// user has been clicked, set the states, reset the vehicle states, load vehicles
async function selectUser(user) {
    stopPolling();
    state.user = user;
    state.vehicle = null;
    state.snapshot = null;
    state.refreshFailed = false;
    await loadVehicles();
}

// Load the vehicle page 
async function loadVehicles() {
    renderSelectedUser();
    showSection("vehicles");
    elements.vehicleOptions.replaceChildren();
    setStatus("Loading vehicles...");

    // fetch the vehicles, create elements and add buttons
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

// on user selecting a vehicle
function selectVehicle(vehicle) {
    stopPolling();
    state.vehicle = vehicle;
    state.snapshot = null;
    state.refreshFailed = false;
    state.scheduleDirty = false;
    elements.vehicleName.textContent = displayName(vehicle.displayName, "Vehicle");
    renderSelectedUser();
    // load the dashboard view
    showSection("dashboard");
    renderSnapshot();
    setStatus("Loading vehicle state...");
    refreshVehicleState(true);
}

// stop polling the state of the device
function stopPolling() {
    clearTimeout(state.pollTimer);
    state.pollTimer = null;
    state.refreshVersion += 1;
    state.refreshController?.abort();
    state.refreshController = null;
}
// set a timeout to poll
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

// poll the vehicle state for live updates
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

// helper to validate battery value (should be already validated but double check)
function validBattery(value) {
    return typeof value === "number" && Number.isFinite(value) && value >= 0 && value <= 100;
}

// parse the date into JS date 
function telemetryDate(value) {
    if (typeof value !== "string") {
        return null;
    }

    const parsed = new Date(value);
    return Number.isFinite(parsed.getTime()) ? parsed : null;
}

// convert UTC to browser localtime
function toLocalTimeValue(value) {
    const date = telemetryDate(value);
    if (!date) {
        return "";
    }

    const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
    return local.toISOString().slice(11, 16);
}

// convert selected time to the next time that time will with JS Date
function nextStartTime(value, now = new Date()) {
    const [hours, minutes] = value.split(":").map(Number);
    const start = new Date(now);
    start.setHours(hours, minutes, 0, 0);

    if (start <= now) {
        start.setDate(start.getDate() + 1);
    }

    return start;
}

// On state update, re-render the new states
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

// set the charging state and call the API
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
        // once we are sure the api request succeeded, we client side update the message
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

// save the schedule the user has set
async function saveSchedule(event) {
    event.preventDefault(); // dont make the page reload on submit
    if (!state.vehicle || state.schedulePending) {
        return;
    }

    const enabled = elements.scheduleEnabled.checked;
    const payload = { scheduleEnabled: enabled };

    // if the state is enabled we can go ahead and parse the time, and convert it to UTC
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
    // call the API now
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

// go back to selecting users
function showUserPicker() {
    stopPolling();
    state.user = null;
    state.vehicle = null;
    state.snapshot = null;
    loadUsers();
}

// go back to selecting vehicles
function showVehiclePicker() {
    stopPolling();
    state.vehicle = null;
    state.snapshot = null;
    state.scheduleDirty = false;
    loadVehicles();
}

// Add event listeners to elements
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

// load users on load
loadUsers();
