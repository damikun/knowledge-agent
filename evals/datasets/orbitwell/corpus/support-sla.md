# Customer Support Service Level Agreement

**Document:** SUP-SLA-2026 · **Version:** 2.0 · **Effective:** 2 March 2026 · **Owner:** Customer Support · **Portal:** support.orbitwell.example

This Service Level Agreement (SLA) describes the support Orbitwell Robotics provides for OW-X7 robot arms, OW-CB2 control boxes, OW-TP10 teach pendants and the **Orbitwell Fleet** cloud service (the dashboard at fleet.orbitwell.example and the Fleet API). It forms part of every support contract. Where a signed contract states different terms, the contract prevails.

## 1. Support tiers

| Feature | Standard | Plus | Premier |
|---|---|---|---|
| How you get it | Included for two years after delivery of each robot | Annual subscription | Annual subscription |
| Support hours | Monday–Friday 08:00–17:00 CET, excluding Czech public holidays | Monday–Friday 07:00–20:00 CET | 24/7 for P1 and P2; Monday–Friday 07:00–20:00 CET for P3 and P4 |
| Channels | Support portal, email | Portal, email, phone | Portal, email, phone, 24/7 hotline, named Technical Account Manager |
| Remote diagnostics through Fleet | Yes, with customer approval | Yes, with customer approval | Yes, with customer approval |
| On-site engineer | Chargeable, best effort | Chargeable, scheduled within one week | Included for P1 hardware failures; on site within 48 hours in the EU |
| Advance replacement parts | No | Yes, standard shipping | Yes, express shipping |
| Quarterly service review | No | No | Yes |

## 2. Priority definitions

| Priority | Definition |
|---|---|
| P1 – Critical | Production is stopped on one or more robots, or Orbitwell Fleet is unavailable, and no workaround exists. All suspected security incidents are P1. |
| P2 – High | A major function is impaired; production continues at reduced capacity or with a workaround. |
| P3 – Normal | A minor function is affected, or the problem has little impact on production. |
| P4 – Low | Questions, documentation requests and feature suggestions. |

The customer chooses the initial priority. Orbitwell may adjust it after triage and will explain why.

## 3. Response times

The response time is the time until a qualified support engineer first responds to a ticket. It is not a resolution time.

| Priority | Standard | Plus | Premier |
|---|---|---|---|
| P1 – Critical | 4 business hours | 1 hour | 15 minutes |
| P2 – High | 8 business hours | 3 hours | 30 minutes |
| P3 – Normal | 2 business days | 1 business day | 6 business hours |
| P4 – Low | 5 business days | 3 business days | next business day |

- Business hours and business days are counted within the support hours of the customer's tier.
- The P1 and P2 targets apply to tickets opened by phone, through the hotline or in the support portal. Tickets sent only by email are triaged in order of arrival.
- For Premier P1 tickets, Orbitwell engineers work continuously until a workaround is in place.

## 4. Security incidents

Suspected security incidents involving robots or Fleet accounts, for example leaked API credentials, unknown sign-ins or jobs that nobody dispatched, are **always handled as P1**, whatever priority was selected and whatever the customer's tier.

- Customers should immediately rotate the affected client secrets in the Fleet dashboard and report the incident by phone or through the portal.
- Inside Orbitwell, the support engineer who receives the report also hands it over to the Information Security Office, following Orbitwell's internal Information Security Policy.

## 5. Orbitwell Fleet availability

- **Uptime commitment:** the Fleet dashboard and Fleet API are available at least **99.9%** of each calendar month for every customer with an active Fleet subscription.
- Monthly uptime = available minutes ÷ total minutes in the month, excluding scheduled maintenance, force majeure and problems in the customer's own network.
- Availability is measured by external probes every 60 seconds from three European regions.
- **Scheduled maintenance** takes place on Sundays between 01:00 and 03:00 CET, at most twice per month, and is announced at least 7 days in advance on status.fleet.orbitwell.example.
- Robots continue to run their programs locally while Fleet is unavailable; telemetry is uploaded once the connection returns.

## 6. Service credits

If monthly uptime falls below the commitment, the customer is entitled to a service credit on the monthly Fleet subscription fee:

| Monthly uptime | Service credit |
|---|---|
| 99.9% or higher | None |
| 99.0% to below 99.9% | 10% |
| 95.0% to below 99.0% | 25% |
| Below 95.0% | 50% |

- Request the credit through the support portal within **45 days** after the end of the affected month.
- Credits are applied to the next invoice and are never paid out in cash.
- Total credits for one month are capped at 50% of that month's Fleet subscription fee.
- Service credits are the customer's sole remedy for Fleet unavailability.

## 7. Escalation path

If a response target is missed or you are not satisfied with progress, escalate as follows:

| Level | Contact | When to use |
|---|---|---|
| 1 | Assigned support engineer | Default contact for every ticket |
| 2 | Support Team Lead, escalation@orbitwell.example | A response target was missed, or an agreed update did not arrive |
| 3 | Head of Customer Support | The issue is still unresolved after a Level 2 escalation, or P1 incidents keep recurring |
| 4 | Executive sponsor (Premier only), contacted through your Technical Account Manager | Business-critical disputes |

Escalations are acknowledged within 2 hours during the support hours of your tier. Premier customers can reach the 24/7 duty manager through the hotline for any P1 ticket.

## 8. Customer responsibilities

To help us meet these targets, customers:

- provide the robot serial number (format X7-YY-NNNNNN), the firmware version, the exact error code and a log bundle exported from the pendant;
- keep robots on a supported firmware version, meaning one of the two most recent minor versions;
- allow remote diagnostics through Orbitwell Fleet or name a contact who can perform checks on site;
- maintain a list of named support contacts in the portal (up to 2 for Standard, 4 for Plus and 8 for Premier).

## 9. Contact

- Support portal: support.orbitwell.example
- Email: support@orbitwell.example
- Phone and hotline numbers are listed in your support contract and in the portal.
- Fleet status page: status.fleet.orbitwell.example
