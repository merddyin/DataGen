import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  docsSidebar: [
    'intro',
    {
      type: 'category',
      label: 'Getting Started',
      items: [
        'getting-started/installation',
        'getting-started/first-world',
        'getting-started/understanding-output',
      ],
    },
    {
      type: 'category',
      label: 'Walkthroughs',
      items: [
        'walkthroughs/general-enterprise-lab',
        'walkthroughs/active-directory-lab',
        'walkthroughs/entra-lab',
        'walkthroughs/hybrid-identity-lab',
        'walkthroughs/cmdb-rich-environment',
        'walkthroughs/security-discovery-lab',
        'walkthroughs/collaboration-and-repositories',
        'walkthroughs/plugin-extended-dataset',
      ],
    },
    {
      type: 'category',
      label: 'Cmdlet Reference',
      items: [
        'reference/cmdlets/scenario-authoring',
        'reference/cmdlets/generation-and-lifecycle',
        'reference/cmdlets/export-and-persistence',
        'reference/cmdlets/plugins',
      ],
    },
    {
      type: 'category',
      label: 'User Guides',
      items: [
        'guides/scenario-authoring',
        'guides/scenario-wizard',
        'guides/catalogs-and-generation',
        'guides/cmdb-and-observed-data',
        'guides/realism-and-deviations',
      ],
    },
    {
      type: 'category',
      label: 'Capabilities',
      items: [
        'capabilities/overview',
        'capabilities/identity-and-access',
        'capabilities/infrastructure-and-repositories',
        'capabilities/cmdb-and-observed',
        'capabilities/integrations-and-exports',
      ],
    },
    {
      type: 'category',
      label: 'SDK',
      items: [
        'sdk/overview',
        'sdk/plugin-architecture',
        'sdk/script-plugin-guide',
        'sdk/binary-plugin-guide',
        'sdk/examples',
      ],
    },
    {
      type: 'category',
      label: 'Integrations',
      items: [
        'integrations/overview',
        'integrations/normalized-export',
        'integrations/consumer-adapters',
      ],
    },
    {
      type: 'category',
      label: 'Troubleshooting',
      items: ['troubleshooting/common-issues'],
    },
    {
      type: 'category',
      label: 'About',
      items: [
        'about/product-roadmap',
        'about/contributing',
        'about/docs-site-implementation-plan',
      ],
    },
  ],
};

export default sidebars;
