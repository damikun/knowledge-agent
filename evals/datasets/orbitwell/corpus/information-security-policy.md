# Information Security Policy

**Policy ID:** ISP-001 · **Version:** 4.2 · **Owner:** Information Security Office · **Effective:** 1 February 2026 · **Contact:** security@orbitwell.example

## 1. Purpose and scope

This policy protects Orbitwell Robotics, our customers and the robots we connect to the Orbitwell Fleet cloud. It applies to all employees, contractors and interns, to every company device and account, and to any personal device used for Orbitwell work. Breaking this policy can lead to disciplinary action, and in serious cases to termination of employment or contract.

## 2. Passphrases

Long passphrases are easier to remember and harder to crack than short, complex passwords.

| Account type | Minimum length |
|---|---|
| Standard accounts (Orbitwell ID, email, business applications) | 16 characters |
| Privileged accounts (domain administration, Fleet production, network equipment, finance system administration) | 24 characters |

- Build passphrases from four or more unrelated words; spaces are allowed.
- There is **no forced periodic rotation**. Change your passphrase immediately if you suspect it has been exposed, or when the Information Security Office asks you to.
- New passphrases are checked against a list of known breached passwords and rejected if they appear on it.
- Never reuse an Orbitwell passphrase on any other service, and never share it with anyone. IT staff will never ask you for it.
- An account is locked after eight failed sign-in attempts; unlock it through the self-service portal, which requires MFA.

## 3. Password manager

**Ciphernest** is the only approved password manager at Orbitwell.

- Every employee receives a personal Ciphernest vault on their first day.
- Team credentials (for example test accounts or supplier portals) are stored in shared Ciphernest collections, never in spreadsheets, chat messages or wiki pages.
- Your Ciphernest master passphrase must meet the privileged-account length requirement, because it protects all your other credentials.
- Built-in browser password storage is disabled on company devices.
- If you leave Orbitwell, your vault is transferred to your manager under the supervision of the Information Security Office.

## 4. Multi-factor authentication (MFA)

- MFA is mandatory for every account that supports it and for all applications behind single sign-on.
- **Standard method:** push approval with number matching in the Orbitwell Authenticator app.
- **Hardware security keys** are mandatory for privileged accounts, Finance, People & Culture and anyone with access to Fleet production systems. Two keys are issued per person; keep the backup key at home in a safe place.
- SMS and voice-call codes are not permitted for any Orbitwell account.
- If you lose your phone or security key, contact the IT Service Desk. Temporary access codes are valid for one working day.

## 5. Devices

- All company laptops use **full-disk encryption (AES-256)**, enabled by IT before the device is handed over.
- Laptops and phones are enrolled in Orbitwell Device Manager, our mobile device management service. Do not remove or disable it.
- Screens lock automatically after **5 minutes** of inactivity. Lock your screen manually whenever you leave your device.
- Critical security updates must be installed within **72 hours** of release; all other updates within three weeks.
- Local administrator rights are not granted by default; request them through the IT Service Desk with a business reason.
- Personal phones may be used for email and chat only through the managed work profile. No Orbitwell data may be stored outside that profile.

## 6. Data classification

Every document, dataset and system is classified at one of four levels. The owner applies the label; unlabelled information is treated as INTERNAL.

| Level | Meaning | Examples | Key handling rules |
|---|---|---|---|
| PUBLIC | Approved for publication | Product brochures, published firmware release notes, job adverts | No restrictions |
| INTERNAL | Default for business information | Intranet pages, internal procedures, organisation charts | Share inside Orbitwell and with partners under NDA |
| CONFIDENTIAL | Disclosure would harm Orbitwell or a customer | Customer contracts, customer telemetry, source code, personnel files | Need-to-know access; encrypted in transit; external sharing only through the approved secure file transfer service |
| RESTRICTED | Disclosure would cause severe harm | Firmware signing keys, Fleet production secrets, payroll and health data, unpublished financial results | Named access list; encrypted at rest and in transit; never printed; never stored on removable media |

## 7. Removable media

- USB mass-storage devices are **blocked by default** on company laptops.
- Exceptions are requested through the IT Service Desk. Only Orbitwell-issued, hardware-encrypted OW-SecureStick drives (64 GB) are allowed, each registered to one person. An exception expires after six months.
- RESTRICTED data must never be copied to removable media. CONFIDENTIAL data may only be copied to an encrypted OW-SecureStick.
- Never plug in a USB drive you found or were given at an event. Hand it to the Security desk in Building A, ground floor, room A0.11.
- Service USB drives used by Field Service for robot firmware updates are managed separately and must never be connected to office laptops.

## 8. Reporting security incidents

Report anything that might be a security incident, even if you are not sure. Examples include:

- a lost or stolen laptop, phone, badge or security key;
- clicking a link or opening an attachment in a suspicious email;
- malware warnings or unexpected pop-ups;
- data sent to the wrong recipient;
- leaked passwords, API keys or customer Fleet credentials;
- unusual behaviour of robots or Fleet accounts that could indicate unauthorised access.

**Deadline:** report immediately, and in any case **within 60 minutes** of discovering the incident, at any time of day.

**How to report:** email **security@orbitwell.example** or call the 24/7 Security hotline on **internal extension 7110**. For lost devices, also tell your manager.

While waiting for the Security team:

1. Do not try to investigate on your own and do not delete anything.
2. Disconnect an affected laptop from the network, but do not switch it off.
3. Do not contact the suspected attacker or reply to the suspicious message.
4. Write down what happened and when, while it is fresh in your memory.

The Information Security Office classifies every incident from SEV-1 (critical) to SEV-4 (low) and coordinates any notifications to customers or authorities together with the Data Protection Officer.

## 9. Phishing awareness

- The Information Security Office runs **simulated phishing campaigns every six weeks**. Results are reported per department, never per person.
- Report suspicious emails with the "Report phishing" button in your mail client, or forward them to phish@orbitwell.example.
- Employees who click on two simulated phishing emails within one year complete a short refresher module within two weeks.
- The annual security awareness refresher must be completed by **30 November** each year.

## 10. Quick reference

| Topic | Rule |
|---|---|
| Passphrase length | 16 characters standard, 24 characters privileged |
| Password manager | Ciphernest only |
| MFA | Authenticator push with number matching; hardware keys for privileged roles; no SMS |
| Disk encryption | AES-256, mandatory |
| Screen lock | 5 minutes |
| Incident reporting | Within 60 minutes to security@orbitwell.example or extension 7110 |
| Phishing simulation cadence | 6 weeks |
| Annual refresher deadline | 30 November |

## 11. Exceptions

Exceptions to this policy must be requested in writing from the Information Security Office, include a risk assessment and an end date, and are reviewed quarterly. An exception is never granted for MFA on privileged accounts.
