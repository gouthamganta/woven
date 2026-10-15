import { randomBytes, createECDH } from 'node:crypto';
import { mkdirSync, existsSync, writeFileSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
const directory = fileURLToPath(new URL('../.local/full-qa-runtime/', import.meta.url));
mkdirSync(directory, { recursive: true });
const file = `${directory}/runtime.env`;
if (!existsSync(file)) {
  writeFileSync(file, `WOVEN_QA_ENVIRONMENT=Development\nWOVEN_QA_JWT_KEY=${randomBytes(48).toString('base64url')}\nWOVEN_QA_ENCRYPTION_KEY=${randomBytes(32).toString('base64')}\nWOVEN_QA_ANALYTICS_SALT=${randomBytes(32).toString('base64url')}\n`);
}
if (!readFileSync(file, 'utf8').includes('WOVEN_QA_VAPID_PUBLIC=')) {
  const vapid = createECDH('prime256v1'); vapid.generateKeys();
  writeFileSync(file, `WOVEN_QA_VAPID_PUBLIC=${vapid.getPublicKey().toString('base64url')}\nWOVEN_QA_VAPID_PRIVATE=${vapid.getPrivateKey().toString('base64url')}\n`, { flag: 'a' });
}
console.log('Local-only runtime configuration ready. Keys not printed. Backend network denies external traffic.');
