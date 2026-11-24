import { copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { parseRange, odds } from '../../web/js/core/rng.js';
import { FONT_MANIFEST } from '../../web/js/core/font-manifest.js';

const source = new URL('../../web/assets/', import.meta.url);
const resources = new URL('../Assets/Resources/Dynamons/', import.meta.url);
const list = (value) => value ?? [];
const flag = (value) => value === true || value === 'true';
const range = (value, fallback = 1) => parseRange(value ?? fallback);
const reward = (row) => ({ id: row.id ?? row.name, amount: Number(row.amount ?? 1) });
const bundle = (text) => ({ rewards: text.split(',').map(entry => { const [id, amount] = entry.split(':'); return reward({ id, amount }); }) });
const pool = (rows) => list(rows).map(row => ({ ...row, odds: odds(row.odds ?? 1), levels: range(row.levels, '1-2'), amount: range(row.amount) }));
const scripted = (row) => row ? {
  ...row, mons: list(row.mons).map(mon => ({ ...mon, level: Number(mon.level), maxHP: Number(mon.maxHP ?? 0) })),
  reward: list(row.reward).map(reward),
} : null;

export function normalize(raw) {
  return {
    mons: raw.mons.map(row => ({ ...row, bossOnly: flag(row.bossOnly) })),
    abilities: raw.abilities.map(row => ({ ...row, chance: odds(row.state_chance ?? 0),
      power: Number(row.power ?? 0), amount: Number(row.amount ?? 0), percent: Number(row.percent ?? 0), cooldown: Number(row.cooldown ?? 0) })),
    items: raw.items.map(row => ({ ...row, inBattle: flag(row.inBattle) })),
    mapNodes: raw.mapNodes.map(row => ({ ...row, arenaBattle: flag(row.arenaBattle),
      availMons: pool(row.availMons), alsoAvailMons: pool(row.alsoAvailMons),
      bossBattle: scripted(row.bossBattle), trainingBattle: scripted(row.trainingBattle) })),
    mapPacks: raw.mapPacks.map(row => ({ ...row, availMons: pool(row.availMons), captains: row.captains ? {
      ...row.captains, levelsFactor: range(row.captains.levelsFactor, '1-2'),
    } : null })),
    mapData: { ...raw.mapData, mapVisibility: raw.mapData.mapVisibility.map(row => ({ ...row, visible: row.visible.split(',') })) },
    arenaData: { ...raw.arenaData, monLevelDeltas: range(raw.arenaData.monLevelDeltas),
      monTeams: raw.arenaData.monTeams.map(row => ({ ...row, teams: row.teams.map(mons => ({ mons })) })),
      rewards: raw.arenaData.rewards.map(row => ({ ...row, rewardsPool: row.rewardsPool.map(bundle) })) },
    loot: { availLoot: raw.loot.availLoot.map(row => ({ ...row, loot: pool(row.loot) })),
      wildAmounts: raw.loot.wildAmounts.map(row => ({ ...row, coins: range(row.coins) })),
      captainAmounts: raw.loot.captainAmounts.map(row => ({ ...row, coins: range(row.coins) })) },
    products: raw.lootShop.products.map(row => ({ ...row, price: row.id === 'chest' && !Number(row.price) ? 150 : Number(row.price),
      loot: row.loot.map(step => ({ amount: Number(step.amount), pool: pool(step.pool) })) })),
    shopMessage: raw.lootShop.messages?.find(row => row.id === 'from_menu')?.msg ?? "I wonder what you'll get!",
    battleData: { ...raw.battleData, hypnoBackfireChance: odds(raw.battleData.hypnoBackfireChance),
      aiming: raw.battleData.aiming.map(row => ({ upTo: row.upTo, miss: odds(row.miss), crit: odds(row.crit) })) },
    tutorial: { chooseMons: raw.tutorial.chooseMons, startingCoins: Number(raw.tutorial.startingCoins),
      startingItems: raw.tutorial.startingItems.map(text => { const [id, amount] = text.split(','); return reward({ id, amount }); }) },
    dialogs: raw.dialogs,
    fonts: Object.entries(FONT_MANIFEST).map(([id, font]) => {
      const rawKern = JSON.parse(readFileSync(new URL(`../../web/assets/hd-images/fonts/${id}/kern.txt`, import.meta.url), 'utf8'));
      const glyphs = Object.entries(font.glyphs).map(([code, g]) => ({
        code: Number(code), w: g[0], h: g[1], top: g[3] ?? -1, bottom: g[5] ?? -1,
      }));
      const kerns = [];
      for (const [leftKey, row] of Object.entries(rawKern)) {
        const left = leftKey === 'any' ? 0 : leftKey.codePointAt(0);
        for (const [rightKey, adjust] of Object.entries(row)) {
          kerns.push({ left, right: rightKey === 'any' ? 0 : rightKey.codePointAt(0), adjust });
        }
      }
      return { id, capTop: font.capTop, baseline: font.baseline, chars: glyphs.map(g => g.code), glyphs, kerns };
    }),
  };
}

export function validateData(data) {
  const ids = (rows, key = 'id') => {
    const values = rows.map(row => row[key]);
    if (values.length !== new Set(values).size || values.some(x => x == null || x === '')) throw Error(`Invalid or duplicate ${key}`);
    return new Set(values);
  };
  const mons = ids(data.mons), moves = ids(data.abilities), items = ids(data.items), nodes = ids(data.mapNodes), packs = ids(data.mapPacks);
  const dialogs = ids(data.dialogs);
  items.add('coins');
  const demand = (set, key, owner) => { if (!set.has(key)) throw Error(`${owner}: missing ${key}`); };
  const references = (rows, set, owner) => rows.forEach(row => demand(set, row.id, owner));
  references(data.tutorial.chooseMons, mons, 'starter');
  references(data.tutorial.startingItems, items, 'starting items');
  for (const id of ['nyxalon', 'charmander', 'bulbasaur', 'squirtle']) demand(mons, id, 'Starter Event');
  for (const id of ['discatch', 'starter_ticket', 'heal_spray', 'antidote', 'atk_pepper', 'def_berry']) demand(items, id, 'game rules');
  for (const item of data.items.filter(row => row.inBattle && row.id !== 'discatch')) {
    demand(moves, item.id.startsWith('can_') ? `can-${item.id.slice(4)}` : `item-${item.id}`, item.id);
  }
  const dialogRefs = (row, owner) => {
    for (const id of [row.dialog_start, row.dialog_win, row.dialog_lose].filter(Boolean)) demand(dialogs, id, owner);
  };
  for (const mon of data.mons) {
    if (!mon.lvlTable.length) throw Error(`${mon.id}: empty stats table`);
    references(mon.abilities, moves, mon.id);
    if (mon.passive?.abilityId) demand(moves, mon.passive.abilityId, mon.id);
  }
  for (const pack of data.mapPacks) references(pack.availMons, mons, pack.id);
  for (const node of data.mapNodes) {
    demand(packs, node.pack, node.id);
    if (node.connect !== -1) demand(nodes, node.connect, node.id);
    if (node.unlocksPack) demand(packs, node.unlocksPack, node.id);
    references([...node.availMons, ...node.alsoAvailMons], mons, node.id);
    for (const battle of [node.bossBattle, node.trainingBattle].filter(Boolean)) {
      references(battle.mons, mons, node.id);
      references(battle.reward, items, node.id);
      dialogRefs(battle, node.id);
    }
    if (node.arenaDialogs) dialogRefs(node.arenaDialogs, node.id);
  }
  demand(nodes, 1, 'starting node');
  const unlocked = new Set(['a']);
  const reachable = new Set([1]);
  for (let before = -1; before !== unlocked.size + reachable.size;) {
    before = unlocked.size + reachable.size;
    for (const node of data.mapNodes) {
      if (!unlocked.has(node.pack)) continue;
      if (reachable.has(node.connect)) reachable.add(node.id);
      if (reachable.has(node.id) && node.unlocksPack) unlocked.add(node.unlocksPack);
      if (reachable.has(node.id) && node.connect !== -1) {
        const parent = data.mapNodes.find(row => row.id === node.connect);
        if (unlocked.has(parent.pack)) reachable.add(parent.id);
      }
    }
  }
  for (const node of data.mapNodes) if (!reachable.has(node.id)) throw Error(`Unreachable map node ${node.id} (${node.pack})`);
  for (const row of data.mapData.mapVisibility) for (const id of row.visible) demand(packs, id, 'visibility');
  for (const row of data.arenaData.monTeams) for (const team of row.teams) for (const id of team.mons) demand(mons, id, 'arena team');
  for (const row of data.arenaData.rewards) for (const bundle of row.rewardsPool) references(bundle.rewards, items, 'arena rewards');
  for (const row of data.loot.availLoot) references(row.loot, items, 'battle loot');
  for (const product of data.products) for (const row of product.loot) references(row.pool, items, product.id);
}

export function runtimeAssets(data) {
  return [...new Set([
    ...data.mons.flatMap(({ id }) => ['front','back','icon'].map(view => `Art/general/mons/${id}/${view}.png`)),
    ...data.items.map(({ id }) => `Art/items/icons/${id}.png`),
    ...data.abilities.map(({ id }) => id.startsWith('item-') ? `Art/items/icons/${id.slice(5)}.png` : id.startsWith('can-') ? `Art/items/icons/${id.replace('can-','can_')}.png` : `Art/session/abilities/icons/${id}.png`),
    ...data.abilities.flatMap(({visualSteps}) => list(visualSteps).filter(step => step.action.startsWith('sfx-')).map(({action}) => `sounds/attack_${action.slice(4)}.ogg`)),
    ...data.fonts.flatMap(font => font.chars.map(code => `Art/fonts/${font.id}/c${code}.png`)),
    ...Array.from({ length: 4 }, (_, i) => `Art/map/bgs/bg${i + 1}.jpg`),
    ...[...new Set(data.mapNodes.map(node => node.bg).filter(Boolean).concat('arena'))].map(bg => `Art/session/bgs/${bg}.jpg`),
    ...data.tutorial.chooseMons.map(row => `Art/onboarding/choose_panel_${row.type}.png`),
    'Art/splash/splash.jpg', 'Art/splash/logo.png', 'Art/splash/btn_play.png', 'Art/onboarding/btn_choose.png',
    'Art/onboarding/btn_yes.png', 'Art/onboarding/choose_panel_earth.png', 'Art/onboarding/choose_panel_fire.png',
    'Art/onboarding/choose_panel_water.png', 'Art/onboarding/choose_title.png', 'Art/onboarding/hand.png',
    ...[...new Set(['stephan', ...data.mapPacks.flatMap(row => row.captains?.avail ?? []),
      ...data.mapNodes.flatMap(node => [node.bossBattle?.enemy, node.trainingBattle?.enemy]).filter(Boolean)])].map(id => `Art/general/enemies/${id}_icon.png`),
    ...['chuk', 'jenni', 'klyde', 'kolin', 'manifesto', 'nora', 'patrik', 'remi', 'shelldon', 'sofia', 'sumsum', 'training', 'zak'].map(id => `Art/general/enemies/${id}_avatar.png`),
    ...data.dialogs.flatMap(row => row.content.filter(line => line.who?.includes(':')).map(line => `Art/general/dialog/${line.who.split(':')[1]}.png`)),
    'Art/general/dialog/speech_bubble.png', 'Art/general/dialog/jovani-hand.png',
    'Art/general/hud_bg.png', 'Art/general/hud_coins.png', 'Art/general/badge_levelup.png', 'Art/general/badge_red.png',
    'Art/general/btn_arrow_right.png', 'Art/general/btn_cancel.png', 'Art/general/btn_close.png', 'Art/general/btn_cool.png',
    'Art/general/btn_levelup.png', 'Art/general/btn_levelup_text.png', 'Art/general/btn_sound_off.png', 'Art/general/btn_sound_on.png',
    'Art/general/icon_aim.png', 'Art/general/icon_atk.png', 'Art/general/icon_def.png', 'Art/general/levelup_blast.png',
    'Art/general/pedestal.png', 'Art/general/reward_slot.png', 'Art/general/statsbar_bg.png', 'Art/general/statsbar_fill.png',
    'Art/general/statsbar_fill_red.png', 'Art/general/statsbar_fill_white.png', 'Art/general/xpbar_bg.png', 'Art/general/xpbar_fill.png',
    ...['dark', 'earth', 'electric', 'fire', 'water'].flatMap(t => [
      `Art/general/huds/pane_big_${t}.png`, `Art/general/huds/pane_mini_${t}.png`,
      `Art/general/huds/pane_thumb_${t}.png`, `Art/general/huds/pane_tooltip_${t}.png`,
    ]),
    'Art/general/huds/hpbar_big_bg.png', 'Art/general/huds/hpbar_big_fill.png', 'Art/general/huds/hpbar_big_fill_red.png',
    'Art/general/huds/hpbar_big_fill_white.png', 'Art/general/huds/hpbar_med_bg.png', 'Art/general/huds/hpbar_med_fill.png',
    'Art/general/huds/hpbar_small_bg.png', 'Art/general/huds/hpbar_small_fill.png', 'Art/general/huds/tip_strong.png', 'Art/general/huds/tip_weak.png',
    ...['blast', 'blast_water', 'boulder_earth', 'boulder_fire', 'bubble', 'can_open', 'enflame', 'fire_spin', 'freeze_gas',
      'heatwave', 'hypno_fx', 'ice_shield', 'poison_gas', 'rage_fx_back', 'rage_fx_front', 'rainbow', 'rain_fire', 'rain_ice',
      'rain_leaf', 'rain_pods', 'rock_shield', 'sick_fx', 'slash', 'smoke', 'splash', 'splash_fire', 'spores_cloud', 'spray',
      'tentacle_back', 'tentacle_top', 'vine_back', 'vine_top', 'wave', 'winter'].map(id => `Art/session/abilities/fx/${id}.png`),
    'Art/session/mon_shadow.png', 'Art/session/ui/btn_back.png', 'Art/session/ui/btn_catch.png', 'Art/session/ui/btn_catch_off.png',
    'Art/session/ui/btn_escape.png', 'Art/session/ui/btn_mega.png', 'Art/session/ui/btn_more.png', 'Art/session/ui/catch_disk.png',
    'Art/session/ui/discatch_glow.png', 'Art/session/ui/disk_icon.png', 'Art/session/ui/icon_hypno.png', 'Art/session/ui/icon_off.png',
    'Art/session/ui/icon_rage.png', 'Art/session/ui/icon_sick.png', 'Art/session/ui/icon_sleep.png', 'Art/session/ui/report_backfire.png',
    'Art/session/ui/report_critical.png', 'Art/session/ui/report_rage.png', 'Art/session/ui/report_sick.png', 'Art/session/ui/report_strong.png',
    'Art/session/ui/report_weak.png', 'Art/session/ui/turn.png', 'Art/session/endscreen/btn_later.png', 'Art/session/endscreen/rewards_modal.png',
    'Art/session/endscreen/superlative1.png', 'Art/session/endscreen/superlative2.png', 'Art/session/endscreen/superlative3.png',
    'Art/session/endscreen/superlative4.png', 'Art/session/endscreen/you_win.png',
    ...data.products.map(row => `Art/iap-shop/products/product_${row.id === 'chest' ? 'coolchest' : row.id}.png`),
    'Art/shop/bg_shop.png', 'Art/shop/btn_buy.png', 'Art/shop/btn_buy_off.png', 'Art/shop/btn_watch.png', 'Art/shop/btn_watch_off.png',
    'Art/shop/divider_contains.png', 'Art/shop/divider_maycontain.png', 'Art/shop/label_free.png', 'Art/shop/label_outofstock.png',
    'Art/shop/loot_explosion.png', 'Art/shop/loot_lights.png', 'Art/shop/product_select.png', 'Art/shop/product_slot.png',
    'Art/shop/products/product_bag.png', 'Art/shop/products/product_chest.png',
    'Art/team/bg_team.jpg', 'Art/team/bg_team_slots.png', 'Art/team/btn_back_all.png', 'Art/team/btn_back_map.png',
    'Art/team/btn_confirm_release.png', 'Art/team/btn_heal_small_kit.png', 'Art/team/btn_heal_small_nokit.png', 'Art/team/hud_mons.png',
    'Art/team/team_slot.png', 'Art/team/info/btn_heal_kit.png', 'Art/team/info/btn_heal_nokit.png', 'Art/team/info/btn_release.png',
    'Art/team/info/modal_release.png', 'Art/team/info/skill_description.png', 'Art/team/info/skill_lock_overlay.png', 'Art/team/info/skill_select.png',
    'Art/map/node.png', 'Art/map/node_arena.png', 'Art/map/avatar.png', 'Art/map/marker_battle.png', 'Art/map/marker_arena.png',
    'Art/map/marker_off.png', 'Art/map/marker_research.png', 'Art/map/path_dot.png', 'Art/map/msg_heal.png',
    'Art/map/btn_items.png', 'Art/map/btn_like.png', 'Art/map/btn_shop.png', 'Art/map/btn_team.png',
    'Art/map/bgs/bg1.jpg', 'Art/map/bgs/bg2.jpg', 'Art/map/bgs/bg3.jpg', 'Art/map/bgs/bg4.jpg',
    'Art/map/modals/enemy/bg_arena.png', 'Art/map/modals/enemy/bg_boss.png', 'Art/map/modals/enemy/bg_captain.png', 'Art/map/modals/enemy/btn_fight.png',
    'Art/items/icons/coins.png', 'Art/items/icons/levelup_unknown.png', 'Art/items/icons/more_items.png', 'Art/items/icons/starter_ticket.png',
    'musics/music_map_loop.ogg', 'musics/music_battle_loop.ogg', 'musics/music_arena_loop.ogg',
    ...['attack_blizzard', 'attack_boulder_pile', 'attack_bubble_burst', 'attack_confuse', 'attack_enflame', 'attack_enrage',
      'attack_fire_storm', 'attack_flame_swirl', 'attack_flash', 'attack_furnace', 'attack_hellfire', 'attack_iceberg',
      'attack_jungle_whip', 'attack_leaf_strike', 'attack_miss', 'attack_rainbow_beam', 'attack_revive', 'attack_rock_shield',
      'attack_scratch', 'attack_smoke_screen', 'attack_sonic_shock', 'attack_spark', 'attack_spore_shot', 'attack_thunder_storm',
      'attack_vine_squeeze', 'attack_volcano', 'attack_water_blast', 'attack_winter_chills', 'button_click', 'canned_powerup',
      'catch_failure', 'catch_success', 'chest_opening', 'cool_button', 'critical_hit', 'eating_powerup', 'faint', 'hypno',
      'level_up_animation', 'level_up_indicator', 'loot_bag_opening', 'music_arena_start', 'music_battle_boss_start',
      'music_battle_start', 'purchase', 'screen_close', 'screen_open', 'sick', 'spray_powerup', 'stat_lowered', 'stat_raised',
      'use_healing_kit', 'win_battle'].map(id => `sounds/${id}.ogg`),
  ])];
}

function assetSources() {
  const result = new Map();
  for (const folder of ['images', 'hd-images', 'sounds', 'musics']) {
    const root = new URL(`${folder}/`, source);
    const art = folder.endsWith('images');
    for (const path of readdirSync(root, { recursive: true }).sort()) {
      if (!(art ? /\.(png|jpe?g|txt)$/i : /\.ogg$/i).test(path)) continue;
      result.set(`${art ? 'Art' : folder}/${path}`, new URL(path, root));
    }
  }
  // Unity keys omit file extensions. Prefer PNG when both forms share a stem.
  for (const path of result.keys()) if (/\.jpe?g$/i.test(path) && result.has(path.replace(/\.jpe?g$/i, '.png'))) result.delete(path);
  return result;
}

if (process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url) {
  const data = normalize(JSON.parse(readFileSync(new URL('data/gameplay.js', source), 'utf8')));
  validateData(data);
  const assets = assetSources();
  for (const path of runtimeAssets(data)) if (!assets.has(path) || !existsSync(assets.get(path))) throw Error(`Missing runtime asset ${path}`);
  const json = JSON.stringify(data);
  for (const [path, from] of assets) {
    const to = new URL(path, resources);
    mkdirSync(dirname(fileURLToPath(to)), { recursive: true });
    copyFileSync(from, to);
  }
  writeFileSync(new URL('gameplay.json', resources), `${json}\n`);
  console.log(`Imported ${assets.size} assets and gameplay data.`);
}
