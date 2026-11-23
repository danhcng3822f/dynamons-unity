import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

export const resources = new URL('../Assets/Resources/Dynamons/', import.meta.url);
export const readJson = (url) => JSON.parse(readFileSync(url, 'utf8'));

if (process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url) {
  const data = readJson(new URL('gameplay.json', resources));
  const { normalize, validateData, runtimeAssets } = await import('./import-web.mjs');
  validateData(data);
  assert.deepEqual(data, normalize(readJson(new URL('../../web/assets/data/gameplay.js', import.meta.url))), 'Unity snapshot is stale; rerun import');
  const project = new URL('../', import.meta.url);
  const text = name => readFileSync(new URL(name, project), 'utf8');
  const guid = name => text(name + '.meta').match(/^guid: ([a-f0-9]{32})$/m)?.[1];
  const scene = 'Assets/Scenes/Main.unity', root = 'Assets/Scripts/Unity/GameRoot.cs';
  assert.ok(guid(scene) && text('ProjectSettings/EditorBuildSettings.asset').includes(guid(scene)), 'entry scene GUID');
  assert.ok(guid(root) && text(scene).includes(guid(root)) && text(root).includes('class GameRoot : MonoBehaviour'), 'entry behaviour GUID');
  assert.deepEqual([data.mons.length, data.abilities.length, data.items.length, data.mapNodes.length], [37, 59, 21, 57]);
  for (const [mutate, message] of [
    [fixture => { fixture.mons[0].abilities[0].id = 'missing-test-move'; }, /missing-test-move/],
    [fixture => { fixture.mapNodes.find(node => node.arenaBattle).arenaDialogs.dialog_start = 'missing-arena-dialog'; }, /missing-arena-dialog/],
    [fixture => { fixture.mons.find(mon => mon.id === 'nyxalon').id = 'renamed-boss'; }, /nyxalon/],
    [fixture => { fixture.abilities.find(move => move.id === 'item-heal_spray').id = 'renamed-item-move'; }, /item-heal_spray/],
    [fixture => { fixture.mapNodes.find(node => node.id === 5).connect = 5; }, /unreachable.*5/i],
    [fixture => { fixture.mapNodes.find(node => node.id === 5).connect = 6; }, /unreachable.*5/i],
  ]) {
    const fixture = structuredClone(data);
    mutate(fixture);
    assert.throws(() => validateData(fixture), message);
  }
  const assets = runtimeAssets(data);
  for (const path of assets) assert.ok(readFileSync(new URL(path, resources)).length > 0, path);
  console.log(`Verified ${data.mons.length} species, ${data.abilities.length} abilities, ${data.items.length} items, ${data.mapNodes.length} nodes and ${assets.length} required assets.`);
}
