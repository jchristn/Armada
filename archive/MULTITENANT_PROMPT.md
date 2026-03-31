I would like you to build a plan in MULTITENANT.md for the Armada vessel (vsl_mmit8chk_PTvi4HamUKb) that is actionable, that is, a developer can annotate progress and completion.  This will be a substantial overhaul of Armada, and using Armada voyages and missions will be a fantastic test of Armada itself.

The goal is to make Armada multi-tenant and scalable to many users.  Currently Armada is a single user system, though there are some architecture elements in place that make the solution lend well to a multi-tenant, multi-user architecture.

Core requirements:
- Add the following object types in the backend: tenant, user, and credential
- Fence rows in the database with tenant ID and in some cases user ID
- APIs should inject tenant ID and/or user ID into data being passed to or retrieved from the database

Dashboard:
- Externalize and run as a React app
- Packaged for docker as jchristn77/armada-dashboard
- Login experience should be:
  - User opens browser and points to dashboard
  - Text box to enter their email address, and click "Login"
  - Tenants list associated with that email are retrieved
    - User not found?  Error, back to login email screen
    - One tenant found?  Keep that in memory for the next step
    - Multiple tenants found?  Present a "Choose your tenant" view where they pick the tenant they want to login to
  - View presented to allow user to enter password
    - Success, let the user in
    - Failure, error message and return to the email view
- On the main dashboard page, add up to three badges at the top
  - Tenant name
  - User email
  - Whether or not the user is an admin
- Include new section ADMINISTRATION, shown ONLY when the user IsAdmin = true
  - Include views for Tenants, Users, and Credentials
  - All three objects should have an "Active" flag, when "false", it's unusable (i.e. user cannot perform any API actions)
  
New APIs:
- POST /api/v1/authenticate
  - Take in an email/password OR use the bearer token from the request to generate a 24-hour session token
  - Authenticated calls using the sesion token should include the x-token header (define this in constants)
  - Can also call this API with a session token
  - On success, return back { "Success": true, "Token": "{token}", "ExpiresUtc": "yyyy-MM-ddTHH:mm:ss.ffffffZ" }
- GET /api/v1/whoami
  - Return back an object { "Tenant": { ... }, "User": { ... } } where user password is redacted
    - This will be used by the dashboard for understanding state
- API set for tenants, users, and credentials
  - ALL should be gated unless user has IsAdmin = true, EXCEPT:
  - GET /api/v1/tenants/{id} where {id} matches the user {id} if user IsAdmin = false
  - GET /api/v1/users/{id} where {id} matches the user {id} if user IsAdmin = false
  - GET /api/v1/credentials and GET /api/v1/credentials/{id} - anyone can do this, but need to ensure when user IsAdmin = false the only ones returned are associated with that user
- POST /api/v1/onboarding
  - Take in an OnboardingRequest object, process the request, return an OnboardingResult

Default data:
On initial system startup, the server should check the database to see if a tenant exists.  If no tenant exists:
- Create the first tenant "Default Tenant" and ID default
- Create the first user with email admin@armada with password of password and ID default and boolean IsAdmin set to true
- Create the first credential linking to tenant default and user default, with bearer token default, using ID default

Data model:
- It should be assumed that every table will have a tenant ID field at the BEGINNING, and likely a user ID field just after tenant ID
- Database implementations will need to have calls that require tenant ID and/or user ID, and variants that don't (for admins)

Docker:
- Create a docker/ directory with a compose.yaml that starts both the server and the dashboard
- Environment variable for the dashboard that indicates the server URL (should be the same as the URL the user would use if hitting the server directly)
- compose.yaml should persist docker/server/armada.json (settings) and docker/armada/db/ (database, add a .gitkeep here, will be used in case they are using sqlite), and docker/armada/logs/ (add a .gitkeep file here)
- Add a docker/factory/ directory with reset.bat and reset.sh which stop docker compose and reset the system back to factory settings (refer to C:\Code\AssistantHub\docker\factory for an example)

Code style:
- Do not violate code style
- No var, no tuple
- Using statements instead of declarations
- Using statements inside of namespace blocks
- XML documentation
- One entity per file
- Public things named LikeThis
- Private things named _LikeThat
- null check and value clamping on set, XML docs should have min/max/default values, use sensible defaults
- Do not access JsonProperty values directly, everything should have a named type
- Follow the DTO pattern of {type}Request and {type}Result for request/response objects

Test projects:
- Test projects will need to be updated to include new APIs and interfaces

Documentation:
- Update REST_API.md
- Update MCP_API.md
- Update Postman collection
- Update README.md and CHANGELOG.md AT THE END

Reference projects:
- Refer to the following projects for their multi-tenant architecture to use for design and implementation: c:\code\view\backend, c:\code\litegraph, c:\code\conductor, c:\code\partio

For the future:
These items should be CONSIDERED when designing the plan but are considered out-of-scope for now:
- Roles-based access control

Ask any questions necessary before building the plan.
