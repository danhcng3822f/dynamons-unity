import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const unityDir = fileURLToPath(new URL('..', import.meta.url));
const repoDir = fileURLToPath(new URL('../..', import.meta.url));
const assetsDir = join(unityDir, 'Assets');
const resourcesArtDir = join(assetsDir, 'Resources', 'Dynamons', 'Art');
const resourcesRootDir = join(assetsDir, 'Resources', 'Dynamons');
const data = JSON.parse(readFileSync(join(resourcesRootDir, 'gameplay.json'), 'utf8'));

function walk(dir, filter) {
  const out = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) out.push(...walk(full, filter));
    else if (!filter || filter(full)) out.push(full);
  }
  return out.sort();
}

function checkCSharpSyntaxAndNamespace() {
  const csFiles = walk(assetsDir, f => f.endsWith('.cs'));
  assert.ok(csFiles.length >= 15, `Expected Unity/Core/Editor C# files, found ${csFiles.length}`);
  for (const file of csFiles) {
    const rel = relative(unityDir, file);
    const txt = readFileSync(file, 'utf8');
    assert.ok(txt.includes('namespace Dynamons'), `${rel}: missing namespace Dynamons`);

    let braceDepth = 0;
    let parenDepth = 0;
    let inBlockComment = false;
    for (const line of txt.split('\n')) {
      for (let i = 0; i < line.length; i++) {
        const c = line[i];
        const next = line[i + 1];
        if (inBlockComment) {
          if (c === '*' && next === '/') { inBlockComment = false; i++; }
          continue;
        }
        if (c === '/' && next === '/') break;
        if (c === '/' && next === '*') { inBlockComment = true; i++; continue; }
        if (c === '@' && next === '"') {
          i += 2;
          while (i < line.length) {
            if (line[i] === '"' && line[i + 1] === '"') { i += 2; continue; }
            if (line[i] === '"') break;
            i++;
          }
          continue;
        }
        if (c === '"') {
          i++;
          while (i < line.length && line[i] !== '"') { if (line[i] === '\\') i++; i++; }
          continue;
        }
        if (c === "'") {
          i++;
          while (i < line.length && line[i] !== "'") { if (line[i] === '\\') i++; i++; }
          continue;
        }
        if (c === '{') braceDepth++;
        else if (c === '}') braceDepth--;
        else if (c === '(') parenDepth++;
        else if (c === ')') parenDepth--;
        assert.ok(braceDepth >= 0, `${rel}: brace underflow on line: ${line}`);
        assert.ok(parenDepth >= 0, `${rel}: paren underflow on line: ${line}`);
      }
    }
    assert.equal(braceDepth, 0, `${rel}: unbalanced curly braces (${braceDepth})`);
    assert.equal(parenDepth, 0, `${rel}: unbalanced parentheses (${parenDepth})`);
  }
  return csFiles.length;
}

function checkSceneGuidAndNoMetaBloat() {
  const metaTxt = readFileSync(join(assetsDir, 'Scripts', 'Unity', 'GameRoot.cs.meta'), 'utf8');
  const guidMatch = metaTxt.match(/guid:\s*([0-9a-f]+)/i);
  assert.ok(guidMatch, 'GameRoot.cs.meta guid missing');
  const sceneTxt = readFileSync(join(assetsDir, 'Scenes', 'Main.unity'), 'utf8');
  assert.ok(sceneTxt.includes(guidMatch[1]), 'Main.unity must reference GameRoot.cs GUID');
  const pngMetas = walk(assetsDir, f => f.endsWith('.png.meta') || f.endsWith('.jpg.meta'));
  assert.equal(pngMetas.length, 0, `Unexpected texture .meta bloat: ${pngMetas.join(', ')}`);
}

function assetExists(relWithoutExt, searchExts = ['.png', '.jpg']) {
  for (const ext of searchExts) {
    const full = join(resourcesArtDir, relWithoutExt + ext);
    if (existsSync(full) && statSync(full).size > 0) return true;
  }
  return false;
}

function checkStaticAssetLiteralsAndTemplates() {
  const unityCs = walk(join(assetsDir, 'Scripts', 'Unity'), f => f.endsWith('.cs'));
  const simpleArtCalls = /(?:ui\.Art(?:Fit|Cell|Button)?|DrawClippedBar|DrawClippedSegment)\([^;]*?,\s*"([^"]+)"/g;
  const soundCalls = /ui\.Sound\(\s*"([^"]+)"/g;
  const musicCalls = /ui\.Music\(\s*"([^"]+)"/g;
  const checkedLiterals = new Set();

  for (const file of unityCs) {
    const txt = readFileSync(file, 'utf8');
    const rel = relative(unityDir, file);
    for (const m of txt.matchAll(simpleArtCalls)) {
      const p = m[1];
      const after = txt.slice(m.index + m[0].length, m.index + m[0].length + 6).trimStart();
      if (!p.includes('/') || p.includes('{') || p.endsWith('/') || after.startsWith('+')) continue;
      checkedLiterals.add(p);
      assert.ok(assetExists(p), `${rel}: Missing art asset '${p}'`);
    }
    for (const m of txt.matchAll(soundCalls)) {
      const sfx = m[1];
      if (sfx.includes('{') || sfx.endsWith('_')) continue;
      const p = join(resourcesRootDir, 'sounds', sfx + '.ogg');
      assert.ok(existsSync(p) && statSync(p).size > 0, `${rel}: Missing SFX 'sounds/${sfx}.ogg'`);
    }
    for (const m of txt.matchAll(musicCalls)) {
      const mus = m[1];
      if (mus.includes('{')) continue;
      const p = join(resourcesRootDir, 'musics', mus + '.ogg');
      assert.ok(existsSync(p) && statSync(p).size > 0, `${rel}: Missing music 'musics/${mus}.ogg'`);
    }
  }

  for (const kind of ['earth', 'fire', 'water', 'electric', 'dark']) {
    for (const pane of ['big', 'mini', 'thumb', 'tooltip'])
      assert.ok(assetExists(`general/huds/pane_${pane}_${kind}`), `HUD pane_${pane}_${kind}`);
  }
  for (const bg of ['arena', 'desert', 'snow', 'volcano', 'water', 'woods'])
    assert.ok(assetExists(`session/bgs/${bg}`), `battle bg ${bg}`);
  for (const num of [1, 2, 3, 4]) {
    assert.ok(assetExists(`map/bgs/bg${num}`), `map bg${num}`);
    assert.ok(assetExists(`session/endscreen/superlative${num}`), `superlative${num}`);
  }
  for (const mon of data.mons) {
    assert.ok(assetExists(`general/mons/${mon.id}/icon`), `mon icon ${mon.id}`);
    assert.ok(assetExists(`general/mons/${mon.id}/front`), `mon front ${mon.id}`);
    assert.ok(assetExists(`general/mons/${mon.id}/back`), `mon back ${mon.id}`);
  }
  for (const item of data.items) assert.ok(assetExists(`items/icons/${item.id}`), `item ${item.id}`);
  for (const ab of data.abilities) {
    if (!ab.id.startsWith('can-') && !ab.id.startsWith('item-'))
      assert.ok(assetExists(`session/abilities/icons/${ab.id}`), `ability icon ${ab.id}`);
  }
  for (const prod of data.products) {
    assert.ok(assetExists(`shop/products/product_${prod.id}`), `shop product ${prod.id}`);
    for (const rw of prod.displayRewards) assert.ok(assetExists(`items/icons/${rw.icon}`), `displayReward ${rw.icon}`);
  }
  for (const st of ['sick', 'hypno', 'sleep', 'rage']) assert.ok(assetExists(`session/ui/icon_${st}`), `status ${st}`);
  for (const rp of ['critical', 'strong', 'weak', 'backfire', 'rage', 'sick'])
    assert.ok(assetExists(`session/ui/report_${rp}`), `report ${rp}`);
  return checkedLiterals.size;
}

function checkFxAndMegaTablesAgainstWeb() {
  const webFxTxt = readFileSync(join(repoDir, 'web', 'js', 'battle-fx.js'), 'utf8');
  const csFxTxt = readFileSync(join(assetsDir, 'Scripts', 'Unity', 'BattleFxView.cs'), 'utf8');

  const webSheets = {};
  for (const m of webFxTxt.match(/FX_SHEETS = Object\.freeze\(\{([\s\S]*?)\}\);/)[1].matchAll(/([a-z0-9_]+):\s*\[(\d+),\s*(\d+)\]/g))
    webSheets[m[1]] = [Number(m[2]), Number(m[3])];

  const csSheets = {};
  for (const m of csFxTxt.match(/FxSheets =[\s\S]*?\{([\s\S]*?\n\s*)\};/)[1].matchAll(/\{\s*"([a-z0-9_]+)",\s*\((\d+),\s*(\d+)\)\s*\}/g))
    csSheets[m[1]] = [Number(m[2]), Number(m[3])];

  assert.deepEqual(Object.keys(csSheets).sort(), Object.keys(webSheets).sort(), 'FxSheets keys must match Web FX_SHEETS');
  assert.equal(Object.keys(csSheets).length, 34, 'FxSheets count == 34');
  for (const [k, v] of Object.entries(webSheets)) {
    assert.deepEqual(csSheets[k], v, `FxSheets grid mismatch for ${k}`);
    assert.ok(assetExists(`session/abilities/fx/${k}`, ['.png']), `Missing FX png: ${k}`);
  }

  const webMap = {};
  for (const m of webFxTxt.match(/STEP_FX_MAP = Object\.freeze\(\{([\s\S]*?)\}\);/)[1].matchAll(/['"]?([a-zA-Z0-9_-]+)['"]?:\s*['"]([a-z0-9_]+)['"]/g))
    webMap[m[1]] = m[2];
  const csMap = {};
  for (const m of csFxTxt.match(/StepFxMap =[\s\S]*?\{([\s\S]*?\n\s*)\};/)[1].matchAll(/\{\s*"([^"]+)",\s*"([^"]+)"\s*\}/g))
    csMap[m[1]] = m[2];
  assert.deepEqual(csMap, webMap, 'StepFxMap must match Web STEP_FX_MAP identically');
  assert.equal(Object.keys(csMap).length, 29, 'StepFxMap count == 29');

  const webEyeNames = [...webFxTxt.match(/MEGA_EYE_ANCHORS = Object\.freeze\(\{([\s\S]*?)\}\);/)[1].matchAll(/^\s{2}([a-z]+):\s*\{/gm)].map(m => m[1]).sort();
  const csEyeNames = [...csFxTxt.match(/MegaEyeAnchors =[\s\S]*?\{([\s\S]*?\n\s*)\};/)[1].matchAll(/\{\s*"([a-z]+)",/g)].map(m => m[1]).sort();
  assert.deepEqual(csEyeNames, webEyeNames, 'MegaEyeAnchors species must match Web');
  assert.equal(csEyeNames.length, 7, 'MegaEyeAnchors count == 7');

  const vectorFxNames = [
    'fx-spark', 'fx-volt_strike', 'fx-static_charge', 'fx-shock_wave', 'fx-thunder_storm',
    'fx-shadow_claw', 'fx-night_pulse', 'fx-gloom_veil', 'fx-umbral_slash', 'fx-slumber_hex', 'fx-eclipse_nova',
    'fx-volt_tackle', 'fx-solar_bloom', 'fx-flare_burst', 'fx-hydro_vortex', 'fx-ion_overdrive', 'fx-water_cannon',
  ];
  for (const fx of vectorFxNames) {
    assert.ok(webFxTxt.includes(`'${fx}'`), `Web missing vector FX ${fx}`);
    assert.ok(csFxTxt.includes(`"${fx}"`), `BattleFxView.cs missing vector FX ${fx}`);
  }
}

function checkFontDesignRulesAndDtoConstraints() {
  const scriptFiles = walk(join(assetsDir, 'Scripts'), f => f.endsWith('.cs'));
  for (const file of scriptFiles) {
    const rel = relative(unityDir, file);
    const txt = readFileSync(file, 'utf8');
    for (const call of txt.matchAll(/fonts\.(?:Draw|DrawLines)\(([^;]*)\);/g)) {
      const stripped = call[1].replace(/\{[^{}]*\}/g, '');
      for (const lit of stripped.matchAll(/"([^"\\]*(?:\\.[^"\\]*)*)"/g)) {
        assert.ok(!lit[1].includes('+'), `${rel}: Forbidden '+' inside font text literal: "${lit[1]}"`);
      }
    }
    assert.ok(!/GRANTED_STARTER_MONS/.test(txt), `${rel}: Auto-gifted bonus starters are forbidden`);
    if (file.includes('/Scripts/Unity/') && !file.endsWith('GameUi.cs') && !file.endsWith('GameRoot.cs')) {
      assert.ok(!/\bui\.Label\(/.test(txt), `${rel}: Must use BitmapFontRegistry instead of legacy ui.Label`);
    }
  }

  for (const dtoFile of [join(assetsDir, 'Scripts', 'Core', 'GameData.cs'), join(assetsDir, 'Scripts', 'Core', 'SaveData.cs')]) {
    const txt = readFileSync(dtoFile, 'utf8');
    const rel = relative(unityDir, dtoFile);
    assert.ok(!/Dictionary</.test(txt), `${rel}: JsonUtility [Serializable] DTOs must not use Dictionary fields`);
  }
}

const csCount = checkCSharpSyntaxAndNamespace();
checkSceneGuidAndNoMetaBloat();
const artCount = checkStaticAssetLiteralsAndTemplates();
checkFxAndMegaTablesAgainstWeb();
checkFontDesignRulesAndDtoConstraints();

console.log(`Unity parity verified: ${csCount} C# files balanced/valid, ${artCount} static art literals checked, 34 FX sheets, 29 StepFxMap, 17 vector FX, 7 Mega eye species, 0 '+’ font literals, 0 auto-gifted starters.`);
