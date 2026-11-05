# OW-X7 Collaborative Robot Arm – User Manual

**Document:** OW-X7-UM-EN, revision D · **Applies to:** OW-X7 arm with OW-CB2 control box and OW-TP10 teach pendant, firmware 3.2.0 and later · **Manufacturer:** Orbitwell Robotics, Brno, Czech Republic

The OW-X7 is a six-axis collaborative robot arm for machine tending, assembly and inspection. Read this manual before installing or operating the robot.

## 1. Safety notes

A risk assessment of the complete application is required before production use. Only trained personnel may change the safety configuration, which is protected by the safety password.

## 2. Technical specifications

| Parameter | Value |
|---|---|
| Axes | 6 rotating joints (J1–J6) |
| Payload | 7 kg |
| Reach | 917 mm |
| Pose repeatability | ±0.03 mm |
| Arm weight | 24.6 kg |
| Maximum TCP speed | 2 m/s |
| Joint range | ±360° on all joints |
| Ingress protection | IP54 (arm), IP44 (OW-CB2 control box) |
| Power supply | 100–240 V AC, 50/60 Hz, single phase |
| Power consumption | 350 W typical, 1.1 kW peak |
| Operating temperature | 0–45 °C |
| Relative humidity | 10–90%, non-condensing |
| Noise level | below 62 dB(A) |
| Tool flange | 50 mm bolt circle, 4 × M6 threads |
| Tool I/O | 2 digital inputs, 2 digital outputs, 24 V supply up to 600 mA |
| Mounting | Floor, wall or ceiling, any orientation |
| Base footprint | Ø 158 mm |
| Arm cable | 6 m between arm and control box |

## 3. Installation requirements

### 3.1 Mounting

- Mount the arm on a rigid, flat base that can withstand a tilting torque of at least **650 N·m**.
- Use four M8 bolts, property class 8.8, tightened to **25 N·m**, plus the two supplied dowel pins for repeatable positioning.

### 3.2 Environment

- Ambient temperature 0–45 °C, without condensation.
- The arm is not suitable for high-pressure washdown or explosive atmospheres.

### 3.3 Control box OW-CB2

- Connect to a dedicated 16 A circuit protected by a 30 mA residual-current device.
- Keep a clearance of at least 100 mm on all ventilated sides of the control box.

### 3.4 Network and Orbitwell Fleet

- Connect port **LAN2** of the control box to a network with internet access to enable Orbitwell Fleet.
- Allow outbound TCP port **443** to fleet.orbitwell.example and NTP time synchronisation (UDP port 123). No inbound ports are required.
- The robot continues to run its programs locally if the Fleet connection is lost; telemetry is buffered and uploaded once the connection returns.

## 4. Maintenance schedule

Maintenance intervals are based on operating hours, shown on the pendant under Settings → System → Usage. Orbitwell Fleet sends a reminder 100 operating hours before each task is due.

| Interval | Task | Performed by |
|---|---|---|
| Every 500 hours | Visual inspection of arm, cables and connectors; clean the arm surface; check emergency stop and enabling device | Operator |
| Every 2,000 hours | Check base bolt torque; run the brake test on all joints (Settings → Service → Brake test); clean the control box air filter | Trained maintenance staff |
| Every 8,000 hours | Inspect joint seals; replace the control box filter mat; back up and verify the safety configuration | Trained maintenance staff |
| Every 20,000 hours or every 3 years, whichever comes first | Joint service: renew strain-wave gearbox grease and inspect bearings | Orbitwell certified service technician |

Record every maintenance task in the robot log (Settings → Service → Maintenance log).

## 5. Error codes

Error codes are grouped by range: **E-1xx** power and controller, **E-2xx** joints and drives, **E-3xx** safety, **E-4xx** connectivity and software. Active errors are shown on the pendant and, when connected, in Orbitwell Fleet.

| Code | Meaning | Remedy |
|---|---|---|
| E-101 | Supply voltage out of range (below 90 V or above 264 V AC) | Check the mains supply and any UPS, then restart the control box. |
| E-117 | Control box over-temperature (internal temperature above 60 °C) | Clean the air filter, restore the 100 mm clearance and lower the ambient temperature. |
| E-152 | Teach pendant communication lost | Check the pendant cable and connector and reconnect. Restart the control box if the error persists. |
| E-208 | Overcurrent on joint 1 | Verify payload and TCP settings, make sure the arm is not blocked, reduce acceleration. |
| E-214 | Encoder mismatch on joint 2: motor-side and output-side encoder readings differ by more than 0.5° | Power-cycle the robot. If the error returns, run the J2 joint calibration (Settings → Service → Calibration). If it still persists, contact support with a log bundle. |
| E-233 | Brake release failure on joint 3 | Check that nothing obstructs the joint and restart. If it repeats, stop using the robot and contact support. |
| E-305 | Protective stop: collision detected (external force above the configured limit) | Remove the obstacle, check the work area, acknowledge on the pendant and resume the program. |
| E-319 | Safety configuration checksum mismatch: the active safety configuration does not match the checksum stored in the safety controller | Open Settings → Safety, review the configuration and re-confirm it with the safety password. After restoring a backup, compare the checksum with the value recorded in your risk assessment. |
| E-342 | Speed limit exceeded in reduced mode | Lower the programmed speed inside reduced-speed zones and check the safety plane settings. |
| E-401 | Fleet connection lost: no heartbeat acknowledgement from Orbitwell Fleet for 120 s | Check the Ethernet cable, DNS and firewall rules for outbound TCP 443. The robot keeps running locally. |
| E-417 | Firmware image signature invalid | Download the firmware package again from Orbitwell Fleet or the support portal. Never install packages from other sources. |
| E-436 | Fleet client certificate expired | Make sure the robot clock is correct (NTP), then renew the certificate under Settings → Network → Fleet → Renew certificate. |

To create a log bundle for support, open Settings → Service → Export logs and save the file to a USB drive or send it through Orbitwell Fleet.

## 6. Firmware update procedure

Firmware can be updated over the air through Orbitwell Fleet or locally from a USB drive. Read the release notes first; some versions must be installed in a specific order.

**Before you start**

- Back up the robot programs and the safety configuration (Settings → System → Backup).
- Stop the program, put the robot in idle state and move the arm to a safe position.
- Make sure the power supply is stable for the whole update.

**Local update from USB**

1. Download the firmware package (file name `ow-x7-fw-<version>.owpkg`) from the Orbitwell support portal.
2. Copy the package to the root folder of a USB drive.
3. Insert the drive into the USB port on the front panel of the control box.
4. On the pendant, open Settings → System → Firmware update and select the package.
5. The pendant verifies the package signature. An invalid package is rejected with E-417.
6. Confirm with the safety password. The update takes about **12 minutes** and the controller restarts twice. Do not switch off the power.
7. Check the installed version under Settings → System → About.
8. Re-confirm the safety configuration and check that the checksum matches your records.
9. Run a test program at reduced speed before returning to production.

**Over-the-air update through Fleet**

Schedule the update in the Orbitwell Fleet dashboard. The robot downloads the package in the background, but installation only starts after an operator confirms it on the pendant.

The controller keeps two firmware banks. If an update is interrupted, it automatically starts the previous version. Downgrades are possible only to the previous minor version and should be agreed with Orbitwell support.

## 7. Identification and support

The serial number is printed on the label on the arm base in the format X7-YY-NNNNNN (for example X7-26-004812). Have it ready when you contact Orbitwell support through support.orbitwell.example; response times depend on your support plan.
