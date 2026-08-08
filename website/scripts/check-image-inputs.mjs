import {constants as fsConstants} from 'node:fs';
import {lstat, open, readdir} from 'node:fs/promises';
import path from 'node:path';
import {pathToFileURL} from 'node:url';

export const HEADER_READ_LIMIT = 64;

export const RISKY_EXTENSIONS = new Set([
  '.avci',
  '.avif',
  '.avifs',
  '.heic',
  '.heif',
  '.heifs',
  '.hif',
  '.icns',
  '.jxl',
]);

// Docusaurus/MDX may resolve @site/ and relative paths outside the usual
// docs/static/src roots. Scan the whole source tree, excluding only generated
// output and dependency trees that Docusaurus does not treat as site input.
export const DEFAULT_INPUT_ROOTS = ['.'];

export const EXCLUDED_ROOT_DIRECTORY_NAMES = new Set([
  '.docusaurus',
  '.git',
  'build',
  'node_modules',
]);

const HEIF_FAMILY_BRANDS = new Set([
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
]);

const JXL_CANONICAL_CONTAINER_SIGNATURE = Buffer.from(
  '0000000c4a584c200d0a870a',
  'hex',
);

export class ImageInputGuardError extends Error {
  constructor(violations) {
    const details = violations
      .map(({filePath, reason}) => `  - ${filePath}: ${reason}`)
      .join('\n');
    super(`Blocked unsafe documentation image input(s):\n${details}`);
    this.name = 'ImageInputGuardError';
    this.violations = violations;
  }
}

export async function readBoundedHeader(filePath) {
  const flags = fsConstants.O_RDONLY | (fsConstants.O_NOFOLLOW ?? 0);
  const file = await open(filePath, flags);
  try {
    const stat = await file.stat();
    if (!stat.isFile()) {
      throw new Error('path changed while being scanned and is no longer a regular file');
    }

    const header = Buffer.allocUnsafe(HEADER_READ_LIMIT);
    const {bytesRead} = await file.read(header, 0, HEADER_READ_LIMIT, 0);
    return header.subarray(0, bytesRead);
  } finally {
    await file.close();
  }
}

function startsWith(header, signature) {
  return (
    header.length >= signature.length &&
    header.subarray(0, signature.length).equals(signature)
  );
}

function signatureViolation(header) {
  if (header.length >= 4 && header.toString('ascii', 0, 4) === 'icns') {
    return 'ICNS signature reaches the vulnerable image-size parser';
  }

  if (header.length >= 2 && header[0] === 0xff && header[1] === 0x0a) {
    return 'JXL codestream signature reaches the vulnerable image-size parser';
  }

  if (startsWith(header, JXL_CANONICAL_CONTAINER_SIGNATURE)) {
    return 'canonical JXL container signature reaches the vulnerable image-size parser';
  }

  // image-size identifies a JXL container from this box type without first
  // validating the preceding box size or canonical signature. That accepts
  // malformed shapes such as a zero-length jxlp box, which can loop forever.
  if (header.length >= 8 && header.toString('ascii', 4, 8) === 'JXL ') {
    return 'JXL container box type reaches the vulnerable image-size parser';
  }

  if (header.length >= 12 && header.toString('ascii', 4, 8) === 'ftyp') {
    const brand = header.toString('ascii', 8, 12);
    if (HEIF_FAMILY_BRANDS.has(brand)) {
      return `HEIF-family brand ${brand} reaches the vulnerable image-size parser`;
    }
  }

  return undefined;
}

function displayPath(rootDir, filePath) {
  const relativePath = path.relative(rootDir, filePath);
  return relativePath && !relativePath.startsWith('..')
    ? relativePath
    : filePath;
}

export async function scanImageInputs({
  rootDir = process.cwd(),
  inputRoots,
} = {}) {
  const resolvedRootDir = path.resolve(rootDir);
  const roots = (inputRoots ?? DEFAULT_INPUT_ROOTS).map((inputRoot) =>
    path.resolve(resolvedRootDir, inputRoot),
  );
  const ignoreMissingRoots = inputRoots === undefined;
  const violations = [];
  let filesScanned = 0;
  let rootsScanned = 0;
  let headerBytesRead = 0;

  async function walk(currentPath) {
    const stat = await lstat(currentPath);
    const shownPath = displayPath(resolvedRootDir, currentPath);

    if (stat.isSymbolicLink()) {
      violations.push({
        filePath: shownPath,
        reason: 'symbolic link or junction traversal is not allowed',
      });
      return;
    }

    if (stat.isDirectory()) {
      const entries = await readdir(currentPath, {withFileTypes: true});
      entries.sort((left, right) => left.name.localeCompare(right.name, 'en'));
      for (const entry of entries) {
        const entryPath = path.join(currentPath, entry.name);
        if (entry.isSymbolicLink()) {
          violations.push({
            filePath: displayPath(resolvedRootDir, entryPath),
            reason: 'symbolic link or junction traversal is not allowed',
          });
          continue;
        }

        if (
          entry.isDirectory() &&
          currentPath === resolvedRootDir &&
          EXCLUDED_ROOT_DIRECTORY_NAMES.has(entry.name)
        ) {
          continue;
        }
        await walk(entryPath);
      }
      return;
    }

    if (!stat.isFile()) {
      violations.push({
        filePath: shownPath,
        reason: 'unsupported filesystem entry is not allowed',
      });
      return;
    }

    filesScanned += 1;
    const reasons = [];
    const extension = path.extname(currentPath).toLowerCase();
    if (RISKY_EXTENSIONS.has(extension)) {
      reasons.push(`blocked extension ${extension}`);
    }

    const header = await readBoundedHeader(currentPath);
    headerBytesRead += header.length;
    const signatureReason = signatureViolation(header);
    if (signatureReason) {
      reasons.push(signatureReason);
    }

    for (const reason of reasons) {
      violations.push({filePath: shownPath, reason});
    }
  }

  for (const root of roots) {
    try {
      await walk(root);
      rootsScanned += 1;
    } catch (error) {
      if (ignoreMissingRoots && error?.code === 'ENOENT') {
        continue;
      }
      throw error;
    }
  }

  if (violations.length > 0) {
    throw new ImageInputGuardError(violations);
  }

  return {
    filesScanned,
    headerBytesRead,
    rootsScanned,
    violations,
  };
}

async function main() {
  const result = await scanImageInputs();
  console.log(
    `Image input guard passed: ${result.filesScanned} files across ` +
      `${result.rootsScanned} roots; reads capped at ${HEADER_READ_LIMIT} bytes per file.`,
  );
}

const isMain =
  process.argv[1] &&
  import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href;

if (isMain) {
  main().catch((error) => {
    console.error(error instanceof Error ? error.message : error);
    process.exitCode = 1;
  });
}
