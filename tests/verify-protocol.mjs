import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const core = require(path.resolve(process.argv[2]));
const captures = JSON.parse(fs.readFileSync(new URL('./protocol-capture.json', import.meta.url), 'utf8'));
for (const capture of captures) {
  const request = {
    method: 'POST', path: '/license/' + capture.action,
    ts: capture.headers['x-license-ts'], nonce: capture.headers['x-license-nonce'],
    bodyHash: createHash('sha256').update(capture.raw, 'utf8').digest('hex'),
    signatureB64: capture.headers['x-license-signature'], publicKeyB64: capture.headers['x-license-public-key'],
    nowSec: Number(capture.headers['x-license-ts'])
  };
  assert.equal(core.verifyRequestSignature(request).ok, true);
  assert.equal(core.verifyRequestSignature({ ...request, path: '/verify' }).ok, false);
  assert.equal(core.verifyRequestSignature({ ...request, bodyHash: '0'.repeat(64) }).ok, false);
}
console.log(`服务端协议交叉验签通过：${captures.length} 个 C# 请求；路径及内容篡改均被拒绝。`);
