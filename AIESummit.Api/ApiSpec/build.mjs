import {readFile, readdir, writeFile} from 'node:fs/promises';

const readJson = async path => JSON.parse(await readFile(new URL(path, import.meta.url), 'utf8'));
async function collect(dir) {
  const entries = {};
  for (const name of (await readdir(new URL(dir, import.meta.url))).sort()) {
    if (!name.endsWith('.json')) continue;
    for (const [key, value] of Object.entries(await readJson(dir + name))) {
      if (Object.hasOwn(entries, key)) throw new Error('Duplicate OpenAPI entry ' + key);
      entries[key] = value;
    }
  }
  return entries;
}
const spec = await readJson('./template.json');
spec.paths = await collect('./Paths/');
spec.components.schemas = await collect('./Components/');
await writeFile(new URL('../wwwroot/openapi.json', import.meta.url), JSON.stringify(spec, null, 2) + '\n');
