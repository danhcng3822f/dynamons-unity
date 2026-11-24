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
  assert.equal(data.fonts.length, 7, 'font count');
  for (const font of data.fonts) {
    assert.ok(Array.isArray(font.glyphs) && font.glyphs.length === font.chars.length && font.glyphs.length > 0, `font glyphs: ${font.id}`);
    assert.ok(font.glyphs.every(g => Number.isInteger(g.code) && g.w > 0 && g.h > 0 && Number.isInteger(g.top) && Number.isInteger(g.bottom)), `font glyph metrics: ${font.id}`);
    assert.ok(Array.isArray(font.kerns) && font.kerns.length > 0, `font kerns: ${font.id}`);
  }
  const big = data.fonts.find(f => f.id === 'white_stroked_big');
  assert.equal(big.kerns.find(k => k.left === 48 && k.right === 50)?.adjust, -3.5, 'white_stroked_big 0->2 kern');
  assert.equal(big.kerns.find(k => k.left === 0 && k.right === 0)?.adjust, -2, 'white_stroked_big any->any kern');
  const med = data.fonts.find(f => f.id === 'black_medium');
  assert.equal(med.kerns.find(k => k.left === 35 && k.right === 49)?.adjust, 1.5, 'black_medium #->1 kern');
  assert.equal(data.shopMessage, "I wonder what you'll get!", 'shopMessage');
  for (const prod of data.products) {
    assert.ok(typeof prod.lootTitle === 'string' && prod.lootTitle.length > 0, `product lootTitle: ${prod.id}`);
    assert.ok(Array.isArray(prod.displayRewards) && prod.displayRewards.length === 4, `product displayRewards: ${prod.id}`);
    assert.ok(prod.displayRewards.every(r => typeof r.icon === 'string' && r.icon.length > 0 && typeof r.note === 'string'), `product displayReward fields: ${prod.id}`);
  }
  const assets = runtimeAssets(data);
  const assetSet = new Set(assets);
  for (const req of [
    'Art/general/huds/pane_big_fire.png', 'Art/general/huds/hpbar_big_fill_white.png',
    'Art/general/dialog/speech_bubble.png', 'Art/general/enemies/training_avatar.png',
    'Art/session/abilities/fx/blast.png', 'Art/session/abilities/fx/winter.png',
    'Art/session/ui/btn_mega.png', 'Art/session/ui/report_strong.png',
    'Art/session/endscreen/you_win.png', 'Art/shop/products/product_bag.png',
    'Art/shop/products/product_chest.png', 'Art/shop/loot_lights.png',
    'Art/team/bg_team.jpg', 'Art/team/info/btn_release.png',
    'sounds/button_click.ogg', 'sounds/attack_scratch.ogg', 'sounds/music_battle_start.ogg',
  ]) assert.ok(assetSet.has(req), `runtimeAssets missing ${req}`);
  for (const path of assets) assert.ok(readFileSync(new URL(path, resources)).length > 0, path);
  console.log(`Verified ${data.mons.length} species, ${data.abilities.length} abilities, ${data.items.length} items, ${data.mapNodes.length} nodes and ${assets.length} required assets.`);
}
