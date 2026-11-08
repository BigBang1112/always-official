import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { unzipSync } from 'fflate';
import { parse } from 'yaml';
import { buildSite } from './modloader-server.mjs';

test('published feeds select releases and serve verified DLL-only archives', async () => {
  const outDir = await mkdtemp(join(tmpdir(), 'always-official-pages-'));
  const dll = Buffer.from('fixture DLL');
  const metadata = await readFile(new URL('../product/version.yaml', import.meta.url));
  const product = parse(await readFile(new URL('../product/description.yaml', import.meta.url), 'utf8'));
  const release = (tag, options = {}) => ({
    tag_name: tag, assets: [{ name: 'AlwaysOfficial.dll' }, { name: 'version.yaml' }], ...options,
  });
  try {
    await buildSite({
      outDir, product, baseUrl: 'https://example.github.io/AlwaysOfficial',
      releases: [release('v0.1.0'), release('v0.2.0-beta.1'), release('v0.2.0', { prerelease: true }),
        release('v0.3.0', { draft: true }), release('v0.0.1', { assets: [] }), release('not-a-version')],
      download: async asset => asset.name.endsWith('.dll') ? dll : metadata,
    });
    const stable = JSON.parse(await readFile(join(outDir, 'modloader/index.json')));
    const beta = JSON.parse(await readFile(join(outDir, 'modloader/beta/index.json')));
    assert.equal(stable.api, 1);
    assert.equal(stable.game, 'TmForever');
    assert.equal(stable.products.AlwaysOfficial.name, 'Always Official');
    assert.deepEqual(Object.keys(stable.products.AlwaysOfficial.versions), ['0.1.0']);
    assert.deepEqual(Object.keys(beta.products.AlwaysOfficial.versions), ['0.1.0', '0.2.0-beta.1', '0.2.0']);
    const entry = stable.products.AlwaysOfficial.versions['0.1.0'];
    const archive = await readFile(join(outDir, 'modloader/files/0.1.0/AlwaysOfficial-modloader.zip'));
    assert.equal(entry.hash_sha256, createHash('sha256').update(archive).digest('hex'));
    assert.equal(entry.download_link, 'https://example.github.io/AlwaysOfficial/modloader/files/0.1.0/AlwaysOfficial-modloader.zip');
    assert.deepEqual(entry.dependencies, [{ id: 'CoreMod', version: '^1.0.1' }]);
    assert.equal(entry.changelog, parse(metadata.toString()).changelog);
    const files = unzipSync(archive);
    assert.deepEqual(Object.keys(files), ['AlwaysOfficial.dll']);
    assert.deepEqual(Buffer.from(files['AlwaysOfficial.dll']), dll);
  } finally {
    await rm(outDir, { recursive: true, force: true });
  }
});

test('rejects metadata that would install the wrong executable', async () => {
  await assert.rejects(buildSite({
    releases: [{ tag_name: 'v0.1.0', assets: [{ name: 'AlwaysOfficial.dll' }, { name: 'version.yaml' }] }],
    product: {}, baseUrl: 'https://example.com', outDir: 'unused',
    download: async asset => Buffer.from(asset.name.endsWith('.dll') ? 'DLL' : 'executable: wrong.dll\ndependencies: []'),
  }), /invalid modloader metadata/);
});
