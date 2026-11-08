import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { zipSync, unzipSync } from 'fflate';
import { parse } from 'yaml';

const dllName = 'AlwaysOfficial.dll';
const archiveName = 'AlwaysOfficial-modloader.zip';
const semver = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/;
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');

export async function buildSite({ releases, product, baseUrl, outDir, download }) {
  const stable = {};
  const beta = {};
  for (const release of releases) {
    const version = release.tag_name?.replace(/^v/, '');
    if (release.draft || !release.tag_name?.startsWith('v') || !semver.test(version)) continue;
    const dll = release.assets.find(asset => asset.name === dllName);
    const metadata = release.assets.find(asset => asset.name === 'version.yaml');
    if (!dll || !metadata) {
      console.log(`Skipping ${release.tag_name}: missing DLL or version.yaml`);
      continue;
    }
    const bytes = await download(dll);
    const versionInfo = parse(Buffer.from(await download(metadata)).toString('utf8'));
    if (versionInfo.executable !== dllName || !Array.isArray(versionInfo.dependencies)
        || !versionInfo.dependencies.every(dep => typeof dep.id === 'string' && typeof dep.version === 'string')) {
      throw new Error(`${release.tag_name}: invalid modloader metadata`);
    }
    const archive = zipSync({ [dllName]: [bytes, { mtime: new Date(2020, 0, 1) }] }, { level: 9 });
    const files = unzipSync(archive);
    if (Object.keys(files).join() !== dllName || sha256(files[dllName]) !== sha256(bytes)) {
      throw new Error(`${release.tag_name}: archive verification failed`);
    }
    const relativePath = `files/${version}/${archiveName}`;
    const path = join(outDir, 'modloader', relativePath);
    await mkdir(dirname(path), { recursive: true });
    await writeFile(path, archive);
    const entry = {
      executable: versionInfo.executable,
      dependencies: versionInfo.dependencies,
      changelog: versionInfo.changelog ?? '',
      download_link: `${baseUrl}/modloader/${relativePath}`,
      hash_sha256: sha256(archive),
    };
    beta[version] = entry;
    if (!release.prerelease && !version.includes('-')) stable[version] = entry;
  }

  for (const [channel, versions] of [['', stable], ['beta', beta]]) {
    const directory = join(outDir, 'modloader', channel);
    await mkdir(directory, { recursive: true });
    const index = { api: 1, game: 'TmForever', products: { AlwaysOfficial: { ...product, versions } } };
    await writeFile(join(directory, 'index.json'), JSON.stringify(index, null, 2) + '\n');
    const url = `${baseUrl}/modloader/${channel ? 'beta/' : ''}`;
    await writeFile(join(directory, 'index.html'), `<!doctype html><meta charset="utf-8"><title>Always Official ModLoader server</title><p>Add this URL under <code>servers:</code> in TMLoader's config.yaml:</p><pre>${url}</pre><p><a href="index.json">Version index</a></p>\n`);
    console.log(`${url}: ${Object.keys(versions).join(', ') || '(no releases yet)'}`);
  }
  await writeFile(join(outDir, '.nojekyll'), '');
}

async function github(url, accept = 'application/vnd.github+json') {
  const response = await fetch(url, {
    headers: { Accept: accept, Authorization: `Bearer ${process.env.GH_TOKEN}`, 'X-GitHub-Api-Version': '2022-11-28' },
    signal: AbortSignal.timeout(120_000),
  });
  if (!response.ok) throw new Error(`GitHub returned ${response.status} for ${url}`);
  return response;
}

async function main() {
  const repo = process.env.GITHUB_REPOSITORY;
  const baseUrl = process.env.PAGES_BASE_URL?.replace(/\/$/, '');
  if (!repo || !baseUrl?.startsWith('https://') || !process.env.GH_TOKEN) {
    throw new Error('GITHUB_REPOSITORY, GH_TOKEN and HTTPS PAGES_BASE_URL are required');
  }
  const releases = [];
  for (let page = 1; ; page++) {
    const batch = await (await github(`https://api.github.com/repos/${repo}/releases?per_page=100&page=${page}`)).json();
    releases.push(...batch);
    if (batch.length < 100) break;
  }
  const product = parse(await readFile(new URL('../product/description.yaml', import.meta.url), 'utf8'));
  product.homepage = `https://github.com/${repo}`;
  await buildSite({
    releases, product, baseUrl, outDir: process.argv[2] ?? 'site',
    download: async asset => {
      const bytes = new Uint8Array(await (await github(asset.url, 'application/octet-stream')).arrayBuffer());
      if (asset.digest && asset.digest !== `sha256:${sha256(bytes)}`) throw new Error(`Digest mismatch: ${asset.name}`);
      return bytes;
    },
  });
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch(error => { console.error(error); process.exitCode = 1; });
}
