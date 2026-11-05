# Orbitwell Fleet API Reference

**API version:** v2 (current) · **v1 status:** deprecated, sunset on 31 January 2027 · **Audience:** customer developers and system integrators · **Changelog:** developers.fleet.orbitwell.example/changelog

The Orbitwell Fleet API gives programmatic access to the robots connected to Orbitwell Fleet: inventory and status, telemetry, job dispatch and event notifications through webhooks.

## 1. Basics

| Item | Value |
|---|---|
| Base URL (production) | `https://api.fleet.orbitwell.example/v2` |
| Base URL (sandbox) | `https://sandbox.api.fleet.orbitwell.example/v2` (data is reset every Sunday) |
| Format | JSON over HTTPS (TLS 1.2 or later), UTF-8 |
| Timestamps | RFC 3339, always in UTC |
| ID prefixes | `rbt_` robots, `job_` jobs, `whk_` webhooks, `site_` sites |

## 2. Authentication

The API uses the **OAuth 2.0 client credentials** grant. Create an API client in the Fleet dashboard under Settings → API clients. The client secret is shown only once, so store it in a secrets manager.

Request a token from the token endpoint:

```http
POST /oauth2/token HTTP/1.1
Host: auth.fleet.orbitwell.example
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials&client_id=<CLIENT_ID>&client_secret=<CLIENT_SECRET>&scope=robots:read telemetry:read jobs:write
```

```json
{
  "access_token": "eyJhbGciOi...",
  "token_type": "Bearer",
  "expires_in": 2700,
  "scope": "robots:read telemetry:read jobs:write"
}
```

- Access tokens are valid for **45 minutes** (`expires_in: 2700` seconds). There are no refresh tokens; request a new token shortly before the current one expires.
- Send the token in every request as `Authorization: Bearer <access_token>`.
- Rotate client secrets at least every 180 days. During rotation a client can have two active secrets.

| Scope | Grants |
|---|---|
| `robots:read` | Read robot inventory and status |
| `telemetry:read` | Read telemetry time series |
| `jobs:read` | Read jobs |
| `jobs:write` | Dispatch and cancel jobs |
| `webhooks:manage` | Create, list and delete webhooks |

## 3. Rate limits

| Applies to | Limit |
|---|---|
| All endpoints, per API client | 1,200 requests per minute |
| Telemetry endpoints | 240 requests per minute |
| `POST /jobs` | 60 requests per minute |

Every response includes `X-RateLimit-Limit`, `X-RateLimit-Remaining` and `X-RateLimit-Reset` (epoch seconds when the window resets).

When a limit is exceeded, the API responds with **HTTP 429 Too Many Requests** and a **`Retry-After`** header that states how many seconds to wait before retrying. Do not retry earlier; use exponential backoff with jitter for repeated failures.

## 4. Pagination

All list endpoints use **cursor pagination**.

- Query parameters: `limit` (default **50**, maximum **200**) and `cursor`.
- Responses contain `data`, `has_more` and **`next_cursor`**. Pass `next_cursor` as the `cursor` parameter to get the next page.
- When `next_cursor` is `null`, you have reached the last page.
- Cursors are opaque strings; never construct or modify them. A `limit` above 200 returns `400 invalid_parameter`.

```json
{
  "data": [ { "id": "rbt_7Q2K9X", "model": "OW-X7", "status": "online" } ],
  "has_more": true,
  "next_cursor": "c_9f8e7d6c"
}
```

## 5. Endpoints

### 5.1 Robots

| Method and path | Description |
|---|---|
| `GET /robots` | List robots. Filters: `site_id`, `model`, `status` (`online`, `offline`, `error`, `maintenance`) |
| `GET /robots/{robot_id}` | Robot details: serial number, model, firmware version, site, status, `last_heartbeat_at` and `active_errors` (for example `["E-305"]`) |

### 5.2 Telemetry

`GET /robots/{robot_id}/telemetry?from=…&to=…&resolution=…&metrics=…`

- `resolution`: `1s`, `1m` or `1h`. `metrics`: for example `joint_temperature`, `tcp_speed`, `power`.
- Retention: 1-second samples for 35 days, 1-minute aggregates for 13 months, 1-hour aggregates for 5 years.
- A single request at `1s` resolution may cover at most 12 hours.

### 5.3 Jobs

| Method and path | Description |
|---|---|
| `POST /jobs` | Dispatch a program to a robot. Body: `robot_id`, `program_id`, optional `parameters`, `priority` (`low`, `normal`, `high`), `not_before`. Returns `202 Accepted` with a job in status `queued`. |
| `GET /jobs` | List jobs, filterable by `robot_id` and `status` |
| `GET /jobs/{job_id}` | Job details. Statuses: `queued`, `dispatched`, `running`, `completed`, `failed`, `cancelled` |
| `POST /jobs/{job_id}/cancel` | Cancel a job that is still `queued` or `dispatched` |

`POST /jobs` accepts an `Idempotency-Key` header; keys are remembered for 24 hours, so a retried request never dispatches the same job twice.

### 5.4 Webhook management

| Method and path | Description |
|---|---|
| `POST /webhooks` | Register an HTTPS endpoint with a list of event types. The response contains the signing secret (`whsec_…`), shown only once. |
| `GET /webhooks` | List registered webhooks |
| `DELETE /webhooks/{webhook_id}` | Remove a webhook |

## 6. Webhooks

| Event | Sent when |
|---|---|
| `robot.error` | A robot reports a new error code |
| `robot.offline` | No heartbeat has been received from a robot for 3 minutes |
| `robot.online` | A robot reconnects |
| `job.completed` / `job.failed` | A job finishes successfully or fails |
| `maintenance.due` | An hour-based maintenance task is due within 100 operating hours |

**Delivery.** Fleet sends a `POST` with a JSON body to your endpoint. Respond with any 2xx status within 10 seconds. Otherwise the delivery is retried up to 10 times over 24 hours with exponential backoff. Endpoints that keep failing for 3 consecutive days are disabled automatically and the account administrators are notified by email.

**Signature verification.** Every delivery carries the header **`X-Orbitwell-Signature`**, for example `t=1767225600,v1=5f2b…`:

- `t` is the timestamp of the delivery attempt in epoch seconds.
- `v1` is the hex-encoded **HMAC-SHA256** of the string `{t}.{raw request body}`, computed with your webhook signing secret as the key.
- Reject deliveries whose timestamp is more than 300 seconds old, and compare signatures in constant time.
- `X-Orbitwell-Event` contains the event type and `X-Orbitwell-Delivery` a unique delivery ID you can use to discard duplicates.

Verification in pseudocode:

```text
parts    = parse "t=<timestamp>,v1=<signature>" from the X-Orbitwell-Signature header
payload  = parts.t + "." + raw_request_body
expected = hex( HMAC_SHA256( key = webhook_signing_secret, message = payload ) )
valid    = constant_time_equals(expected, parts.v1) and abs(now_epoch_seconds - parts.t) <= 300
```

## 7. Errors

Errors use `application/problem+json` with the fields `type`, `title`, `status`, `detail` and `request_id`. Quote the `X-Request-Id` response header when you contact support.

| Status | Meaning |
|---|---|
| 400 | Invalid parameter or body |
| 401 | Missing or expired access token |
| 403 | The token lacks the required scope |
| 404 | Resource not found |
| 409 | Conflict, for example cancelling a job that already finished |
| 410 | Gone: the API version has been retired |
| 429 | Rate limit exceeded; see `Retry-After` |
| 5xx | Server error; retry with exponential backoff |

## 8. Versioning and deprecation

- The major version is part of the URL path (`/v1`, `/v2`).
- Additive changes, such as new fields, endpoints or event types, can be released at any time without a new version. Clients must ignore unknown fields.
- Breaking changes are introduced only in a new major version, and a deprecated version is announced at least 12 months before it is retired.
- **v2** became generally available on 3 February 2026.
- **v1** has been deprecated since the same day. Its responses carry `Deprecation` and `Sunset` headers. **The v1 sunset date is 31 January 2027**; after that, every v1 request returns `410 Gone`.
- Main changes from v1 to v2: page-number pagination was replaced by cursors, robot IDs now use the `rbt_` prefix, and job dispatch moved to `POST /jobs`.
