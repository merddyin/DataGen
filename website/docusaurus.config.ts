import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const organizationName = process.env.GITHUB_OWNER ?? 'your-github-org';
const projectName = process.env.GITHUB_REPO ?? 'DataGen';
const url = process.env.DOCS_SITE_URL ?? `https://${organizationName}.github.io`;
const baseUrl = process.env.DOCS_BASE_URL ?? `/${projectName}/`;
const editUrl = `https://github.com/${organizationName}/${projectName}/edit/main/website/`;

const config: Config = {
  title: 'DataGen',
  tagline:
    'Synthetic enterprise data generation for labs, validation, demos, exports, and downstream integration.',
  favicon: 'img/favicon.ico',
  future: {
    v4: true,
  },
  url,
  baseUrl,
  organizationName,
  projectName,
  onBrokenLinks: 'throw',
  trailingSlash: false,
  markdown: {
    hooks: {
      onBrokenMarkdownLinks: 'throw',
    },
  },
  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },
  presets: [
    [
      'classic',
      {
        docs: {
          routeBasePath: '/',
          sidebarPath: './sidebars.ts',
          editUrl,
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],
  themeConfig: {
    image: 'img/site/social-card.svg',
    colorMode: {
      defaultMode: 'dark',
      respectPrefersColorScheme: true,
      disableSwitch: false,
    },
    navbar: {
      title: 'DataGen Docs',
      hideOnScroll: true,
      logo: {
        alt: 'DataGen logo',
        src: 'img/site/logo-datagen.svg',
      },
      items: [
        {
          type: 'doc',
          docId: 'intro',
          position: 'left',
          label: 'Overview',
        },
        {
          to: '/walkthroughs/general-enterprise-lab',
          position: 'left',
          label: 'Walkthroughs',
        },
        {
          to: '/reference/cmdlets/scenario-authoring',
          position: 'left',
          label: 'Cmdlets',
        },
        {
          to: '/sdk/overview',
          position: 'left',
          label: 'SDK',
        },
        {
          href: `https://github.com/${organizationName}/${projectName}`,
          label: 'GitHub',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Start Here',
          items: [
            {label: 'Overview', to: '/intro'},
            {label: 'Installation', to: '/getting-started/installation'},
            {label: 'First World', to: '/getting-started/first-world'},
          ],
        },
        {
          title: 'Build With It',
          items: [
            {label: 'Walkthroughs', to: '/walkthroughs/general-enterprise-lab'},
            {label: 'Cmdlet Reference', to: '/reference/cmdlets/scenario-authoring'},
            {label: 'Normalized Export', to: '/integrations/normalized-export'},
          ],
        },
        {
          title: 'Extend It',
          items: [
            {label: 'Plugin SDK', to: '/sdk/plugin-architecture'},
            {label: 'Contribution Guide', to: '/about/contributing'},
            {label: 'Implementation Plan', to: '/about/docs-site-implementation-plan'},
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} DataGen contributors. Built with Docusaurus.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['powershell', 'json', 'bash'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
