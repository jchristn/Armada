/* global MESSAGE */
// Posts to the newest Ask conversation from the host, through the REST API as the default admin (HOST_SERVER_URL),
// so the result reaches the app over the WebSocket while the app sits idle, the way a tracker milestone, progress
// message, or work report does: with MESSAGE set, a user message (the captain then streams its reply); otherwise the
// /status quick action (its action result).
var base = HOST_SERVER_URL;
var auth = http.post(base + '/api/v1/authenticate', {
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'admin@armada', password: 'password', tenantId: 'default' }),
});
if (!auth.ok) throw new Error('authenticate failed: ' + auth.status);
var headers = { 'X-Token': json(auth.body).Token, 'Content-Type': 'application/json' };
var list = http.post(base + '/api/v1/ask/threads/enumerate', { headers: headers, body: '{}' });
if (!list.ok) throw new Error('list conversations failed: ' + list.status);
var threads = json(list.body).Objects || [];
var newest = null;
for (var i = 0; i < threads.length; i++) {
  if (!newest || String(threads[i].LastMessageUtc) > String(newest.LastMessageUtc)) newest = threads[i];
}
if (!newest) throw new Error('no Ask conversation to post to');
var message = typeof MESSAGE === 'undefined' ? '' : MESSAGE;
var res = message
  ? http.post(base + '/api/v1/ask/threads/' + newest.Id + '/messages', { headers: headers, body: JSON.stringify({ content: message }) })
  : http.post(base + '/api/v1/ask/threads/' + newest.Id + '/actions', { headers: headers, body: JSON.stringify({ toolName: 'status', arguments: {} }) });
if (!res.ok) throw new Error('post to ' + newest.Id + ' failed: ' + res.status + ' ' + res.body);
output.postedThread = newest.Id;
