import type {ReactNode} from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';
import HomepageFeatures from '@site/src/components/HomepageFeatures';

import styles from './index.module.css';

const walkthroughs = [
  {
    title: 'AD Lab',
    description:
      'Build a directory-heavy enterprise with hybrid identity, stale accounts, tiered admin surfaces, and realistic OU and policy structure.',
    to: '/walkthroughs/active-directory-lab',
  },
  {
    title: 'Entra Tenant',
    description:
      'Generate an Entra-first tenant with guests, admin units, cross-tenant trust, Microsoft 365 collaboration, and cloud governance.',
    to: '/walkthroughs/entra-lab',
  },
  {
    title: 'CMDB-Rich World',
    description:
      'Produce canonical configuration items plus realistic CMDB, discovery, and service catalog drift for downstream validation.',
    to: '/walkthroughs/cmdb-rich-environment',
  },
];

function HomepageHeader() {
  const {siteConfig} = useDocusaurusContext();

  return (
    <header className={clsx('hero', styles.heroBanner)}>
      <div className="container">
        <div className={styles.heroGrid}>
          <div className={styles.heroCopy}>
            <div className={styles.heroBadge}>Synthetic Enterprise Labs</div>
            <Heading as="h1" className={styles.heroTitle}>
              {siteConfig.title}
            </Heading>
            <p className={styles.heroSubtitle}>{siteConfig.tagline}</p>
            <div className={styles.heroButtons}>
              <Link className="button button--primary button--lg" to="/getting-started/installation">
                Get Started
              </Link>
              <Link className="button button--secondary button--lg" to="/walkthroughs/general-enterprise-lab">
                Explore Walkthroughs
              </Link>
            </div>
            <div className={styles.heroStats}>
              <div>
                <strong>8</strong>
                <span>guided walkthroughs</span>
              </div>
              <div>
                <strong>4</strong>
                <span>cmdlet reference sections</span>
              </div>
              <div>
                <strong>1</strong>
                <span>GitHub Pages-ready site</span>
              </div>
            </div>
          </div>
          <div className={styles.heroVisualFrame}>
            <img
              className={styles.heroVisual}
              src="/img/site/architecture-flow.svg"
              alt="DataGen architecture and workflow overview"
            />
          </div>
        </div>
      </div>
    </header>
  );
}

function WalkthroughHighlights(): ReactNode {
  return (
    <section className={styles.walkthroughSection}>
      <div className="container">
        <div className={styles.sectionHeader}>
          <Heading as="h2">Built for real working scenarios</Heading>
          <p>
            The docs lead with repeatable workflows: install the module, author a
            scenario, generate a world, export it, and use the data to populate
            labs or validate consumer tooling.
          </p>
        </div>
        <div className={styles.walkthroughCards}>
          {walkthroughs.map((walkthrough) => (
            <Link
              key={walkthrough.title}
              className={styles.walkthroughCard}
              to={walkthrough.to}>
              <div className={styles.walkthroughCardLabel}>Walkthrough</div>
              <Heading as="h3">{walkthrough.title}</Heading>
              <p>{walkthrough.description}</p>
            </Link>
          ))}
        </div>
      </div>
    </section>
  );
}

function DocumentationFlow(): ReactNode {
  return (
    <section className={styles.flowSection}>
      <div className="container">
        <div className={styles.sectionHeader}>
          <Heading as="h2">A clean path from scenario to usable data</Heading>
          <p>
            DataGen is intentionally focused on synthetic data generation. The
            docs emphasize a consistent flow so teams can build labs, exports,
            SDK extensions, and downstream adapters without guessing where each
            concern belongs.
          </p>
        </div>
        <div className={styles.flowPanel}>
          <img
            src="/img/site/data-pipeline.svg"
            alt="DataGen flow from scenario authoring to world generation and exports"
          />
        </div>
      </div>
    </section>
  );
}

export default function Home(): ReactNode {
  const {siteConfig} = useDocusaurusContext();

  return (
    <Layout
      title={siteConfig.title}
      description="Documentation for DataGen, the synthetic enterprise data generation platform.">
      <HomepageHeader />
      <main>
        <HomepageFeatures />
        <DocumentationFlow />
        <WalkthroughHighlights />
      </main>
    </Layout>
  );
}
