# DataGen documentation site

This directory contains the Docusaurus-based documentation site for DataGen.

## Local development

```powershell
Set-Location .\website
npm install
npm run start
```

## Production build

```powershell
Set-Location .\website
npm run build
```

## Deployment

The repository includes a GitHub Actions workflow at:

- `E:\source\DataGen\.github\workflows\deploy-docs-site.yml`

That workflow builds the site from this `website` directory and deploys the static output to GitHub Pages.

## Content scope

The site is meant to publish:

- user-facing product documentation
- walkthroughs
- cmdlet reference
- SDK guidance
- contribution guidance

It intentionally excludes internal scratch areas such as `legacy_notes` and `tmp`.
