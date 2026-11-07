// Run with the official mongosh: mongosh --nodb --quiet --file scripts/mongodb-probe.js
// Read-only. Loads the URI without putting credentials in shell arguments or output.
const fs = require('fs');
const settings = {};
for (const line of fs.readFileSync(process.env.MONGODB_ENV_FILE || '.env', 'utf8').split(/\r?\n/)) {
  if (line.trim().startsWith('#')) continue;
  const at = line.indexOf('=');
  if (at > 0) settings[line.slice(0, at).trim()] = line.slice(at + 1).trim().replace(/^(["'])(.*)\1$/, '$2');
}
const uri = process.env.MONGODB_URI || settings.MONGODB_URI;
function scrub(value) {
  let result = String(value);
  for (const secret of [uri, settings.MONGODB_PASSWORD, settings.MONGODB_USERNAME])
    if (secret) result = result.split(secret).join('[REDACTED]');
  return result;
}
try {
  if (!uri) throw new Error('MONGODB_URI is missing.');
  const connection = new Mongo(uri);
  const admin = connection.getDB('admin');
  const ping = admin.runCommand({ ping: 1 });
  const hello = admin.runCommand({ hello: 1 });
  const address = admin.runCommand({ whatsmyuri: 1 });
  const database = connection.getDB(process.env.MONGODB_DATABASE || settings.MONGODB_DATABASE || 'aiesummit');
  printjson({ primaryPing: ping.ok === 1, writablePrimary: hello.isWritablePrimary,
    primary: hello.primary, outboundAddressSeenByMongoDB: address.you, database: database.getName() });
  const latest = database.evaluation_runs.find({ documentsAttempted: { $gt: 0 } }).sort({ startedAt: -1 }).limit(1).toArray()[0];
  if (!latest) printjson({ latestDocumentRun: null });
  else {
    const filter = { runId: latest._id };
    printjson({ latestDocumentRun: latest._id, status: latest.status, command: latest.configuration?.command,
      startedAt: latest.startedAt, completedAt: latest.completedAt,
      documentsAttempted: latest.documentsAttempted, documentsFailed: latest.documentsFailed,
      documentRecords: database.evaluation_documents.countDocuments(filter),
      stepRecords: database.evaluation_steps.countDocuments(filter),
      eventRecords: database.evaluation_events.countDocuments(filter),
      artifactRecords: database.evaluation_artifacts.countDocuments(filter) });
  }
} catch (error) {
  printjson({ error: error.name, message: scrub(error.message),
    servers: error.reason?.servers ? Array.from(error.reason.servers).map(([host, server]) => ({
      host, type: server.type, error: server.error ? scrub(server.error.message) : null
    })) : undefined });
  exit(1);
}
