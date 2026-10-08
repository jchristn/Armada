// Cancels every voyage that is not finished yet on the throwaway Admiral (HOST_SERVER_URL, as the host reaches it),
// through the REST API as the default admin. The Build flows create a captain, and an idle captain would otherwise
// pick up the Pending missions the Operations flows seeded and dispatched, and start a real agent session.
var base = HOST_SERVER_URL;
var auth = http.post(base + '/api/v1/authenticate', {
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'admin@armada', password: 'password', tenantId: 'default' }),
});
if (!auth.ok) throw new Error('authenticate failed: ' + auth.status);
var token = json(auth.body).Token;
var headers = { 'X-Token': token };
var list = http.get(base + '/api/v1/voyages?pageSize=1000', { headers: headers });
if (!list.ok) throw new Error('list voyages failed: ' + list.status);
var voyages = json(list.body).Objects || [];
var cancelled = 0;
for (var i = 0; i < voyages.length; i++) {
  var v = voyages[i];
  if (v.Status === 'Complete' || v.Status === 'Cancelled' || v.Status === 'Failed') continue;
  var res = http.delete(base + '/api/v1/voyages/' + v.Id, { headers: headers });
  if (!res.ok) throw new Error('cancel voyage ' + v.Id + ' failed: ' + res.status);
  cancelled++;
}
output.cancelledVoyages = cancelled;
