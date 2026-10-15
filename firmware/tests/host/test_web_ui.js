'use strict';
// Actual embedded app.js in a VM, fake DOM/HTTP and controlled logical timers.
// This is software evidence, not browser rendering, USB or RF acceptance.
const fs = require('fs'), path = require('path'), vm = require('vm');
const {webcrypto} = require('crypto');
const source = fs.readFileSync(path.join(__dirname, '../../components/trilink_web/www/app.js'), 'utf8');
let checks = 0;
function check(value, description) { ++checks; if (!value) throw new Error(description); }
const tick = () => new Promise(resolve => setImmediate(resolve));
async function settle() { for (let i = 0; i < 45; ++i) await tick(); }
function harness(active = false) {
  const elements = new Map(), timers = new Map(); let nextTimer = 0, time = 0;
  function element(id) {
    if (!elements.has(id)) elements.set(id, {
      textContent: '', value: id === 'name' ? 'TriLink Room' : '', children: [], listeners: {},
      addEventListener(name, fn) { this.listeners[name] = fn; },
      appendChild(child) { this.children.push(child); }, replaceChildren() { this.children = []; }
    });
    return elements.get(id);
  }
  const device = {active, waiting: false, room: 1, revision: 1, term: 1, boot: '00000001', mac: '98:C3:77:B2:D0:D4',
    sessionCalls: 0, writes: [], requests: [], failReads: 0, rejectToken: false, failWrite: false,
    beforeFetch: null, consent: null, name: 'Original Room', confirmationCount: 0};
  function roomBytes() {
    const bytes = new Uint8Array(132), view = new DataView(bytes.buffer); bytes[0] = 1;
    if (device.active) {
      bytes[2] = 1; bytes[130] = 1; view.setUint32(18, device.room, true);
      view.setUint32(22, 1, true); view.setUint32(26, device.revision, true); view.setUint32(30, device.term, true);
      bytes.set(Buffer.from(device.mac.replaceAll(':', ''), 'hex'), 40); view.setUint32(46, 1, true);
      bytes.set(new TextEncoder().encode(device.name), 100);
      bytes.fill(0x41, 124, 130); // Original snapshot target must not leak into a leave command.
    }
    if (device.waiting) bytes[130] |= 4;
    return Buffer.from(bytes).toString('hex').toUpperCase();
  }
  const response = (body, status = 200, declared) => {
    const bytes = new TextEncoder().encode(body); let done = false;
    return {ok: status === 200, status, headers: {get: () => declared === undefined ? String(bytes.length) : declared},
      body: {getReader: () => ({read: async () => { if (done) return {done: true}; done = true; return {done: false, value: bytes}; }, cancel: async () => { done = true; }})}};
  };
  const context = {
    document: {getElementById: element, createElement: () => ({textContent: ''})},
    crypto: webcrypto, TextEncoder, TextDecoder, Uint8Array, DataView, AbortController,
    performance: {now: () => time}, Date, console,
    setTimeout(fn, delay) { const id = ++nextTimer; timers.set(id, {fn, delay}); return id; },
    clearTimeout(id) { timers.delete(id); },
    confirm(question) { ++device.confirmationCount; if (device.consent) return device.consent(question); return true; },
    fetch: async (url, options) => {
      if (url === '/api/v1/session') {
        ++device.sessionCalls; device.rejectToken = false;
        return response(JSON.stringify({protocol: 1, token: '0123456789abcdef0123456789abcdef', firmware: '0.16.0', endpoint: '/api/v1/exchange', uptime_ms: 1000 + time}));
      }
      const parts = options.body.trim().split(' '), verb = parts[1], nonce = parts[2];
      device.requests.push(verb);
      if (device.beforeFetch) await device.beforeFetch(verb);
      if (device.rejectToken) return response('SESSION_REQUIRED\n', 403);
      if (verb !== 'ROOM' && device.failReads > 0) { --device.failReads; throw new Error('injected read failure'); }
      if (verb === 'HELLO') return response('TRILINK/1 DEVICE ' + nonce + ' ' + device.mac + ' VHJpTGluaw== 000000E3\n');
      if (verb === 'CAPS') return response('TRILINK/3 CAPS ' + nonce + ' 1 0.16.0 ' + device.boot + ' 84 06 06 01\n');
      if (verb === 'SEARCH') return response('TRILINK/1 END ' + nonce + '\n');
      if (verb === 'ROOMGET') return response('TRILINK/3 ROOMSTATE ' + nonce + ' 00 ' + roomBytes() + '\n');
      if (verb === 'ROOM') {
        check(options.headers['X-TriLink-Confirm'] === 'execute', 'mutation explicit confirmation header');
        check(Number(options.headers['X-TriLink-Deadline']) > 1000 + time, 'mutation device-clock expiration header');
        const bytes = Buffer.from(parts[3], 'hex'); device.writes.push(bytes);
        if (bytes[0] === 16) { device.active = true; device.name = bytes.subarray(100, 123).toString().replace(/\0.*$/, ''); }
        else if (bytes[0] === 5) device.active = false;
        if (device.failWrite) throw new Error('injected unknown write result');
        return response('TRILINK/3 OK ' + nonce + ' ROOM\n');
      }
      throw new Error('Unexpected command ' + verb);
    }
  };
  vm.runInNewContext(source, context, {filename: 'embedded-app.js'});
  return {
    device, el: element, click(id) { element(id).listeners.click(); },
    hasPoll: () => [...timers.values()].some(t => t.delay === 2500),
    poll() { const entry = [...timers.entries()].find(([, t]) => t.delay === 2500); if (!entry) throw new Error('No scheduled poll'); timers.delete(entry[0]); time += 2500; entry[1].fn(); },
    blockNextHello() {
      let release, once = true; const wait = new Promise(resolve => { release = resolve; });
      device.beforeFetch = verb => { if (verb === 'HELLO' && once) { once = false; return wait; } };
      return () => { device.beforeFetch = null; release(); };
    }
  };
}

(async () => {
  const basic = harness(); await settle();
  check(basic.el('identity').textContent.includes('0.16.0'), 'real identity/capability render');
  check(basic.device.writes.length === 0, 'initial refresh never writes');
  check(basic.hasPoll(), 'poll starts after completed refresh');
  basic.device.failReads = 5;
  for (let i = 0; i < 5; ++i) { basic.poll(); await settle(); }
  check(!basic.hasPoll(), 'fifth failure removes auto timer');
  check(basic.el('poll').textContent === '手动启动轮询', 'manual resume surfaced');
  const beforeResume = basic.device.sessionCalls;
  basic.click('poll'); await settle();
  check(basic.hasPoll(), 'manual resume restarts successful poll');
  check(basic.device.sessionCalls === beforeResume + 1, 'manual resume fetches fresh RAM session');
  const releasePaused = basic.blockNextHello(); basic.poll(); basic.click('poll'); releasePaused(); await settle();
  check(!basic.hasPoll(), 'late refresh completion cannot undo manual pause');
  basic.device.rejectToken = true; basic.click('refresh'); await settle();
  const countBefore403Recovery = basic.device.sessionCalls; basic.click('refresh'); await settle();
  check(basic.device.sessionCalls === countBefore403Recovery + 1, 'next manual read replaces rejected token without replay');
  check(!basic.hasPoll(), 'manual read does not resume paused polling');

  const create = harness(); await settle();
  create.el('name').value = 'Clicked name'; const releaseCreate = create.blockNextHello();
  create.click('create'); create.el('name').value = 'Changed later'; create.click('create');
  releaseCreate(); await settle();
  check(create.device.writes.length === 1, 'active mutation double click is deduplicated');
  check(create.device.name === 'Clicked name', 'intent captures name at click time');
  check(create.device.confirmationCount === 1, 'single confirmation for single intent');
  create.click('leave'); await settle();
  check(create.device.writes.length === 2 && create.device.writes[1][0] === 5, 'leave operation completes through real page path');
  check(create.device.writes[1].subarray(124, 131).every(n => n === 0), 'leave clears target and status bytes');

  const stale = harness(true); await settle();
  const releaseStale = stale.blockNextHello(); stale.click('refresh'); stale.click('leave');
  stale.device.room = 2; stale.device.revision = 2; releaseStale(); await settle();
  check(stale.device.writes.length === 0, 'queued old-room exit cannot target newly selected room');
  check(stale.el('events').textContent.includes('操作已取消'), 'stale intent explains cancellation');

  const reboot = harness(true); await settle();
  reboot.device.consent = () => { reboot.device.boot = '00000002'; return true; };
  reboot.click('leave'); await settle();
  check(reboot.device.writes.length === 0, 'post-confirm refresh rejects reboot during modal');
  check(reboot.el('events').textContent.includes('确认期间状态已改变'), 'modal change is visible');

  const revision = harness(true); await settle();
  revision.device.consent = () => { ++revision.device.revision; return true; };
  revision.click('leave'); await settle();
  check(revision.device.writes.length === 0, 'post-confirm refresh rejects room revision change');

  const unknown = harness(); await settle(); unknown.device.failWrite = true;
  unknown.click('create'); await settle();
  check(unknown.device.writes.length === 1, 'unknown write submitted once');
  check(!unknown.hasPoll(), 'unknown write pauses automatic work');
  check(unknown.el('events').textContent.includes('待办写入已取消'), 'unknown write invalidates queued mutations visibly');
  unknown.click('refresh'); await settle();
  check(unknown.device.writes.length === 1, 'manual status refresh never replays unknown write');
  check(!unknown.hasPoll(), 'refresh after unknown does not silently resume');

  const invalid = harness(); await settle(); invalid.el('name').value = 'bad\u0001name'; invalid.click('create'); await settle();
  check(invalid.device.writes.length === 0 && invalid.device.confirmationCount === 0, 'control-character name rejected before consent/write');
  console.log('PASS embedded web UI: ' + checks + ' checks (fake DOM/HTTP/logical timers; no hardware)');
})().catch(error => { console.error(error.stack); process.exitCode = 1; });
