'use strict';
(() => {
  const el = id => document.getElementById(id);
  let token = '', identity = '', capabilities = null, room = null, deviceTime = 0, timeBase = 0;
  let failures = 0, polling = true, running = false, timer = 0, activeKey = '';
  const queue = [], events = [];
  const log = message => {
    events.unshift(new Date().toLocaleTimeString() + ' · ' + message);
    if (events.length > 60) events.pop();
    el('events').textContent = events.join('\n');
  };
  const status = message => { el('status').textContent = message; };
  const nonce = () => Array.from(crypto.getRandomValues(new Uint8Array(12)), n => n.toString(16).padStart(2, '0')).join('');
  async function request(path, options, maximum) {
    const abort = new AbortController(), expiry = setTimeout(() => abort.abort(), 3500);
    try {
      const response = await fetch(path, {...options, signal: abort.signal, cache: 'no-store', credentials: 'omit', redirect: 'error'});
      const size = response.headers.get('Content-Length'), declared = Number(size);
      if (!/^\d+$/.test(size || '') || !Number.isSafeInteger(declared) || declared > maximum) throw new Error('响应长度无效');
      const reader = response.body.getReader(), bytes = new Uint8Array(maximum);
      let received = 0;
      for (;;) {
        const chunk = await reader.read();
        if (chunk.done) break;
        if (received + chunk.value.length > maximum) { await reader.cancel(); throw new Error('响应超出上限'); }
        bytes.set(chunk.value, received); received += chunk.value.length;
      }
      if (received !== declared) throw new Error('响应长度不完整');
      const body = new TextDecoder('utf-8', {fatal: true}).decode(bytes.slice(0, received));
      if (!response.ok) { const error = new Error('HTTP ' + response.status + ': ' + body.trim()); error.httpStatus = response.status; throw error; }
      return body;
    } finally { clearTimeout(expiry); }
  }
  async function session() {
    const value = JSON.parse(await request('/api/v1/session', {}, 1024));
    if (value.protocol !== 1 || !/^[0-9a-f]{32}$/.test(value.token) || value.endpoint !== '/api/v1/exchange'
      || !Number.isSafeInteger(value.uptime_ms) || value.uptime_ms < 0) throw new Error('不支持的本地 API');
    token = value.token; deviceTime = value.uptime_ms; timeBase = performance.now();
  }
  async function exchange(version, command, argumentsText = '', mutation = false) {
    if (!token) await session();
    const id = nonce(), headers = {'Content-Type': 'text/plain; charset=utf-8', 'X-TriLink-Token': token};
    if (mutation) {
      headers['X-TriLink-Confirm'] = 'execute';
      const deadline = deviceTime + Math.floor(performance.now() - timeBase) + 2500;
      if (!Number.isSafeInteger(deadline) || deadline < deviceTime) throw new Error('会话时钟失效，请重新连接');
      headers['X-TriLink-Deadline'] = String(deadline);
    }
    let body;
    try {
      body = await request('/api/v1/exchange', {
        method: 'POST', headers, body: 'TRILINK/' + version + ' ' + command + ' ' + id + (argumentsText ? ' ' + argumentsText : '') + '\n'
      }, 2048);
    } catch (error) { if (!mutation && error.httpStatus === 403) token = ''; throw error; }
    const lines = body.trim().split('\n').map(s => s.replace(/\r$/, ''));
    if (command === 'SEARCH') {
      if (lines[lines.length - 1] !== 'TRILINK/1 END ' + id) throw new Error('搜索响应未完整结束');
      return lines.slice(0, -1);
    }
    if (lines.length !== 1) throw new Error('响应行数不匹配');
    const fields = lines[0].split(' ');
    if (fields[0] !== 'TRILINK/' + version || fields[2] !== id) throw new Error('响应标识不匹配；结果未知');
    if (fields[1] === 'ERROR') throw new Error(lines[0]);
    return fields;
  }
  const hex = bytes => Array.from(bytes, n => n.toString(16).padStart(2, '0')).join('').toUpperCase();
  const mac = bytes => hex(bytes).match(/../g).join(':');
  const number = (bytes, offset) => new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength).getUint32(offset, true);
  function decodeRoom(text) {
    if (!/^[0-9A-Fa-f]{264}$/.test(text)) throw new Error('Room 数据长度无效');
    const bytes = Uint8Array.from(text.match(/../g), n => parseInt(n, 16));
    if (bytes[0] !== 1 || bytes[2] > 6 || bytes[3] > 1 || bytes[123] || bytes[131]) throw new Error('Room 数据格式无效');
    return bytes;
  }
  function list(id, items) {
    const target = el(id); target.replaceChildren();
    for (const item of items) { const li = document.createElement('li'); li.textContent = item; target.appendChild(li); }
  }
  async function refresh() {
    const hello = await exchange(1, 'HELLO');
    if (hello.length !== 6 || hello[1] !== 'DEVICE' || !/^([0-9A-F]{2}:){5}[0-9A-F]{2}$/.test(hello[3])) throw new Error('节点身份响应无效');
    const caps = await exchange(3, 'CAPS');
    if (caps.length !== 10 || caps[1] !== 'CAPS' || caps[3] !== '1' || !/^[0-9A-F]{8}$/.test(caps[5])) throw new Error('能力描述响应无效');
    if (identity && (identity !== hello[3] || capabilities[5] !== caps[5])) log('设备身份或启动代次改变；已清除旧 Room 状态');
    identity = hello[3]; capabilities = caps;
    el('identity').textContent = 'MAC  ' + identity + '\n固件 ' + caps[4] + ' · boot ' + caps[5] + '\n路由正文 ' + parseInt(caps[6], 16) + ' B · 成员上限 ' + parseInt(caps[8], 16) + '\n执行模块掩码 0x' + caps[9];
    const peers = await exchange(1, 'SEARCH');
    if (peers.length > 6) throw new Error('邻居数量超过协议上限');
    list('peers', peers.length ? peers.map(line => {
      const f = line.split(' ');
      if (f.length !== 9 || f[0] !== 'TRILINK/1' || f[1] !== 'PEER' || !/^([0-9A-F]{2}:){5}[0-9A-F]{2}$/.test(f[2]) || !/^-?\d+$/.test(f[4])) throw new Error('邻居响应格式无效');
      return f[2] + ' · RSSI ' + f[4] + ' dBm';
    }) : ['暂无附近节点']);
    const state = await exchange(3, 'ROOMGET', '00');
    if (state.length !== 5 || state[1] !== 'ROOMSTATE' || state[3] !== '00') throw new Error('Room 状态响应无效');
    const bytes = decodeRoom(state[4]); room = bytes;
    const active = (bytes[130] & 1) !== 0;
    const nameEnd = bytes.indexOf(0, 100), name = new TextDecoder().decode(bytes.slice(100, Math.min(nameEnd < 0 ? 123 : nameEnd, 123)));
    el('room').textContent = active ? name + ' · ' + bytes[2] + '/6 · revision ' + number(bytes, 26) : (bytes[130] & 4 ? '正在等待加入；请使用桌面客户端查看票据' : '未加入 Room');
    list('members', active ? Array.from({length: bytes[2]}, (_, i) => mac(bytes.slice(40 + i * 10, 46 + i * 10)) + (i === 0 ? ' · leader' : ' · 成员')) : []);
    failures = 0; status(polling ? '状态已更新 · 轮询运行中' : '状态已更新 · 轮询已暂停');
  }
  function roomKey(bytes) {
    return [number(bytes, 18), number(bytes, 22), number(bytes, 26), number(bytes, 30), bytes[130] & 5].join('/');
  }
  function capture(kind) {
    if (!identity || !capabilities || !room) { log('尚无完整设备状态，请先刷新'); return null; }
    const intent = {kind, identity, boot: capabilities[5], roomKey: roomKey(room), name: el('name').value.trim()};
    if (kind === 'create') {
      if ((room[130] & 5) !== 0) { log('当前已加入或正在申请 Room'); return null; }
      const encoded = new TextEncoder().encode(intent.name);
      if (!intent.name || encoded.length > 23 || /[\x00-\x1f\x7f]/.test(intent.name)) { log('名称必须是 1～23 个 UTF-8 字节且不含控制字符'); return null; }
    } else if (!(room[130] & 1)) { log('本机未加入 Room'); return null; }
    return intent;
  }
  function matches(intent) {
    return room && identity === intent.identity && capabilities[5] === intent.boot && roomKey(room) === intent.roomKey;
  }
  function invalidateMutations() {
    for (let i = queue.length - 1; i >= 0; --i) if (queue[i].mutation || queue[i].key === 'auto') queue.splice(i, 1);
    polling = false; clearTimeout(timer); el('poll').textContent = '手动启动轮询';
  }
  async function mutate(intent) {
    await refresh();
    if (!matches(intent)) throw new Error('点击时的设备、启动代次或 Room 修订已改变；操作已取消，请重新选择');
    let packet;
    const question = intent.kind === 'create' ? '在 ' + identity + ' 创建 Room“' + intent.name + '”？' : '让 ' + identity + ' 退出当前 Room？房间可能解散或交接。';
    if (!confirm(question)) return;
    /* Modal dialogs may span a reboot or membership change. Re-read after consent. */
    await refresh();
    if (!matches(intent)) throw new Error('确认期间状态已改变，未提交写入');
    if (intent.kind === 'create') { packet = new Uint8Array(132); packet[0] = 16; packet.set(new TextEncoder().encode(intent.name), 100); }
    else { packet = room.slice(); packet[0] = 5; packet.fill(0, 124, 131); }
    room = null;
    try {
      const result = await exchange(3, 'ROOM', hex(packet), true);
      if (result[1] !== 'OK' || result[3] !== 'ROOM') throw new Error('命令回执无效；结果未知');
      log('Room 命令已被本机接受；正在读取实际状态');
    } catch (error) { invalidateMutations(); log('写入没有自动重发；待办写入已取消、轮询已暂停。请手动刷新确认实际状态：' + error.message); throw error; }
    await refresh();
  }
  function schedule() {
    clearTimeout(timer);
    if (polling) timer = setTimeout(() => enqueue('auto', refresh), 2500);
  }
  async function drain() {
    if (running) return; running = true; clearTimeout(timer);
    while (queue.length) {
      const item = queue.shift(); activeKey = item.key;
      if (item.key === 'auto' && !polling) continue;
      status('执行中 · 待办 ' + queue.length);
      try { await item.run(); }
      catch (error) {
        room = null; el('room').textContent = '状态失效，请刷新后操作';
        log(error.message); status('操作失败：' + error.message);
        if (++failures >= 5) { invalidateMutations(); log('连续 5 次失败，已暂停自动轮询；没有自动重发写入'); }
      }
    }
    activeKey = ''; running = false; schedule();
  }
  function enqueue(key, run, mutation = false) {
    if (activeKey === key || queue.some(item => item.key === key)) { status('该操作已在队列中'); return; }
    if (queue.length >= 4) { log('待办队列已满，请稍后操作'); return; }
    queue.push({key, run, mutation}); void drain();
  }
  el('refresh').addEventListener('click', () => enqueue('refresh', refresh));
  el('create').addEventListener('click', () => { const intent = capture('create'); if (intent) enqueue('create', () => mutate(intent), true); });
  el('leave').addEventListener('click', () => { const intent = capture('leave'); if (intent) enqueue('leave', () => mutate(intent), true); });
  el('poll').addEventListener('click', () => {
    polling = !polling; clearTimeout(timer);
    el('poll').textContent = polling ? '暂停轮询' : '手动启动轮询';
    if (polling) { failures = 0; enqueue('resume', async () => { token = ''; room = null; await refresh(); }); }
    else { status('自动轮询已暂停；手动操作仍可用'); log('已暂停自动轮询'); }
  });
  enqueue('refresh', refresh);
})();
