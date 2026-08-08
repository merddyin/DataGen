import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {mkdtemp, mkdir, rm, symlink, writeFile} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import test from 'node:test';

import {
  HEADER_READ_LIMIT,
  EXCLUDED_ROOT_DIRECTORY_NAMES,
  RISKY_EXTENSIONS,
  ImageInputGuardError,
  readBoundedHeader,
  scanImageInputs,
} from './check-image-inputs.mjs';

const EXPECTED_RISKY_EXTENSIONS = [
  '.avci',
  '.avif',
  '.avifs',
  '.heic',
  '.heif',
  '.heifs',
  '.hif',
  '.icns',
  '.jxl',
];

const HEIF_BRANDS = [
  'avci',
  'avcs',
  'avif',
  'avis',
  'heic',
  'heis',
  'heix',
  'heim',
  'hevc',
  'hevs',
  'hevx',
  'hevm',
  'mif1',
  'msf1',
];

async function withFixture(run) {
  const root = await mkdtemp(path.join(tmpdir(), 'datagen-image-guard-'));
  try {
    await run(root);
  } finally {
    await rm(root, {recursive: true, force: true});
  }
}

async function expectGuardFailure(root, expectedText) {
  await assert.rejects(
    scanImageInputs({inputRoots: [root]}),
    (error) => {
      assert.ok(error instanceof ImageInputGuardError);
      assert.match(error.message, expectedText);
      return true;
    },
  );
}

async function runCommand(command, args, options) {
  return await new Promise((resolve, reject) => {
    const child = spawn(command, args, options);
    let stdout = '';
    let stderr = '';
    child.stdout.on('data', (chunk) => {
      stdout += chunk;
    });
    child.stderr.on('data', (chunk) => {
      stderr += chunk;
    });
    child.on('error', reject);
    child.on('close', (exitCode) => resolve({exitCode, stdout, stderr}));
  });
}

function isoBaseMediaHeader(brand) {
  const header = Buffer.alloc(24);
  header.writeUInt32BE(24, 0);
  header.write('ftyp', 4, 'ascii');
  header.write(brand, 8, 'ascii');
  header.writeUInt32BE(0, 12);
  header.write(brand, 16, 'ascii');
  return header;
}

test('allows safe PNG, JPEG, SVG, and WebP inputs', async () => {
  await withFixture(async (root) => {
    const files = new Map([
      ['safe.png', Buffer.from('89504e470d0a1a0a00000000', 'hex')],
      ['safe.jpg', Buffer.from('ffd8ffe000104a464946', 'hex')],
      ['safe.svg', Buffer.from('<svg xmlns="http://www.w3.org/2000/svg"></svg>')],
      ['safe.webp', Buffer.from('524946460c00000057454250', 'hex')],
    ]);

    for (const [name, contents] of files) {
      await writeFile(path.join(root, name), contents);
    }

    const result = await scanImageInputs({inputRoots: [root]});
    assert.equal(result.filesScanned, files.size);
    assert.equal(result.violations.length, 0);
  });
});

test('defines and rejects every risky extension case-insensitively', async () => {
  assert.deepEqual([...RISKY_EXTENSIONS].sort(), EXPECTED_RISKY_EXTENSIONS);

  for (const extension of EXPECTED_RISKY_EXTENSIONS) {
    await withFixture(async (root) => {
      const caseVariant = extension
        .split('')
        .map((character, index) => (index % 2 === 0 ? character.toUpperCase() : character))
        .join('');
      await writeFile(path.join(root, `blocked${caseVariant}`), 'safe text');
      await expectGuardFailure(root, /blocked extension/i);
    });
  }
});

test('rejects ICNS content with a misleading safe extension', async () => {
  await withFixture(async (root) => {
    await writeFile(path.join(root, 'disguised.png'), Buffer.from('69636e7300000008', 'hex'));
    await expectGuardFailure(root, /ICNS signature/);
  });
});

test('rejects a raw JXL codestream with a misleading safe extension', async () => {
  await withFixture(async (root) => {
    await writeFile(path.join(root, 'disguised.jpg'), Buffer.from('ff0a00112233', 'hex'));
    await expectGuardFailure(root, /JXL codestream signature/);
  });
});

test('rejects a JXL container with a misleading safe extension', async () => {
  await withFixture(async (root) => {
    const signatureBox = Buffer.from('0000000c4a584c200d0a870a', 'hex');
    const ftypBox = isoBaseMediaHeader('jxl ');
    await writeFile(path.join(root, 'disguised.webp'), Buffer.concat([signatureBox, ftypBox]));
    await expectGuardFailure(root, /canonical JXL container signature/);
  });
});

test('rejects the noncanonical JXL container accepted by image-size before a zero-length jxlp can loop', async () => {
  await withFixture(async (root) => {
    const malformedContainer = Buffer.concat([
      Buffer.from('000000004a584c20', 'hex'), // zero-size JXL box
      Buffer.from('00000010667479706a786c2000000000', 'hex'), // ftyp/jxl
      Buffer.from('000000006a786c70', 'hex'), // zero-size jxlp box
    ]);
    await writeFile(path.join(root, 'misleading.png'), malformedContainer);
    await expectGuardFailure(root, /JXL container box type/);
  });
});

test('rejects common HEIF-family brands with misleading safe extensions', async () => {
  for (const brand of HEIF_BRANDS) {
    await withFixture(async (root) => {
      await writeFile(path.join(root, `${brand}.png`), isoBaseMediaHeader(brand));
      await expectGuardFailure(root, new RegExp(`HEIF-family brand ${brand}`));
    });
  }
});

test('does not reject an unrelated ISO base media brand', async () => {
  await withFixture(async (root) => {
    await writeFile(path.join(root, 'video.bin'), isoBaseMediaHeader('isom'));
    const result = await scanImageInputs({inputRoots: [root]});
    assert.equal(result.filesScanned, 1);
    assert.equal(result.violations.length, 0);
  });
});

test('reads at most the bounded header from each file', async () => {
  await withFixture(async (root) => {
    const filePath = path.join(root, 'large-safe.png');
    await writeFile(filePath, Buffer.alloc(HEADER_READ_LIMIT * 4, 0x41));

    const header = await readBoundedHeader(filePath);
    assert.equal(header.length, HEADER_READ_LIMIT);
  });
});

test('rejects directory symlink or junction traversal when supported', async (t) => {
  await withFixture(async (root) => {
    const outside = await mkdtemp(path.join(tmpdir(), 'datagen-image-guard-outside-'));
    try {
      await writeFile(path.join(outside, 'outside.png'), Buffer.from('89504e470d0a1a0a', 'hex'));
      const linkPath = path.join(root, 'linked-assets');
      try {
        await symlink(outside, linkPath, process.platform === 'win32' ? 'junction' : 'dir');
      } catch (error) {
        if (error?.code === 'EPERM' || error?.code === 'EACCES' || error?.code === 'ENOTSUP') {
          t.skip(`link creation is not supported: ${error.code}`);
          return;
        }
        throw error;
      }

      await expectGuardFailure(root, /symbolic link or junction/i);
    } finally {
      await rm(outside, {recursive: true, force: true});
    }
  });
});

test('scans nested directories recursively', async () => {
  await withFixture(async (root) => {
    const nested = path.join(root, 'one', 'two');
    await mkdir(nested, {recursive: true});
    await writeFile(path.join(nested, 'nested.PNG'), Buffer.from('icns0000'));
    await expectGuardFailure(root, /ICNS signature/);
  });
});

test('scans an unsafe nested source directory named build', async () => {
  await withFixture(async (root) => {
    const nestedBuild = path.join(root, 'docs', 'guides', 'build');
    await mkdir(nestedBuild, {recursive: true});
    await writeFile(path.join(nestedBuild, 'unsafe.png'), Buffer.from('icns0000'));
    await expectGuardFailure(root, /docs[\\/]guides[\\/]build[\\/]unsafe\.png.*ICNS signature/);
  });
});

test('excludes generated and dependency trees only at the website root', async () => {
  assert.deepEqual([...EXCLUDED_ROOT_DIRECTORY_NAMES].sort(), [
    '.docusaurus',
    '.git',
    'build',
    'node_modules',
  ]);

  await withFixture(async (root) => {
    const siteRoot = path.join(root, 'website');
    await mkdir(path.join(siteRoot, 'docs'), {recursive: true});
    await mkdir(path.join(siteRoot, 'node_modules', 'ignored'), {recursive: true});
    await mkdir(path.join(siteRoot, 'build', 'ignored'), {recursive: true});
    await writeFile(path.join(siteRoot, 'docs', 'safe.png'), Buffer.from('89504e470d0a1a0a', 'hex'));
    await writeFile(path.join(siteRoot, 'node_modules', 'ignored', 'unsafe.png'), Buffer.from('icns0000'));
    await writeFile(path.join(siteRoot, 'build', 'ignored', 'unsafe.png'), Buffer.from('icns0000'));

    const result = await scanImageInputs({rootDir: siteRoot});
    assert.equal(result.filesScanned, 1);
    assert.equal(result.violations.length, 0);
  });
});

test('npm build cannot bypass the guard when lifecycle scripts are disabled', async () => {
  const npmExecPath = process.env.npm_execpath;
  assert.ok(npmExecPath, 'test:security must provide npm_execpath');

  const websiteRoot = fileURLToPath(new URL('..', import.meta.url));
  const sourceFixture = await mkdtemp(path.join(websiteRoot, 'src', '.image-guard-boundary-'));
  const outputFixture = await mkdtemp(path.join(tmpdir(), 'datagen-site-build-boundary-'));
  try {
    const nestedBuild = path.join(sourceFixture, 'build');
    await mkdir(nestedBuild);
    await writeFile(path.join(nestedBuild, 'unsafe.png'), Buffer.from('icns0000'));

    const result = await runCommand(
      process.execPath,
      [npmExecPath, 'run', 'build', '--ignore-scripts', '--', '--out-dir', outputFixture],
      {cwd: websiteRoot, env: process.env, windowsHide: true},
    );

    assert.notEqual(result.exitCode, 0, 'the guarded build must reject unsafe source input');
    assert.match(`${result.stdout}\n${result.stderr}`, /Blocked unsafe documentation image input/);
    assert.doesNotMatch(result.stdout, /Creating an optimized production build/);
  } finally {
    await rm(sourceFixture, {recursive: true, force: true});
    await rm(outputFixture, {recursive: true, force: true});
  }
});
