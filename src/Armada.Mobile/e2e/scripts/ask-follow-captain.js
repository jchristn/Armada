// Creates the ClaudeCode captain named ask-follow-captain, unless it exists already, through the REST API as the
// default admin (HOST_SERVER_URL). scripts/mobile/run-e2e.sh --ask-follow starts the Admiral with
// scripts/mobile/stub-claude.py first on its PATH as "claude", so the captain streams a long scripted reply to every
// message instead of running a model.
var base = HOST_SERVER_URL;
var auth = http.post(base + '/api/v1/authenticate', {
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'admin@armada', password: 'password', tenantId: 'default' }),
});
if (!auth.ok) throw new Error('authenticate failed: ' + auth.status);
var headers = { 'X-Token': json(auth.body).Token, 'Content-Type': 'application/json' };
var captains = http.get(base + '/api/v1/captains?pageSize=1000', { headers: headers });
if (!captains.ok) throw new Error('list captains failed: ' + captains.status);
var existing = (json(captains.body).Objects || []).filter(function (c) { return c.Name === 'ask-follow-captain'; });
if (existing.length === 0) {
  var captain = http.post(base + '/api/v1/captains', {
    headers: headers,
    body: JSON.stringify({ Name: 'ask-follow-captain', Runtime: 'ClaudeCode' }),
  });
  if (!captain.ok) throw new Error('create captain failed: ' + captain.status + ' ' + captain.body);
  output.captainId = json(captain.body).Id;
} else {
  output.captainId = existing[0].Id;
}
