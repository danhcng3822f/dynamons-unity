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

  const texImportPath = join(assetsDir, 'Editor', 'DynamonsTextureImport.cs');
  assert.ok(existsSync(texImportPath), 'Missing DynamonsTextureImport.cs for Web NPOT parity');
  const texImportTxt = readFileSync(texImportPath, 'utf8');
  assert.ok(texImportTxt.includes('TextureImporterNPOTScale.None'), 'DynamonsTextureImport.cs must set TextureImporterNPOTScale.None');
  assert.ok(texImportTxt.includes('TextureWrapMode.Clamp'), 'DynamonsTextureImport.cs must set TextureWrapMode.Clamp');
  assert.ok(texImportTxt.includes('mipmapEnabled = false'), 'DynamonsTextureImport.cs must disable mipmaps');

  const teamDragTxt = readFileSync(join(assetsDir, 'Scripts', 'Unity', 'TeamSlotDrag.cs'), 'utf8');
  assert.ok(teamDragTxt.includes('suppressClick'), 'TeamSlotDrag.cs must suppress post-drag click');
  assert.ok(teamDragTxt.includes('inFrame'), 'TeamSlotDrag.cs must cancel drops outside logical frame bounds');
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
    for (const call of txt.matchAll(/(?:DrawClippedBar|DrawClippedSegment)\([^;]*?\)/g)) {
      for (const arg of call[0].matchAll(/"([^"]+)"/g)) {
        if (!arg[1].includes('/') || arg[1].includes('{')) continue;
        checkedLiterals.add(arg[1]);
        assert.ok(assetExists(arg[1]), `${rel}: Missing clipped bar asset '${arg[1]}'`);
      }
    }
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

function extractBraceBlock(txt, signatureRegex) {
  const match = signatureRegex.exec(txt);
  if (!match) return null;
  let i = match.index + match[0].length;
  while (i < txt.length && /\s/.test(txt[i])) i++;
  if (txt.slice(i, i + 2) === '=>') {
    const semi = txt.indexOf(';', i + 2);
    return semi >= 0 ? txt.slice(i + 2, semi) : null;
  }
  if (txt[i] !== '{') return null;
  let depth = 0;
  let inString = false;
  let escaped = false;
  for (let p = i; p < txt.length; p++) {
    const ch = txt[p];
    if (inString) {
      if (escaped) { escaped = false; continue; }
      if (ch === '\\') { escaped = true; continue; }
      if (ch === '"') inString = false;
      continue;
    }
    if (ch === '"') { inString = true; continue; }
    if (ch === '{') depth++;
    else if (ch === '}') {
      depth--;
      if (depth === 0) return txt.slice(i + 1, p);
    }
  }
  return null;
}

function checkFontDesignRulesAndDtoConstraints() {
  const scriptFiles = walk(join(assetsDir, 'Scripts'), f => f.endsWith('.cs'));
  for (const file of scriptFiles) {
    const rel = relative(unityDir, file);
    const txt = readFileSync(file, 'utf8');
    for (const call of txt.matchAll(/fonts\.(?:Draw|DrawLines)\(([^;]*)\);/g)) {
      const stripped = call[1].replace(/\{[^{}]*\}/g, '');
      for (const lit of stripped.matchAll(/"([^"\\]*(?:\\.[^"\\]*)*)"/g)) {
        assert.ok(!/[+&]/.test(lit[1]), `${rel}: Forbidden '+' or '&' inside font text literal: "${lit[1]}"`);
      }
    }
    assert.ok(!/GRANTED_STARTER_MONS/.test(txt), `${rel}: Auto-gifted bonus starters are forbidden`);
    assert.ok(!/private void (Header|Settings)\(/.test(txt), `${rel}: Unused Header/Settings UI methods must not remain`);
    if (rel.endsWith('MapView.cs')) {
      assert.ok(!/mapUserDragged/.test(txt), 'MapView.cs must not pan camera automatically while walking');
      for (const artRef of ['"map/island_lan"', '"map/boat"', '"general/enemies/lan_npc_avatar"', '"general/enemies/lan_npc_icon"']) {
        assert.ok(txt.includes(artRef), `MapView.cs must reference ${artRef}`);
      }
      assert.ok(/\bbool\s+onLanIsland\b/.test(txt), 'MapView.cs must declare onLanIsland session flag');
      const resetBody = extractBraceBlock(txt, /void\s+ResetMapVisualRefs\s*\(\s*\)/);
      assert.ok(resetBody !== null, 'MapView.cs must define ResetMapVisualRefs()');
      assert.ok(!/\bonLanIsland\s*=/.test(resetBody), 'ResetMapVisualRefs() must NOT reset onLanIsland so ShowMap() rebuild survives return-to-island');

      const refreshHudBody = extractBraceBlock(txt, /void\s+RefreshLanIslandHud\s*\(\s*\)/);
      assert.ok(refreshHudBody !== null, 'MapView.cs must define RefreshLanIslandHud()');
      assert.ok(
        /(!onLanIsland|onLanIsland\s*&&)/.test(refreshHudBody) && refreshHudBody.includes('overlay') && refreshHudBody.includes('"LAN VS"'),
        'MapView.cs RefreshLanIslandHud() must gate persistent "LAN VS" pill strictly on onLanIsland && overlay == null',
      );
      assert.ok(!refreshHudBody.includes('"LAN VS"') || /if\s*\([^)]*!onLanIsland[^)]*\)\s*return\b/.test(refreshHudBody) || /if\s*\([^)]*onLanIsland[^)]*\)\s*\{[\s\S]*"LAN VS"/.test(refreshHudBody),
        'MapView.cs must guard "LAN VS" pill behind onLanIsland check');
      const pillDims = refreshHudBody.match(/const\s+float\s+px\s*=\s*(\d+(?:\.\d+)?)f\s*,\s*py\s*=\s*[^,]+,\s*pw\s*=\s*(\d+(?:\.\d+)?)f/);
      assert.ok(pillDims !== null, 'MapView.cs RefreshLanIslandHud() must declare px and pw for LanVsHudPill');
      const pillX = Number(pillDims[1]);
      const pillW = Number(pillDims[2]);
      assert.equal(pillX, 80, `MapView.cs LanVsHudPill px must be 80f (got ${pillX}f)`);
      assert.ok(pillX >= 70 && pillX + pillW <= 190,
        `MapView.cs LanVsHudPill (${pillX}..${pillX + pillW}) must not overlap ShopBtn (10..70) or ItemsBtn (190..250)`);

      const topLeftBody = extractBraceBlock(txt, /void\s+DrawMapTopLeftControls\s*\(\s*\)/);
      assert.ok(topLeftBody !== null, 'MapView.cs must define DrawMapTopLeftControls()');
      const healBlock = topLeftBody.match(/ui\.ArtButton\(freeHealBox,[\s\S]*?"FreeHealBtn"\)/);
      assert.ok(healBlock !== null && /if\s*\(\s*traveling\s*\)\s*return\s*;/.test(healBlock[0]),
        'MapView.cs FreeHealBtn callback must guard with if (traveling) return;');

      const travelBody = extractBraceBlock(txt, /void\s+Travel\s*\([^)]*\)/);
      assert.ok(travelBody !== null && /onLanIsland\s*=\s*false/.test(travelBody), 'MapView.cs Travel() must clear onLanIsland when departing island');

      const modalBody = extractBraceBlock(txt, /void\s+OpenLanNpcModal\s*\(\s*\)/);
      assert.ok(modalBody !== null, 'MapView.cs must define OpenLanNpcModal()');
      for (const expected of ['"general/enemies/lan_npc_icon"', '"ISLAND DUELIST"', '"DUEL TRAINERS ON YOUR WI-FI!"', '"LAN VS"', '"general/btn_close"', 'OpenLanLobby(']) {
        assert.ok(modalBody.includes(expected), `OpenLanNpcModal() missing ${expected}`);
      }
    }
    if (rel.endsWith('GameUi.cs')) {
      const textFieldBody = extractBraceBlock(txt, /public\s+InputField\s+TextField\s*\([^)]*\)/);
      assert.ok(textFieldBody !== null, 'GameUi.cs must define public InputField TextField(...)');
      assert.ok(textFieldBody.includes('fontSize = 13') && txt.includes('LegacyRuntime.ttf'),
        'GameUi.TextField() must style InputField text with LegacyRuntime.ttf at 13px');
    }
    if (rel.endsWith('GameRoot.cs')) {
      const updateBody = extractBraceBlock(txt, /void\s+Update\s*\(\s*\)/);
      assert.ok(updateBody !== null && /UpdateLanNetworking\s*\(\s*\)/.test(updateBody), 'GameRoot.cs Update() must call UpdateLanNetworking()');
      assert.ok(!txt.includes('LAN LOBBY WILL OPEN HERE'), 'GameRoot.cs must not keep Task 5 OpenLanLobby stub');
      const onDestroyBody = extractBraceBlock(txt, /void\s+OnDestroy\s*\(\s*\)/);
      assert.ok(onDestroyBody !== null && /ShutdownLanNetworking\s*\(\s*\)/.test(onDestroyBody), 'GameRoot.cs OnDestroy() must call ShutdownLanNetworking()');
      const onQuitBody = extractBraceBlock(txt, /void\s+OnApplicationQuit\s*\(\s*\)/);
      assert.ok(onQuitBody !== null && /ShutdownLanNetworking\s*\(\s*\)/.test(onQuitBody), 'GameRoot.cs OnApplicationQuit() must call ShutdownLanNetworking()');
      const splashBody = extractBraceBlock(txt, /void\s+Splash\s*\(\s*\)/);
      const chooseBody = extractBraceBlock(txt, /void\s+Choose\s*\(\s*\)/);
      assert.ok(splashBody !== null && /onLanIsland\s*=\s*false/.test(splashBody), 'GameRoot.cs Splash() must reset onLanIsland = false');
      assert.ok(chooseBody !== null && /onLanIsland\s*=\s*false/.test(chooseBody), 'GameRoot.cs Choose() must reset onLanIsland = false');
    }
    if (rel.endsWith('PvpLobbyView.cs')) {
      assert.ok(/\bvoid\s+UpdateLanNetworking\s*\(\s*\)/.test(txt), 'PvpLobbyView.cs must define UpdateLanNetworking()');
      assert.ok(/\bvoid\s+ShutdownLanNetworking\s*\(\s*\)/.test(txt), 'PvpLobbyView.cs must define ShutdownLanNetworking()');
      assert.ok(/\bvoid\s+OpenLanLobby\s*\(\s*\)/.test(txt), 'PvpLobbyView.cs must define OpenLanLobby()');
      assert.ok(/\bvoid\s+CloseLanLobby\s*\(/.test(txt), 'PvpLobbyView.cs must define CloseLanLobby()');
      assert.ok(!/\bvoid\s+BeginPvpBattle\s*\(/.test(txt),
        'PvpLobbyView.cs must remove Task 6 BeginPvpBattle stub');
      assert.ok(!/\bvoid\s+OnPvpNetMessage\s*\(/.test(txt),
        'PvpLobbyView.cs must remove Task 6 OnPvpNetMessage stub');
      assert.ok(txt.includes('"rematch"'), 'PvpLobbyView.cs must handle rematch transitions');
      assert.ok(txt.includes('ui.TextField('), 'PvpLobbyView.cs must use GameUi.TextField for manual IP entry');
      assert.ok(txt.includes('HudWidgets.DrawMonPane('), 'PvpLobbyView.cs must render team previews via HudWidgets.DrawMonPane');
      assert.ok(txt.includes('DrainMainThreadMessages(') && txt.includes('GetRooms(') && txt.includes('UpdateBeacon('),
        'PvpLobbyView.cs must pump LanTransport messages, rooms, and beacons on the main thread');
      for (const expected of ['"hello"', '"start"', '"disconnected"', '"connected"',
        '"FAIR LV 20"', '"REAL TEAM"', '"ITEMS ON"', '"ITEMS OFF"',
        '"HOST ROOM"', '"JOIN ROOM"', '"CONNECT"', '"START DUEL"', '"WAITING FOR HOST"']) {
        assert.ok(txt.includes(expected), `PvpLobbyView.cs missing ${expected}`);
      }
      assert.ok(!/(?:save\.Team\.(?:Add|Remove|Clear)|save\.Inventory\.(?:Add|Remove|Clear)|save\.(?:Take|Add)\(|save\.NodeId\s*=)/.test(txt),
        'PvpLobbyView.cs must keep SaveData Team/Inventory/NodeId strictly read-only');
      const disconnectBody = extractBraceBlock(txt, /void\s+HandleLobbyPeerDisconnect\s*\([^)]*\)/);
      assert.ok(disconnectBody !== null, 'PvpLobbyView.cs must define HandleLobbyPeerDisconnect()');
      assert.ok(disconnectBody.includes('IsHost') && disconnectBody.includes('RenderHostLobby('),
        'HandleLobbyPeerDisconnect() must guard EnterHostMode() with IsHost/active check and fall back to RenderHostLobby() without looping StartHost');
      const lobbyMsgBody = extractBraceBlock(txt, /void\s+HandleLobbyNetMessage\s*\(\s*LanNetMessage\s+[^)]+\)/);
      assert.ok(lobbyMsgBody !== null, 'PvpLobbyView.cs must define HandleLobbyNetMessage()');
      const helloCaseBody = extractBraceBlock(lobbyMsgBody, /case\s+"hello"\s*:/);
      assert.ok(helloCaseBody !== null && /pvpRemoteRematchRequested\s*=\s*false\s*;/.test(helloCaseBody),
        'PvpLobbyView.cs HandleLobbyNetMessage() "hello" handshake must clear pvpRemoteRematchRequested = false');
      assert.ok(disconnectBody.includes('41976') && disconnectBody.includes('TIMED OUT'),
        'PvpLobbyView.cs HandleLobbyPeerDisconnect() must surface actionable firewall/host hint on timeout');
    }
    if (rel.endsWith('LanTransport.cs')) {
      const drainBody = extractBraceBlock(txt, /List<LanNetMessage>\s+DrainMainThreadMessages\s*\([^)]*\)/);
      assert.ok(drainBody !== null, 'LanTransport.cs must define DrainMainThreadMessages()');
      const dequeueBody = extractBraceBlock(drainBody, /while\s*\(\s*incomingQueue\.TryDequeue\([^)]*\)\s*\)/);
      assert.ok(dequeueBody !== null, 'DrainMainThreadMessages() must drain incomingQueue');
      const discIdx = dequeueBody.indexOf('RawKindDisconnected');
      const skipIdx = dequeueBody.search(/if\s*\(\s*sawDisconnectedThisDrain\s*\)\s*continue\s*;/);
      const connIdx = dequeueBody.indexOf('RawKindConnected');
      const lineIdx = dequeueBody.indexOf('RawKindLine');
      assert.ok(discIdx >= 0 && skipIdx >= 0 && connIdx > skipIdx && lineIdx > skipIdx,
        'DrainMainThreadMessages() must skip trailing RawKindConnected/RawKindLine items after RawKindDisconnected in the same drain');

      const closeDisconnectBody = extractBraceBlock(txt, /void\s+CloseSocketsForDisconnect\s*\([^)]*\)/);
      assert.ok(closeDisconnectBody !== null, 'LanTransport.cs must define CloseSocketsForDisconnect()');
      assert.ok(
        /isConnected\s*=\s*false\s*;/.test(closeDisconnectBody) &&
        /isHost\s*=\s*false\s*;/.test(closeDisconnectBody) &&
        /boundPort\s*=\s*0\s*;/.test(closeDisconnectBody),
        'CloseSocketsForDisconnect() must reset isConnected, isHost, and boundPort',
      );
      for (const expected of [
        'LanConst.DiscoveryMulticastIp',
        'JoinMulticastGroup',
        'AcquireAndroidMulticastLock',
        'ReleaseAndroidMulticastLock',
        'CollectAndroidLanInterfaces',
        'ComputeBroadcastIp',
        'IsVirtualInterfaceName',
      ]) {
        assert.ok(txt.includes(expected), `LanTransport.cs missing LAN reliability helper: ${expected}`);
      }
      for (const bgLoop of ['UdpBeaconLoop', 'UdpDiscoveryLoop', 'TcpAcceptLoop', 'TcpConnectLoop', 'TcpReaderLoop', 'TcpWriterLoop']) {
        const bgBody = extractBraceBlock(txt, new RegExp(`void\\s+${bgLoop}\\s*\\([^)]*\\)`));
        assert.ok(bgBody !== null, `LanTransport.cs must define ${bgLoop}()`);
        assert.ok(!/AndroidJava|ConnectivityManager|MulticastLock|JsonUtility/.test(bgBody),
          `LanTransport.cs ${bgLoop}() must never touch Unity JNI or JsonUtility on a background thread`);
      }
    }
    if (rel.endsWith('LanProtocol.cs')) {
      assert.ok(txt.includes('DiscoveryMulticastIp = "239.255.41.75"'),
        'LanProtocol.cs must define LanConst.DiscoveryMulticastIp');
    }
    if (rel.endsWith('BattleView.cs')) {
      assert.ok(txt.includes('HudWidgets.DrawTopHud(ui, fonts, data, save, topHudLayer'), 'BattleView.cs must draw TopHud on topHudLayer above veilLayer');
      assert.ok(txt.includes('GameData.HasPassive(') && txt.includes('GameData.PassiveKey('), 'BattleView.cs must guard passives with GameData.HasPassive/PassiveKey');
      assert.ok(txt.includes('else if (ev.Type == "miss")'), 'BattleView.cs must render miss feedback immediately in BeginBattleEvent');
      assert.ok(txt.includes('general/huds/tip_strong') && txt.includes('general/huds/tip_weak'), 'BattleView.cs must show effectiveness hints on move buttons');
      assert.ok(/\bvoid\s+BeginPvpBattle\s*\(\s*LanStartMatchDto\s+[^,]+,\s*bool\s+[^)]+\)/.test(txt),
        'BattleView.cs must define BeginPvpBattle(LanStartMatchDto, bool)');
      assert.ok(/\bvoid\s+OnPvpNetMessage\s*\(\s*LanNetMessage\s+[^)]+\)/.test(txt),
        'BattleView.cs must define OnPvpNetMessage(LanNetMessage)');
      for (const expected of ['LanTurnResultDto', 'LanAnimDoneDto', 'LanActionCmdDto', 'LanCodec.EventFromDto',
        'HudWidgets.DrawCaptainHud', '"general/enemies/lan_npc_icon"', 'LanConst.TurnTimeoutSec', 'LanConst.AnimGraceSec',
        '"OPPONENT TURN', '"OPPONENT CHOOSING..."', 'pvpPouch', 'flow.StartBattle', 'flow.CommitBattle']) {
        assert.ok(txt.includes(expected), `BattleView.cs missing ${expected}`);
      }
      assert.ok(/\bvoid\s+OpenItemsOverlay\s*\([^)]*IReadOnlyList<LanItemSlotDto>\s+pvpPouch\b/.test(txt),
        'BattleView.cs OpenItemsOverlay must accept optional IReadOnlyList<LanItemSlotDto> pvpPouch');
      const onNetMsgBody = extractBraceBlock(txt, /void\s+OnPvpNetMessage\s*\(\s*LanNetMessage\s+[^)]+\)/);
      assert.ok(onNetMsgBody !== null, 'BattleView.cs must define OnPvpNetMessage(LanNetMessage)');
      const disconnectedCaseBody = extractBraceBlock(onNetMsgBody, /case\s+"disconnected"\s*:/);
      assert.ok(disconnectedCaseBody !== null, 'OnPvpNetMessage() must handle case "disconnected": { ... }');
      const hasHelperGuard = /HasTerminalPvpResult\s*\(\s*\)/.test(disconnectedCaseBody);
      const hasInlineGuard =
        /pvpCurrentResult[^;]*\.phase\s*==\s*"ended"/.test(disconnectedCaseBody) &&
        /pvpPendingQueue[\s\S]*\.phase\s*==\s*"ended"/.test(disconnectedCaseBody);
      assert.ok(
        hasHelperGuard || hasInlineGuard,
        'OnPvpNetMessage() case "disconnected" must guard against an already-received terminal result in pvpCurrentResult or pvpPendingQueue before awarding disconnect victory',
      );
      if (hasHelperGuard) {
        const hasTermBody = extractBraceBlock(txt, /bool\s+HasTerminalPvpResult\s*\(\s*\)/);
        assert.ok(
          hasTermBody !== null &&
          /pvpCurrentResult[^;]*\.phase\s*==\s*"ended"/.test(hasTermBody) &&
          /pvpPendingQueue[\s\S]*\.phase\s*==\s*"ended"/.test(hasTermBody),
          'HasTerminalPvpResult() must inspect both pvpCurrentResult.phase == "ended" and pvpPendingQueue for phase == "ended"',
        );
      }

      const resetBattleBody = extractBraceBlock(txt, /void\s+ResetBattleState\s*\(\s*\)/);
      assert.ok(resetBattleBody !== null && /\binPvpBattle\s*=\s*false\s*;/.test(resetBattleBody),
        'BattleView.cs ResetBattleState() must reset inPvpBattle = false');

      const pvpClickBody = extractBraceBlock(txt, /void\s+PvpClick\s*\(\s*BattleAction\s+[^)]+\)/);
      assert.ok(pvpClickBody !== null, 'BattleView.cs must define PvpClick(BattleAction)');
      assert.ok(/if\s*\(\s*battleWaiting\s*\|\|\s*!IsMyPvpInputTurn\(\)\s*\|\|\s*pvpSubmittedCurrentSeq\s*\|\|\s*pvpForfeitSent\s*\)\s*return\s*;/.test(pvpClickBody),
        'PvpClick() must block non-escape actions when pvpForfeitSent is true');

      const refreshBannerBody = extractBraceBlock(txt, /void\s+RefreshPvpTurnBanner\s*\(\s*\)/);
      assert.ok(refreshBannerBody !== null, 'BattleView.cs must define RefreshPvpTurnBanner()');
      assert.ok(/!pvpSubmittedCurrentSeq\s*&&\s*!pvpForfeitSent/.test(refreshBannerBody),
        'RefreshPvpTurnBanner() must not early-return when pvpForfeitSent is true so FORFEITING... banner renders');

      const rebuildBody = extractBraceBlock(txt, /void\s+RebuildBattleControls\s*\(\s*\)/);
      assert.ok(rebuildBody !== null, 'BattleView.cs must define RebuildBattleControls()');
      assert.ok(
        /IsMyPvpInputTurn\(\)\s*&&\s*!pvpSubmittedCurrentSeq\s*&&\s*!pvpForfeitSent/.test(rebuildBody) &&
        /!IsMyPvpInputTurn\(\)\s*\|\|\s*pvpSubmittedCurrentSeq\s*\|\|\s*pvpForfeitSent/.test(rebuildBody),
        'RebuildBattleControls() must gate interactive and PvP banner state on !pvpForfeitSent',
      );

      const pickerBody = extractBraceBlock(txt, /void\s+OpenBattleItemPicker\s*\(\s*\)/);
      assert.ok(pickerBody !== null && /pvpSubmittedCurrentSeq\s*\|\|\s*pvpForfeitSent/.test(pickerBody),
        'OpenBattleItemPicker() must guard against pvpForfeitSent');

      const frameBody = extractBraceBlock(txt, /void\s+RenderDynamicBattleFrame\s*\(\s*\)/);
      assert.ok(frameBody !== null && /turnRingActive\s*=[\s\S]*?!pvpSubmittedCurrentSeq\s*&&\s*!pvpForfeitSent/.test(frameBody),
        'RenderDynamicBattleFrame() turnRingActive must exclude pvpForfeitSent');

      const handleEvBody = extractBraceBlock(txt, /void\s+HandleBattleEvent\s*\(\s*BattleEvent\s+[^)]+\)/);
      assert.ok(handleEvBody !== null && /ev\.Type\s*==\s*"escape"[\s\S]*pvpActive\s*&&\s*!string\.IsNullOrEmpty\(ev\.Text\)\s*\?\s*ev\.Text\s*:\s*"GOT AWAY!"/.test(handleEvBody),
        'HandleBattleEvent() must display PvP forfeit text for escape events when pvpActive');

      const itemsBody = extractBraceBlock(txt, /void\s+RenderItemsModalContents\s*\([^)]*\)/);
      assert.ok(itemsBody !== null && itemsBody.includes('"NO PVP ITEMS LEFT"') && itemsBody.includes('"Your Battle Pouch is empty for this duel."'),
        'RenderItemsModalContents() must show PvP-specific empty pouch wording');
    }
    if (rel.endsWith('CollectionView.cs')) {
      assert.ok(/\bvoid\s+ShowPvpResult\s*\(\s*string\s+[^)]+\)/.test(txt),
        'CollectionView.cs must define ShowPvpResult(string)');
      const showPvpBody = extractBraceBlock(txt, /void\s+ShowPvpResult\s*\([^)]*\)/);
      assert.ok(showPvpBody !== null, 'CollectionView.cs ShowPvpResult must have a block body');
      assert.ok(showPvpBody.includes('pvpResolvedTurns >= 1') && showPvpBody.includes('50') &&
        showPvpBody.includes('pvpResolvedTurns >= 2') && showPvpBody.includes('15'),
        'ShowPvpResult() must implement anti-farm 50/15/0 Coin reward logic');
      assert.ok(!/(?:save\.Team\.(?:Add|Remove|RemoveAt|Clear|Insert|Sort)|save\.Team\[|save\.Inventory\.(?:Add|Remove|RemoveAt|Clear|Insert)|save\.(?:Take|Add)\(|save\.ClearedNodes\.(?:Add|Remove|Clear)|save\.NodeId\s*=)/.test(showPvpBody),
        'ShowPvpResult() must NEVER mutate save.Team, save.Inventory, save.ClearedNodes, or save.NodeId');
      const renderPvpBody = extractBraceBlock(txt, /void\s+RenderPvpResultView\s*\(\s*\)/);
      assert.ok(renderPvpBody !== null, 'CollectionView.cs must define RenderPvpResultView()');
      for (const expected of ['"REMATCH"', '"TO ISLAND"', '"DEFEAT"', '"session/endscreen/you_win"',
        'ReturnToLanIsland()', 'IsConnected', 'COINS']) {
        assert.ok(renderPvpBody.includes(expected), `RenderPvpResultView() missing ${expected}`);
      }
      assert.ok(/!lanTransport\.IsConnected\s*\)\s*\{[\s\S]*pvpRemoteRematchRequested\s*=\s*false\s*;/.test(renderPvpBody),
        'RenderPvpResultView() disconnect fallback must reset pvpRemoteRematchRequested = false');
      assert.ok(!/(?:save\.Team\.(?:Add|Remove|RemoveAt|Clear|Insert|Sort)|save\.Team\[|save\.Inventory\.(?:Add|Remove|RemoveAt|Clear|Insert)|save\.(?:Take|Add)\(|save\.ClearedNodes\.(?:Add|Remove|Clear)|save\.NodeId\s*=)/.test(renderPvpBody),
        'RenderPvpResultView() must NEVER mutate save.Team, save.Inventory, save.ClearedNodes, or save.NodeId');
    }
    if (file.includes('/Scripts/Unity/') && !file.endsWith('GameUi.cs') && !file.endsWith('GameRoot.cs')) {
      assert.ok(!/\bui\.Label\(/.test(txt), `${rel}: Must use BitmapFontRegistry instead of legacy ui.Label`);
    }
  }

  assert.ok(existsSync(join(assetsDir, 'Scripts', 'Unity', 'PvpLobbyView.cs')),
    'Missing Assets/Scripts/Unity/PvpLobbyView.cs');

  const buildSettingsTxt = readFileSync(join(assetsDir, 'Editor', 'DynamonsBuildSettings.cs'), 'utf8');
  assert.ok(buildSettingsTxt.includes('PlayerSettings.productName = "Dynamons Next"'), 'DynamonsBuildSettings.cs must set productName = "Dynamons Next" unconditionally');
  assert.ok(buildSettingsTxt.includes('SetApplicationIdentifier(NamedBuildTarget.Android, "com.dynamons.game")'), 'DynamonsBuildSettings.cs must set Android applicationIdentifier to com.dynamons.game');
  assert.ok(buildSettingsTxt.includes('PlayerSettings.SplashScreen.show = false'), 'DynamonsBuildSettings.cs must disable Unity SplashScreen');
  assert.ok(buildSettingsTxt.includes('PlayerSettings.Android.forceInternetPermission = true'), 'DynamonsBuildSettings.cs must set PlayerSettings.Android.forceInternetPermission = true');

  const outsideAndroidGuard = buildSettingsTxt.replace(/#if\s+UNITY_ANDROID[\s\S]*?#endif/g, '');
  assert.ok(!/UnityEditor\.Android/.test(outsideAndroidGuard) && !/IPostGenerateGradleAndroidProject/.test(outsideAndroidGuard),
    'DynamonsBuildSettings.cs must guard UnityEditor.Android and IPostGenerateGradleAndroidProject strictly inside #if UNITY_ANDROID');
  const androidBlocks = [...buildSettingsTxt.matchAll(/#if\s+UNITY_ANDROID([\s\S]*?)#endif/g)].map(m => m[1]).join('\n');
  assert.ok(androidBlocks.includes('using UnityEditor.Android;'), 'DynamonsBuildSettings.cs must import UnityEditor.Android inside #if UNITY_ANDROID');
  assert.ok(androidBlocks.includes('IPostGenerateGradleAndroidProject') && androidBlocks.includes('OnPostGenerateGradleAndroidProject'),
    'DynamonsBuildSettings.cs must implement IPostGenerateGradleAndroidProject inside #if UNITY_ANDROID');
  for (const manifestPath of ['src/main/AndroidManifest.xml', '../launcher/src/main/AndroidManifest.xml']) {
    assert.ok(androidBlocks.includes(manifestPath), `DynamonsBuildSettings.cs must check ${manifestPath}`);
  }
  const androidPermissions = [
    'android.permission.INTERNET',
    'android.permission.ACCESS_NETWORK_STATE',
    'android.permission.ACCESS_WIFI_STATE',
    'android.permission.CHANGE_WIFI_MULTICAST_STATE',
  ];
  for (const perm of androidPermissions) {
    assert.ok(androidBlocks.includes(perm), `DynamonsBuildSettings.cs must declare ${perm} inside #if UNITY_ANDROID`);
  }

  assert.ok(assetExists('splash/app_icon', ['.png']), 'Missing Assets/Resources/Dynamons/Art/splash/app_icon.png');
  assert.ok(assetExists('splash/logo', ['.png']), 'Missing Assets/Resources/Dynamons/Art/splash/logo.png');
  assert.ok(assetExists('map/island_lan', ['.png']), 'Missing Assets/Resources/Dynamons/Art/map/island_lan.png');
  assert.ok(assetExists('map/boat', ['.png']), 'Missing Assets/Resources/Dynamons/Art/map/boat.png');
  assert.ok(assetExists('general/enemies/lan_npc_avatar', ['.png']), 'Missing Assets/Resources/Dynamons/Art/general/enemies/lan_npc_avatar.png');
  assert.ok(assetExists('general/enemies/lan_npc_icon', ['.png']), 'Missing Assets/Resources/Dynamons/Art/general/enemies/lan_npc_icon.png');

  const releaseWf = readFileSync(join(unityDir, '.github', 'workflows', 'build-release.yml'), 'utf8');
  assert.ok(/contents:\s*write/.test(releaseWf), 'build-release.yml must request contents: write for GitHub Releases');
  assert.ok(releaseWf.includes('gh release create') && releaseWf.includes('--latest'), 'build-release.yml must create a new versioned GitHub Release per build');
  assert.ok(!releaseWf.includes('TAG="latest"'), 'build-release.yml must not overwrite a single mutable latest tag');
  assert.ok(releaseWf.includes('DynamonsNext-android-arm64.apk') && releaseWf.includes('DynamonsNext-linux-x64.tar.gz'), 'build-release.yml must publish both Android APK and Linux tarball');
  assert.ok(releaseWf.includes('versioning: Custom') && releaseWf.includes('androidVersionCode:'), 'build-release.yml must use versioning: Custom and increasing androidVersionCode for overwrite updates');
  for (const ksKey of ['androidKeystoreName:', 'androidKeystoreBase64:', 'androidKeystorePass:', 'androidKeyaliasName:', 'androidKeyaliasPass:']) {
    assert.ok(releaseWf.includes(ksKey), `build-release.yml must configure ${ksKey} for persistent Android signing`);
  }
  assert.ok(releaseWf.includes('d20a448a905a852203030f813bdb006f6b6fd254c4ca14c3da6751372eb4539f'),
    'build-release.yml Verify Android APK must pin the persistent release signing certificate SHA-256');
  for (const perm of androidPermissions) {
    assert.ok(releaseWf.includes(perm), `build-release.yml Verify Android APK must assert ${perm}`);
  }

  for (const dtoFile of [
    join(assetsDir, 'Scripts', 'Core', 'GameData.cs'),
    join(assetsDir, 'Scripts', 'Core', 'SaveData.cs'),
    join(assetsDir, 'Scripts', 'Core', 'LanProtocol.cs'),
  ]) {
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
