# Website Dependency Disposition

**Status:** bounded acceptance for DataGen v0.9.4

**Owner:** DataGen maintainers and the release owner

**Review by:** 2026-09-30

## Scope

The website currently resolves this transitive path:

```text
@docusaurus/core@3.10.2
  -> @docusaurus/mdx-loader@3.10.2
    -> image-size@2.0.2
```

The affected package is used while Docusaurus determines dimensions for local
Markdown/MDX images. The current npm audit reports 17 high findings that
collapse to these two advisories:

- [GHSA-w3rx-r6r6-pgpr](https://github.com/advisories/GHSA-w3rx-r6r6-pgpr)
  affects ICNS parsing.
- [GHSA-5p2g-fcmc-qvqq](https://github.com/advisories/GHSA-5p2g-fcmc-qvqq)
  affects JXL and HEIF parsing.

Both describe an infinite-loop denial of service and affect `image-size <=
2.0.2`. No fixed upstream package is currently available, and npm reports no
automatic fix. The release does not claim that `npm audit` passes.

## Reachable Surfaces

- The package is reachable during a Docusaurus build and local Docusaurus
  development server.
- It is not part of the generated static-site runtime. The browser receives
  generated HTML, CSS, JavaScript, and assets, not the Node parser.
- It is not part of the published DataGen PowerShell module.

## Controls

`website/scripts/check-image-inputs.mjs` runs before the supported production
build. It scans website source inputs, rejects risky ICNS/JXL/HEIF-family
extensions and signatures, rejects symbolic-link or junction traversal, and
reads only a bounded header from each file. The 14-test security suite covers
these cases and proves that the explicit guard still runs when npm lifecycle
scripts are disabled.

Pull-request CI uses `npm run build` with `contents: read` and no release or
deployment secrets. Pages deployment runs from `main`, and the module release
workflow does not build the website. These controls reduce the release-path
exposure but are not a sandbox and do not protect a developer who directly
invokes Docusaurus or runs `npm start` against untrusted content.

## Acceptance and Action

This bounded acceptance is valid through **2026-09-30**. The maintainers should
re-review the advisory, dependency tree, guard tests, build, and workflow
permissions by that date. Replace the acceptance with an upstream fixed
dependency as soon as one is compatible with Docusaurus.

Re-review is required sooner if a fixed `image-size` or Docusaurus release
appears, the website accepts user/customer-uploaded content, CI gains
write-capable permissions or sensitive secrets, the dev server becomes a
shared or reachable service, or the image guard is weakened or bypassed.

Until then, keep the guard in the supported build path, keep the pull-request
workflow read-only, and do not add a forced npm override that violates the
Docusaurus dependency contract.
