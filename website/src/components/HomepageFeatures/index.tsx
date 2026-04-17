import type {ReactNode} from 'react';
import clsx from 'clsx';
import useBaseUrl from '@docusaurus/useBaseUrl';
import Heading from '@theme/Heading';
import styles from './styles.module.css';

type FeatureItem = {
  title: string;
  image: string;
  description: ReactNode;
};

const FeatureList: FeatureItem[] = [
  {
    title: 'Model complete enterprise worlds',
    image: '/img/site/feature-worlds.svg',
    description: (
      <>
        Generate identity, infrastructure, repositories, applications, CMDB
        views, policies, access evidence, and external ecosystem data from a
        single scenario definition.
      </>
    ),
  },
  {
    title: 'Drive labs and validation with the same dataset',
    image: '/img/site/feature-labs.svg',
    description: (
      <>
        Use the generated world to populate labs, seed exports, validate
        discovery tooling, and create richer demos without maintaining fragile
        hand-authored fixtures.
      </>
    ),
  },
  {
    title: 'Stay scenario-first and plugin-safe',
    image: '/img/site/feature-plugins.svg',
    description: (
      <>
        Author scenarios with templates, overlays, and the terminal wizard, then
        extend the dataset through plugins that add synthetic data rather than
        system-specific adapters.
      </>
    ),
  },
  {
    title: 'Dial realism without losing structure',
    image: '/img/site/feature-realism.svg',
    description: (
      <>
        Keep enterprise richness intact while choosing a deviation profile that
        ranges from clean to aggressively messy for labs, demos, and regression
        suites.
      </>
    ),
  },
];

function Feature({title, image, description}: FeatureItem) {
  const imageUrl = useBaseUrl(image);

  return (
    <div className={clsx('col col--6', styles.featureWrap)}>
      <div className={styles.featureCard}>
        <img className={styles.featureVisual} src={imageUrl} alt="" />
        <div>
          <Heading as="h3">{title}</Heading>
          <p>{description}</p>
        </div>
      </div>
    </div>
  );
}

export default function HomepageFeatures(): ReactNode {
  return (
    <section className={styles.features}>
      <div className="container">
        <div className={styles.sectionLead}>
          <Heading as="h2">Documentation shaped around real usage</Heading>
          <p>
            This site is organized for operators, lab builders, and SDK authors.
            It starts with scenarios and outputs, then moves into reference and
            extension surfaces.
          </p>
        </div>
        <div className="row">
          {FeatureList.map((props, idx) => (
            <Feature key={idx} {...props} />
          ))}
        </div>
      </div>
    </section>
  );
}
