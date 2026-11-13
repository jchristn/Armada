// Example-replay mock of the Admiral's black box REST routes (src/Armada.Server/Routes/BlackBoxRoutes.cs).
// responses.json holds the saved examples of the Postman "Black Box" folder, captured from a real Admiral.
// Every id resolves to the same sample entry; ids starting bbx_doesnotexist return 404 BlackBoxNotFound.
// The recovery route keeps the confirm-token contract: no token is 409, a dry run issues a token, the token is single use.
const http = require('http');
const examples = require('./responses.json');
const issued = new Set();
let tokenCount = 0;

const E = '/api/v1/blackbox/([^/?]+)';
const routes = [
  ['GET', '^/api/v1/missions/[^/]+/blackbox$', 'missionEntry'],
  ['GET', '^/api/v1/missions/[^/]+/blackbox/brief$', 'missionBrief'],
  ['GET', '^/api/v1/missions/[^/]+/blackbox/entries$', 'missionEntries'],
  ['POST', '^/api/v1/missions/[^/]+/blackbox/entries/enumerate$', 'missionEntriesEnum'],
  ['GET', '^/api/v1/missions/[^/]+$', 'mission'],
  ['GET', '^/api/v1/voyages/[^/]+/blackbox$', 'voyageEntry'],
  ['GET', '^/api/v1/voyages/[^/]+/blackbox/brief$', 'voyageBrief'],
  ['GET', '^/api/v1/blackbox$', 'list'],
  ['POST', '^/api/v1/blackbox/enumerate$', 'listEnum'],
  ['POST', '^/api/v1/blackbox/search$', 'search'],
  ['POST', '^/api/v1/blackbox/bundle$', 'bundle'],
  ['POST', '^/api/v1/blackbox/hotspots/enumerate$', 'hotspots'],
  ['POST', '^/api/v1/blackbox/fingerprints/enumerate$', 'fingerprints'],
  ['GET', '^/api/v1/blackbox/fingerprints/[^/]+/trend$', 'trend'],
  ['POST', '^/api/v1/blackbox/delete$', 'deleteMany'],
  ['POST', '^/api/v1/blackbox/purge$', 'purge'],
  ['POST', '^/api/v1/blackbox/gc$', 'gc'],
  ['GET', '^/api/v1/blackbox/retention$', 'retention'],
  ['GET', '^/api/v1/blackbox/storage$', 'storage'],
  ['POST', '^/api/v1/blackbox/storage/migrate$', 'migrate'],
  ['GET', '^/api/v1/blackbox/storage/migrations/[^/]+$', 'migration'],
  ['POST', '^/api/v1/blackbox/backfill$', 'backfill'],
  ['GET', '^' + E + '$', 'entry'],
  ['DELETE', '^' + E + '$', 'deleteOne'],
  ['GET', '^' + E + '/brief$', 'entryBrief'],
  ['GET', '^' + E + '/sections/(Subject|Status|Site|Mechanism|Classification|Workspace|Conflict|Scope|Configuration|Attempts|Evidence|Analysis|Related|Voyage|Redaction|Truncation)$', 'analysis'],
  ['GET', '^' + E + '/sections/[^/]+$', 'sectionBogus'],
  ['GET', '^' + E + '/objects$', 'objects'],
  ['POST', '^' + E + '/objects/enumerate$', 'objectsEnum'],
  ['GET', '^' + E + '/objects/[^?]*\\.md$', 'objectBrief'],
  ['GET', '^' + E + '/objects/.+$', 'object'],
  ['GET', '^' + E + '/download$', 'download'],
  ['GET', '^' + E + '/notes$', 'notes'],
  ['POST', '^' + E + '/notes$', 'noteAdd'],
  ['POST', '^' + E + '/notes/enumerate$', 'notesEnum'],
  ['POST', '^' + E + '/resolve$', 'resolve'],
  ['POST', '^' + E + '/pin$', 'pin'],
  ['POST', '^' + E + '/unpin$', 'unpin'],
  ['POST', '^' + E + '/recovery/[^/]+$', 'recovery'],
  ['POST', '^' + E + '/recommendations/[^/]+/apply$', 'recommendation'],
  ['POST', '^' + E + '/reanalyze$', 'reanalyze'],
  ['POST', '^' + E + '/export-fixture$', 'fixture']
].map(([method, pattern, key]) => ({ method, regex: new RegExp(pattern), key }));

function replay(res, key) {
  const example = examples[key];
  const headers = Object.assign({ 'Content-Type': example.contentType }, example.headers || {});
  let payload;
  if (example.base64 !== undefined) payload = Buffer.from(example.base64, 'base64');
  else if (example.json !== undefined) payload = Buffer.from(JSON.stringify(example.json));
  else payload = Buffer.from(example.text || '');
  headers['Content-Length'] = payload.length;
  res.writeHead(example.code, headers);
  res.end(payload);
}

function send(res, status, body) {
  res.writeHead(status, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify(body));
}

// GET /api/v1/blackbox filters the sample page by the query (step, subjectType, missionId, isCurrent, ...) and pages it.
function list(res, query) {
  const filters = { step: 'Step', subjectType: 'SubjectType', phase: 'Phase', missionId: 'MissionId', voyageId: 'VoyageId', vesselId: 'VesselId',
    triage: 'Triage', resolution: 'Resolution', state: 'State', fingerprint: 'Fingerprint', isCurrent: 'IsCurrent', pinned: 'Pinned' };
  let rows = examples.list.json.Objects;
  for (const [param, field] of Object.entries(filters)) {
    const value = query.get(param);
    if (value) rows = rows.filter(row => String(row[field]).toLowerCase() === value.toLowerCase());
  }
  const pageSize = Math.max(1, Math.min(1000, Number(query.get('pageSize')) || 10));
  const pageNumber = Math.max(1, Number(query.get('pageNumber')) || 1);
  return send(res, 200, { Success: true, PageNumber: pageNumber, PageSize: pageSize, TotalPages: Math.ceil(rows.length / pageSize), TotalRecords: rows.length,
    Objects: rows.slice((pageNumber - 1) * pageSize, pageNumber * pageSize), TotalMs: 0 });
}

function recovery(res, body) {
  if (body.DryRun !== false) {
    const token = 'bbc_' + (Date.now().toString(16) + (++tokenCount).toString(16)).padStart(32, '0').slice(-32);
    issued.add(token);
    const dry = Object.assign({}, examples.recoveryDry.json, { ConfirmToken: token, ConfirmTokenExpiresUtc: new Date(Date.now() + 600000).toISOString() });
    return send(res, 200, dry);
  }
  if (!body.ConfirmToken) return replay(res, 'recoveryRefused');
  if (!issued.delete(body.ConfirmToken)) return replay(res, 'recoveryReuse');
  return replay(res, 'recoveryRun');
}

http.createServer(async (req, res) => {
  try {
    const auth = req.headers.authorization || '';
    if (!auth.startsWith('Bearer ')) return send(res, 401, { Error: 'Unauthorized', StatusCode: 401, Description: 'Authentication is required.', Message: 'Send Authorization: Bearer <token>.' });
    let raw = ''; for await (const chunk of req) raw += chunk;
    const body = typeof req.body === 'object' && req.body !== null ? req.body : JSON.parse(req.body || raw || '{}');
    const path = decodeURIComponent(req.url.split('?')[0]).replace(/\/+$/, '');
    const route = routes.find(r => r.method === req.method && r.regex.test(path));
    if (!route) return send(res, 501, { Error: 'NotImplemented', StatusCode: 501, Description: 'Outside the black box mock contract.', Message: req.method + ' ' + path });
    const id = (path.match(/^\/api\/v1\/blackbox\/([^/]+)/) || [])[1] || '';
    if (id.startsWith('bbx_doesnotexist')) return replay(res, 'notFound');
    if (route.key === 'recovery') return recovery(res, body);
    if (route.key === 'list') return list(res, new URL(req.url, 'http://mock').searchParams);
    return replay(res, route.key);
  } catch (error) { send(res, 500, { Error: 'InternalError', StatusCode: 500, Message: error.message }); }
}).listen(process.env.PORT);
