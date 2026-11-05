# OW-X7 Firmware Release Notes

**Product:** OW-X7 collaborative robot arm with OW-CB2 control box · **Current version:** 3.4.0 · **Classification:** PUBLIC

These notes list new features, fixes, known issues and upgrade instructions for each OW-X7 firmware release in 2026. Firmware packages are signed and can be downloaded through Orbitwell Fleet or the Orbitwell support portal. The installation procedure is described in the OW-X7 user manual.

## Version overview

| Version | Release date | Type | Status |
|---|---|---|---|
| 3.4.0 | 22 June 2026 | Feature release | Recommended for all robots |
| 3.3.1 | 7 April 2026 | Maintenance release | Supported |
| 3.3.0 | 16 March 2026 | Feature release | Superseded by 3.3.1 |
| 3.2.0 | 19 January 2026 | Feature release | No longer receives fixes; upgrade recommended |

Orbitwell provides fixes for the two most recent minor versions, currently 3.4 and 3.3. Robots on older versions should be upgraded at the next planned maintenance window.

---

## 3.4.0 – 22 June 2026

### New features

- **Conveyor tracking.** The arm can pick and place parts on a moving conveyor using a conveyor pulse signal wired to the tool I/O. Up to two conveyors can be configured per robot. Requires an OW-CB2 control box of hardware revision C or later.
- **Path blending.** A new blend-radius parameter produces smoother continuous paths. In typical pick-and-place programs, cycle time drops by up to 9%.
- **Fleet remote diagnostics.** Orbitwell support can request a log bundle remotely through Orbitwell Fleet. Nothing is sent until an operator approves the request on the pendant.
- **Pendant improvements.** Dark theme, plus Slovak and Hungarian user-interface languages.

### Fixes

- E-319 was raised after restoring a backup created on 3.2.x firmware. Older backups are now migrated automatically during the restore.
- The pendant froze when importing programs larger than 5 MB.
- Brake test results were not stored in the maintenance log when the test was started from Orbitwell Fleet.

### Known issues

- Conveyor tracking is not available on OW-CB2 revision B controllers. The pendant shows "Feature not supported" when you try to enable it.
- E-436 may appear for up to two minutes after Orbitwell Fleet rotates its server certificates. It clears automatically and needs no action.

### Upgrade notes

- **Upgrading directly from 3.2.x to 3.4.0 is not supported.** Install 3.3.1 first, then 3.4.0.
- Back up the safety configuration before updating, and re-confirm it after the update.
- Programs that use conveyor tracking cannot be loaded on robots running older firmware.

---

## 3.3.1 – 7 April 2026 (maintenance release)

### Fixes

- **Fixed spurious E-214 errors after a cold start** at ambient temperatures below 8 °C. The temperature compensation of the joint 2 output-side sensor was corrected, so the error is no longer raised when the arm is started in a cold hall.
- E-401 did not clear automatically when the network connection to Orbitwell Fleet was restored; operators previously had to restart the controller.
- The Payload Wizard reported an incorrect centre of gravity for tools mounted with a large offset from the flange.

### Upgrade notes

- Recommended for every robot running 3.3.0, and especially for robots installed in unheated halls, warehouses or sites where the heating is turned down at weekends.
- A direct upgrade from 3.2.x to 3.3.1 is supported.

---

## 3.3.0 – 16 March 2026

### New features

- **Hand guiding with force limit.** Operators can move the arm by hand to teach positions, with a configurable maximum guiding force.
- **More safety zones.** The number of configurable safety zones increases from 8 to 16.
- **Telemetry compression.** Upload bandwidth to Orbitwell Fleet drops by about 40%.
- **Maintenance reminders in Fleet.** Orbitwell Fleet now notifies users before hour-based maintenance tasks are due.

### Fixes

- E-117 was raised too early at ambient temperatures above 40 °C. The threshold now matches the specification.
- The pendant language was reset to English after every restart.

### Known issues

- E-214 may be reported incorrectly on joint 2 after a cold start at ambient temperatures below 8 °C. **Workaround:** leave the robot switched on in idle state for 10 minutes before starting production. Fixed in 3.3.1.

---

## 3.2.0 – 19 January 2026

### New features

- **Fleet API v2 job dispatch.** Robots can receive jobs dispatched through version 2 of the Orbitwell Fleet API (POST /jobs). Robots running firmware older than 3.2.0 only accept jobs through the legacy v1 API.
- **Payload Wizard.** Automatic estimation of payload mass and centre of gravity, started from the pendant in three guided steps.
- **New languages.** Czech and Polish user-interface languages on the pendant.

### Fixes

- E-152 was raised when the pendant cable was reconnected while the robot was idle.
- The robot clock drifted when the configured NTP server was unreachable; the controller now falls back to the Orbitwell Fleet time service.

### Upgrade notes

- Robots running firmware older than 3.1.0 cannot be upgraded directly. Contact Orbitwell support for an assisted upgrade.
- After upgrading, existing Fleet job integrations built on the v1 API keep working without changes.

---

## Reporting firmware problems

If you suspect a firmware defect, export a log bundle from the pendant and open a ticket through support.orbitwell.example. Include the robot serial number, the firmware version and the exact error code shown on the pendant.
