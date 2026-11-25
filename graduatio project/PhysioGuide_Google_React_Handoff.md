# PhysioGuide Google Login — React Integration Handoff

Prepared: 20 October 2026. Backend branch: `Asma-Backend`.

This document describes the existing implementation. It does not introduce new APIs. JSON property names and messages below match the source. Example IDs, timestamps, tokens, and profile details are illustrative, not captured success responses. Replace angle-bracket placeholders with actual runtime values.

## 1. Connection details and the two-computer requirement

### Current backend settings

```text
Backend HTTPS base URL: https://localhost:7133
Swagger:                https://localhost:7133/swagger
Alternative HTTP URL:   http://localhost:5222
Frontend dev origin:    http://localhost:5173
```

Google Web Client ID:

```text
564871365119-fsn3u0ssrangdvlsl2tqcup047utblgr.apps.googleusercontent.com
```

Google authentication is enabled in Development configuration. Default configuration remains disabled. This flow uses the public Web Client ID and does not require a secret in React.

### IMPORTANT: frontend and backend run on different computers

**`localhost` always means the computer running the browser.** A request made by React in the frontend teammate's browser to `https://localhost:7133` goes to the frontend teammate's computer, not the backend developer's computer. The current localhost URL will not connect those two devices.

Before testing across devices, agree on one of these arrangements:

1. Run a separately configured backend development instance on the frontend teammate's computer. Then its own localhost URL can work.
2. Provide a shared, reachable HTTPS development backend address. React must use that actual address as `API_BASE_URL`.

A shared address is NOT configured or verified by this handoff. Do not invent an address or assume replacing `localhost` with a LAN IP is enough: the backend must listen on a reachable interface, network access must be allowed, and the HTTPS certificate must be trusted and match the hostname. The localhost development certificate does not automatically work for another computer or a LAN IP.

Keep these three settings separate:

| Setting | Must identify |
|---|---|
| React `API_BASE_URL` | The backend address reachable from the frontend browser |
| Backend CORS allowed origin | The actual React page origin, including scheme and port |
| Google Cloud Authorized JavaScript origins | The actual React page origin, not the backend API URL |

React may still run at `http://localhost:5173` on the frontend computer while calling a reachable shared HTTPS backend. In that case the browser's React origin is still `http://localhost:5173`, which the current Development CORS policy permits. A different frontend origin requires an explicit backend CORS change and matching Google Cloud configuration.

Google Cloud origins and the shared-network setup have not been verified. No hosting, certificate, firewall, or configuration changes were performed while preparing this document.

## 2. API conventions

- All four endpoints use `POST` with `Content-Type: application/json`.
- None requires a pre-existing PhysioGuide Bearer token.
- Use the HTTPS backend URL directly. The application enables HTTPS redirection; HTTP redirects can interfere with cross-origin preflight requests.
- Combined limit: **20 Google endpoint requests per minute per remote IP**, with no queue.
- Configured request body limit: **32,768 bytes**.
- Responses are configured not to be cached.
- IDs in responses are JSON integers. Tokens, emails, messages, names, and hexadecimal values are strings. `requiresRegistration` is a boolean. `expiresAt` is an ISO 8601 timestamp string.
- All success responses shown contain the complete property set returned by that action. Do not assume additional fields such as `refreshToken`, `tokenType`, or `expiresIn` exist.

## 3. Full Google Identity Services flow

### Google/frontend preparation

1. Configure the actual frontend origin in this Web Client ID's Google Cloud Authorized JavaScript origins. Verify consent-screen/test-user restrictions with the Google Cloud project owner.
2. Load Google's GIS script once in the React HTML entry document:

   ```html
   <script src="https://accounts.google.com/gsi/client" async defer></script>
   ```

3. Wait for the script to load and for the React button container to mount before using `google.accounts.id`.
4. Use the GIS JavaScript callback flow below. This backend has no Google redirect-callback or OAuth authorization-code exchange endpoint.
5. Use a browser environment supporting `crypto.getRandomValues` and `crypto.subtle`. Localhost development is suitable; for a shared deployment use HTTPS.

Google references: [GIS setup and authorized origins](https://developers.google.com/identity/gsi/web/guides/get-google-api-clientid), [GIS JavaScript API, nonce, and credential callback](https://developers.google.com/identity/gsi/web/reference/js-reference).

### Step A: create the verifier and binding

Generate a fresh 32-byte random value and encode it as a **64-character hexadecimal string**, named `flowVerifier`. Compute:

```text
flowBinding = HEX(SHA256(UTF8(flowVerifier)))
```

Hash the verifier's exact text, NOT the original random bytes or hexadecimal-decoded bytes. Preserve the verifier text and its capitalization for later requests.

```javascript
const API_BASE_URL = "https://localhost:7133"; // Replace for two-computer testing.
const GOOGLE_CLIENT_ID =
  "564871365119-fsn3u0ssrangdvlsl2tqcup047utblgr.apps.googleusercontent.com";

const hex = bytes =>
  Array.from(bytes, byte => byte.toString(16).padStart(2, "0")).join("");

async function createGoogleFlowValues() {
  const flowVerifier = hex(crypto.getRandomValues(new Uint8Array(32)));
  const digest = await crypto.subtle.digest(
    "SHA-256",
    new TextEncoder().encode(flowVerifier)
  );
  const flowBinding = hex(new Uint8Array(digest)).toUpperCase();
  return { flowVerifier, flowBinding };
}
```

### Step B: obtain a server challenge

POST `{ "flowBinding": "..." }` to `/api/Auth/google/challenge`. Check the response status before using its body. Keep the returned `challengeId` and `nonce` associated with this flow.

### Step C: configure GIS with the SERVER nonce

Pass **`challenge.nonce` unchanged** to GIS. Do not substitute `challengeId`, `flowVerifier`, or `flowBinding` for the nonce.

The following is integration wiring, not a complete React component. `buttonElement` is your mounted DOM container; the three `on...` callbacks are frontend UI handlers. Initialize deliberately for one active flow, not on every React render. Ensure React effect reruns/Strict Mode do not create overlapping flows.

```javascript
async function postGoogle(path, body) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 30000);
  try {
    const response = await fetch(`${API_BASE_URL}/api/Auth/google/${path}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
      signal: controller.signal
    });
    // Some unexpected server/proxy failures may not contain JSON.
    const text = await response.text();
    let data;
    try { data = text ? JSON.parse(text) : null; }
    catch { data = { message: text || "Unexpected server response." }; }
    if (!response.ok) {
      const error = new Error(data?.message || data?.title || "Request failed.");
      error.status = response.status;
      error.data = data;
      throw error;
    }
    return data;
  } finally {
    clearTimeout(timer);
  }
}

async function initializeGoogleButton(buttonElement, {
  onAuthenticated,
  onRegistrationRequired,
  onError
}) {
  if (!window.google?.accounts?.id) {
    throw new Error("Wait for the Google Identity Services script to load.");
  }
  const { flowVerifier, flowBinding } = await createGoogleFlowValues();
  const challenge = await postGoogle("challenge", { flowBinding });
  let submitted = false;

  window.google.accounts.id.initialize({
    client_id: GOOGLE_CLIENT_ID,
    ux_mode: "popup",
    nonce: challenge.nonce,
    callback: async response => {
      if (submitted) return;
      submitted = true; // Avoid duplicate submissions for the same flow.
      try {
        const result = await postGoogle("sign-in", {
          idToken: response.credential,
          challengeId: challenge.challengeId,
          flowVerifier
        });
        if (result.accessToken) {
          onAuthenticated(result);
        } else if (result.requiresRegistration === true) {
          onRegistrationRequired({
            registrationTicket: result.registrationTicket,
            expiresAt: result.expiresAt,
            email: result.email,
            flowVerifier
          });
        } else {
          throw new Error("Unexpected Google sign-in response.");
        }
      } catch (error) {
        onError(error); // Offer a fresh flow; do not resubmit blindly.
      }
    }
  });
  window.google.accounts.id.renderButton(buttonElement, {
    type: "standard",
    text: "continue_with",
    theme: "outline",
    size: "large"
  });
}
```

Keep the verifier and registration ticket in temporary frontend state for the active flow. Do not put tokens/tickets/verifiers in URLs or logs. If the challenge expires while the button is displayed, restart with fresh flow values and a fresh challenge; do not keep submitting the expired flow. A page reload can restart the flow.

### Step D: handle sign-in or complete registration

- `accessToken` present: linked account signed in; use the returned stored role.
- `requiresRegistration === true`: show Patient/Center selection, collect required fields, and use exactly one registration route with the ticket and the SAME verifier.
- Patient registration returns a JWT. Center registration returns `Pending` with no JWT.
- Do not treat a Google credential or registration ticket as a PhysioGuide session.

### What the backend verifies

The backend uses Google's official .NET library for cryptographic verification. It requires the configured Web Client ID as audience, an accepted Google issuer, a nonexpired token, a verified valid-format email, and a nonempty Google subject. It checks the verifier binding and compares the token's signed nonce to its own stored nonce. The client does not supply an expected nonce to trust.

The stored external identity is `(Provider = "Google", ProviderSubject = verified sub)`. It is not identified by email.

## 4. POST /api/Auth/google/challenge

### Request

```json
{
  "flowBinding": "A8AE6E6EE929ABEA3AFCFC5258C8CCD6F85273E0D4626D26C7279F3250F77C8E"
}
```

`flowBinding`: required string, 64 hexadecimal characters. This specific sample hashes the UTF-8 text `0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef`. That verifier is only an example; generate random values in real use.

### 200 OK

```json
{
  "challengeId": "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
  "nonce": "FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210",
  "expiresAt": "2026-10-20T09:05:00+00:00"
}
```

Both hexadecimal values are independently generated 32-byte random values, returned as 64-character uppercase strings. Expiration is five minutes after issuance. Return the challenge ID exactly as issued; lookup is case-sensitive.

## 5. POST /api/Auth/google/sign-in

### Request

```json
{
  "idToken": "<Google ID token from response.credential>",
  "challengeId": "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
  "flowVerifier": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
}
```

All fields are required strings. `idToken` is at most 10,000 characters. Challenge ID and verifier must pass 64-hex-character validation. Send an ID token, not a Google OAuth access token. There is no separate `nonce` field in this request DTO.

### 200 OK — existing eligible Google-linked account

```json
{
  "message": "Login successful.",
  "accessToken": "<PhysioGuide JWT>",
  "userId": 12,
  "email": "patient@example.com",
  "role": "Patient"
}
```

An eligible Center receives the same structure with `"role": "Center"`. Email and role come from the existing PhysioGuide account. This response has no `requiresRegistration`, `patientId`, `centerId`, or Center `status` property.

### 200 OK — new Google identity requiring registration

```json
{
  "message": "Complete your Patient or Center profile.",
  "requiresRegistration": true,
  "registrationTicket": "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF",
  "expiresAt": "2026-10-20T09:07:00+00:00",
  "email": "patient@example.com"
}
```

This creates a registration ticket, not a User. There is no access token or User ID yet. Let the user select Patient or Center only for this new-account branch. Existing accounts keep their role.

## 6. Registration tickets and retry rules

- Format: opaque random 64-character uppercase hexadecimal string; NOT a JWT.
- Lifetime: **five minutes from ticket issuance**, independently of the challenge issuance time.
- Bound to the exact original `flowVerifier`.
- Single-use across BOTH registration endpoints, consumed atomically under a lock.
- Stored in server memory under a SHA-256 hash; its identity data comes only from the verified Google token.
- Removed BEFORE account-conflict checks and database writes. A server-side registration failure may consume the ticket even though no account is created. Start a fresh Google flow instead of repeatedly submitting it.
- Automatic DTO validation errors occur before the action and do not consume the ticket. A mismatching verifier does not consume a valid ticket either.
- Successful token verification consumes the challenge before account lookup. A subsequent sign-in `403`, `409`, or dependency failure may require a fresh challenge and Google token.
- API restart loses outstanding challenges/tickets. Multiple backend instances require a shared atomic store; current stores support one instance.
- If a request times out after submission, its outcome can be ambiguous. Do not promise that no account was created or retry registration endlessly. A fresh Google sign-in can identify a linked account that was successfully created.

## 7. POST /api/Auth/google/register/patient

### Request

```json
{
  "registrationTicket": "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF",
  "flowVerifier": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "phoneNumber": "+970599123456",
  "fullName": "Example Patient",
  "gender": "Female"
}
```

| Field | Type | Required | Validation |
|---|---|---|---|
| `registrationTicket` | string | Yes | 64 hexadecimal characters |
| `flowVerifier` | string | Yes | 64 hexadecimal characters |
| `phoneNumber` | string | Yes | Maximum 20 characters; .NET `[Phone]` validation |
| `fullName` | string | Yes | Maximum 150 characters |
| `gender` | string or null | No | Maximum 20 characters; no enum restriction |

Optional gender may be omitted. Required string validation rejects empty/whitespace-only values. Full name, phone, and non-null gender are trimmed.

Email, password, role, IDs, date of birth, city, and profile image are not defined request fields for this endpoint. The backend obtains email/subject from the ticket and assigns the role.

### 201 Created

```json
{
  "message": "Patient registered successfully.",
  "accessToken": "<PhysioGuide JWT>",
  "userId": 12,
  "patientId": 7,
  "email": "patient@example.com",
  "role": "Patient"
}
```

The code creates User, Patient, and ExternalLogin in a transaction. The User is active with a null password hash. React may enter the authenticated Patient area after a successful response.

Frontend registration submission, using values retained from the sign-in response:

```javascript
const result = await postGoogle("register/patient", {
  registrationTicket: pendingGoogle.registrationTicket,
  flowVerifier: pendingGoogle.flowVerifier,
  phoneNumber,
  fullName,
  gender: gender || null
});
// Handle result.accessToken and result.role; clear pendingGoogle on success.
```

## 8. POST /api/Auth/google/register/center

### Request

```json
{
  "registrationTicket": "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF",
  "flowVerifier": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "phoneNumber": "+970599123456",
  "centerName": "Example Physiotherapy Center",
  "city": "Ramallah",
  "location": "Example Street, Building 10",
  "latitude": 31.9,
  "longitude": 35.2
}
```

| Field | Type | Required | Validation |
|---|---|---|---|
| `registrationTicket` | string | Yes | 64 hexadecimal characters |
| `flowVerifier` | string | Yes | 64 hexadecimal characters |
| `phoneNumber` | string | Yes | Maximum 20 characters; `[Phone]` |
| `centerName` | string | Yes | Maximum 200 characters |
| `city` | string | Yes | Maximum 100 characters |
| `location` | string | Yes | Maximum 300 characters |
| `latitude` | number or null | No | -90 through 90 |
| `longitude` | number or null | No | -180 through 180 |

Coordinates may be omitted or null; the DTO does not require both together. Send JSON numbers, not empty strings. Phone, center name, city, and location are trimmed. `centerName` maps to `Center.NameArabic`; `location` maps to `Center.Address`. No email/password/role/status/ID field is defined in the request DTO.

### 201 Created

```json
{
  "message": "Center registered successfully and is pending admin approval.",
  "userId": 13,
  "centerId": 5,
  "email": "center@example.com",
  "role": "Center",
  "status": "Pending"
}
```

**No JWT is issued.** Show a pending-approval screen; do not create an authenticated frontend session. The backend transaction creates User, Center, and ExternalLogin, assigning an active passwordless User and `Pending` Center. The client cannot choose approval status.

Frontend submission:

```javascript
const result = await postGoogle("register/center", {
  registrationTicket: pendingGoogle.registrationTicket,
  flowVerifier: pendingGoogle.flowVerifier,
  phoneNumber,
  centerName,
  city,
  location,
  latitude: latitude ?? null,
  longitude: longitude ?? null
});
// Show result.message / Pending screen; clear pendingGoogle on success.
```

## 9. JWT, roles, approval, and existing accounts

Use the PhysioGuide JWT, not the Google ID token, on protected APIs:

```http
Authorization: Bearer <PhysioGuide accessToken>
```

Current JWT setup: HMAC SHA-256, issuer `PhysioGuideAPI`, audience `PhysioGuideClient`, configured lifetime 60 minutes. Claims identify the User and role. Google success responses do not include `tokenType`, `expiresIn`, a JWT `expiresAt` field, or a refresh token. There is no Google refresh endpoint in this implementation.

Store the access token using the frontend team's agreed session handling; avoid URLs/logs. Use the returned role for UI navigation, while backend authorization remains authoritative. Clear frontend session state at logout/expiry; do not treat client-side token decoding as authorization.

Existing Google-linked Patient sign-in requires an active User and a Patient profile. Unsupported roles or a missing Patient profile are blocked.

| Existing linked Center condition | Google sign-in behavior |
|---|---|
| Active User, Center exactly `Approved` | 200 with JWT |
| Active User, `Pending` | 403 `center_not_approved` |
| Active User, `Rejected` | 403 `center_not_approved` |
| Active User, `Suspended` | 403 `center_not_approved` |
| Active User, missing Center or any other status | 403 `center_not_approved` |
| Inactive User | 403 `account_unavailable` |

The Google error does not include actual Center status or a rejection reason. After approval, the user starts a fresh Google sign-in flow.

If verified Google email matches an existing unlinked email/password account, the backend returns **409 `account_conflict`**. It never automatically links or changes the existing role. Ask the user to use the existing sign-in method. No explicit account-linking endpoint is implemented.

Google-only users have no local password; existing password verification safely rejects a null password hash. Existing password signup/login contracts remain separate.

## 10. Development CORS and expected preflight

Current Development policy allows only:

```text
http://localhost:5173
http://127.0.0.1:5173
```

Allowed methods: `GET, HEAD, POST, PUT, PATCH, DELETE, OPTIONS`. All headers are allowed, including `Content-Type` and `Authorization`. No wildcard origin or credentialed-cookie support is configured. Do not add `credentials: "include"` for this flow. CORS runs after routing and before rate limiting/authentication/authorization, and only when the environment is Development.

Expected browser preflight request:

```http
OPTIONS /api/Auth/google/challenge
Origin: http://localhost:5173
Access-Control-Request-Method: POST
Access-Control-Request-Headers: content-type,authorization
```

Expected from configuration: 204 with an allowed-origin header for that origin and permission for the requested method/headers. This is **not a captured runtime response**. An assistant-side attempt was blocked before HTTP by socket permissions. Swagger testing is same-origin and does not prove React CORS works.

Rate-limited responses set `Retry-After: 60`, but CORS does not expose that header to browser JavaScript. Do not depend on reading it cross-origin.

## 11. Error contract

Controller-defined errors contain string `message` and `code` properties:

```json
{
  "message": "Google sign-in could not be verified. Start again.",
  "code": "invalid_google_credentials"
}
```

Invalid registration ticket example:

```json
{
  "message": "Registration ticket is invalid, expired, or used. Start Google sign-in again.",
  "code": "invalid_registration_ticket"
}
```

| HTTP | Code | Exact message |
|---|---|---|
| 401 | `invalid_google_credentials` | `Google sign-in could not be verified. Start again.` |
| 401 | `invalid_registration_ticket` | `Registration ticket is invalid, expired, or used. Start Google sign-in again.` |
| 403 | `account_unavailable` | `This account cannot sign in.` |
| 403 | `center_not_approved` | `The center must be approved before signing in.` |
| 409 | `account_conflict` | `An account already exists. Sign in using its existing method; accounts are not automatically linked.` |
| 429 | `rate_limit_exceeded` | `Too many Google authentication requests. Try again later.` |
| 503 | `google_auth_unavailable` | `Google authentication is unavailable.` |
| 503 | `google_auth_unavailable` | `Google authentication is temporarily unavailable. Start again.` |

Meaning:

- Invalid/expired Google token, expired/unknown/consumed challenge, nonce mismatch, or wrong verifier: 401 `invalid_google_credentials` when validation rejects the credentials.
- Invalid/expired/used ticket or wrong ticket verifier: 401 `invalid_registration_ticket`.
- Existing unlinked email, registration email/subject collision, or handled SQL Server duplicate-key race: 409 `account_conflict`.
- Disabled/unconfigured GoogleAuth: 503. Enabling invalid configuration can also fail application startup validation.
- Handled Google network/timeout, store capacity, or database dependency error: 503.

### 400 validation errors have a DIFFERENT shape

`[ApiController]` automatically handles DTO validation. An illustrative body is:

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "PhoneNumber": [
      "The PhoneNumber field is required."
    ]
  }
}
```

Framework fields such as `type` and `traceId` may also appear. Exact validation keys/messages depend on the input. Handle `errors` as well as `message`/`code`; do not expect one universal error shape. Oversized bodies may produce 413; unsupported media types may produce 415. Unexpected 500 responses are not all normalized to JSON by the current implementation.

## 12. Verified behavior versus outstanding testing

### User-reported manual Swagger results at https://localhost:7133

| Check | Reported observation |
|---|---|
| POST challenge | 200 OK |
| POST sign-in with fake credentials | 401 `invalid_google_credentials` |
| POST Patient registration with fake ticket | 401 `invalid_registration_ticket` |
| POST Center registration with fake ticket | 401 `invalid_registration_ticket` |

These results establish those basic success/error paths only. They do not establish successful Google identity verification or registration. This document does not have captured full response bodies from the manual session; examples are based on source code.

### Other evidence

- Main project previously built successfully to a separate output folder with zero warnings/errors. No build or tests were run to create this document.
- Development CORS policy and pipeline order were reviewed in source.
- One assistant-side HTTPS OPTIONS attempt to the running API failed with `SocketError.AccessDenied`; it produced no HTTP status/body. It was not retried.
- Prior isolated loopback startup also logged an environment-specific Data Protection permission error but still started. The probe's socket failure was separate from URL matching or an application HTTP error.

### Must still be verified using real Google credentials and React

1. Reachable backend URL from the frontend computer, trusted HTTPS, correct Google Cloud frontend origins, and actual browser CORS preflight.
2. Genuine GIS token with the server nonce and exact verifier/hash sequence.
3. New-user sign-in returning a ticket; successful Patient registration/JWT and subsequent linked sign-in.
4. Successful Center registration returning Pending without JWT; blocked Pending/Rejected/Suspended login; Approved login success.
5. Existing email/password collision without automatic linking; inactive accounts and role handling.
6. Expiration/replay, single-use tickets, concurrent requests, uniqueness conflicts, and transactional rollback.
7. Frontend cancellation, validation messages, rate limiting, server failure, and lost-response handling.

Any success-path registration test creates account records and requires separate authorization/test-data planning. No real Google login, account creation, or database operation was performed while preparing this document.

## 13. Implementation limits to retain during handoff

- Challenge and ticket stores are bounded, process-local, and single-instance; API restarts invalidate pending flows.
- Center/account checks occur before JWT issuance, without synchronization against concurrent admin changes. Already-issued JWTs are not automatically revoked by later account/status changes in the shown authentication configuration.
- No Google linking, refresh, or profile-completion-after-account-creation endpoint is added by this flow. Required registration profile fields are supplied before creating the account.
- Some success/error branches remain code-reviewed rather than runtime-tested. Anonymous response shapes are defined in controller code; do not rely on Swagger to infer complete success schemas.
- The backend needs Google's verification keys when validating genuine tokens; browser access to GIS alone does not prove the backend can complete verification.

## 14. Source of this contract

Reviewed implementation files in the API project:

- [GoogleAuthController.cs](<C:/Users/lenovo/Desktop/graduation project/graduatio project/graduatio project/Controllers/GoogleAuthController.cs>) — routes, response shapes, account rules, transactions, errors.
- [GoogleAuthDtos.cs](<C:/Users/lenovo/Desktop/graduation project/graduatio project/graduatio project/DTOs/Auth/GoogleAuthDtos.cs>) — request fields and validation.
- [GoogleTokenValidator.cs](<C:/Users/lenovo/Desktop/graduation project/graduatio project/graduatio project/Services/GoogleTokenValidator.cs>) — Google token and challenge verification.
- [GoogleNonceStore.cs](<C:/Users/lenovo/Desktop/graduation project/graduatio project/graduatio project/Services/GoogleNonceStore.cs>) — nonce lifetime and verifier binding.
- [GoogleRegistrationTicketStore.cs](<C:/Users/lenovo/Desktop/graduation project/graduatio project/graduatio project/Services/GoogleRegistrationTicketStore.cs>) — ticket lifetime and atomic single-use handling.
- [TokenService.cs](<C:/Users/lenovo/Desktop/graduation project/graduatio project/graduatio project/Services/TokenService.cs>) — PhysioGuide JWT generation.
- [Program.cs](<C:/Users/lenovo/Desktop/graduation project/graduatio project/graduatio project/Program.cs>) — registration, rate limiting, JWT middleware, CORS, HTTPS redirection.
- [appsettings.Development.json](<C:/Users/lenovo/Desktop/graduation project/graduatio project/graduatio project/appsettings.Development.json>) — enabled Development Client ID.
- [launchSettings.json](<C:/Users/lenovo/Desktop/graduation project/graduatio project/graduatio project/Properties/launchSettings.json>) — local launch URLs.

Local file links reflect the backend developer's computer; the frontend teammate's checkout may be elsewhere.
